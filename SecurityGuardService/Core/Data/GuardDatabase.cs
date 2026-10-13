using System;
using System.Data.SQLite;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Core.Data
{
    /// <summary>
    /// guard.db erişim noktası.
    /// - Yazmalar: sadece <see cref="Writer"/> üzerinden (tek yazıcı).
    /// - Okumalar: <see cref="Read{T}"/> ile, her okuma kendi bağlantısında (WAL sayesinde yazmayı beklemez).
    /// - Servis çalıştığı sürece bir bağlantı sürekli açık tutulur: Windows dosyayı kilitler,
    ///   DB silinemez ve adı değiştirilemez.
    /// </summary>
    internal sealed class GuardDatabase : IDisposable
    {
        private const string Module = "Veritabanı";
        private const int BusyTimeoutMs = 5000;

        private readonly string _connectionString;
        private readonly string _backupFolder;
        private SQLiteConnection _lockConnection;

        public GuardDatabase(string databaseFile, string backupFolder)
        {
            _backupFolder = backupFolder;
            _connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = databaseFile,
                JournalMode = SQLiteJournalModeEnum.Wal,
                SyncMode = SynchronizationModes.Normal,
                ForeignKeys = true,
                Pooling = true,
                DefaultTimeout = 30
            }.ToString();
        }

        public DbWriter Writer { get; private set; }

        public int SchemaVersion { get; private set; }

        /// <summary>DB'yi açar (yoksa oluşturur), şemayı günceller, yazıcıyı başlatır.</summary>
        public void Open()
        {
            _lockConnection = OpenConnection();
            SchemaVersion = SchemaMigrator.Migrate(_lockConnection, _backupFolder);

            Writer = new DbWriter(OpenConnection);
            Writer.Start();
        }

        /// <summary>Okuma yapar. Bağlantı açılıp kapanır (havuzdan gelir, maliyeti düşüktür).</summary>
        public T Read<T>(Func<SQLiteConnection, T> query)
        {
            using (var conn = OpenConnection())
                return query(conn);
        }

        private SQLiteConnection OpenConnection()
        {
            var conn = new SQLiteConnection(_connectionString);
            try
            {
                conn.Open();
                conn.Execute("PRAGMA busy_timeout = " + BusyTimeoutMs);
                return conn;
            }
            catch
            {
                conn.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                if (Writer != null) Writer.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Yazıcı kapatılamadı.", ex);
            }

            try
            {
                if (_lockConnection != null)
                {
                    // WAL içeriğini ana dosyaya aktar: kapanışta guard.db tek başına tutarlı olsun
                    _lockConnection.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
                    _lockConnection.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Kapanışta WAL aktarımı yapılamadı (bir sonraki açılışta otomatik yapılır).", ex);
            }
            _lockConnection = null;
            SQLiteConnection.ClearAllPools();
        }
    }
}
