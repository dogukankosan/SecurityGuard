using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Core.Data
{
    /// <summary>
    /// DB şemasını koddaki en son sürüme getirir. Her sürüm kendi transaction'ında uygulanır:
    /// ya tamamen uygulanır ya hiç uygulanmaz. Var olan DB'ye sürüm uygulanmadan önce yedeği alınır.
    /// </summary>
    internal static class SchemaMigrator
    {
        private const string Module = "Şema";

        /// <returns>Uygulama sonrası şema sürümü.</returns>
        public static int Migrate(SQLiteConnection conn, string backupFolder)
        {
            conn.Execute(@"CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version     INTEGER NOT NULL PRIMARY KEY,
                Description TEXT    NOT NULL,
                AppliedUtc  TEXT    NOT NULL)");

            int current = conn.Scalar<int>("SELECT IFNULL(MAX(Version), 0) FROM SchemaVersion");
            int latest = Migrations.All.Max(m => m.Version);

            if (current > latest)
            {
                // Yeni sürümün DB'si eski programla açılmış: hiçbir şeye dokunma
                AppLog.Warn(Module, "Veritabanı şeması (v" + current + ") programdan (v" + latest + ") daha yeni. Şemaya dokunulmadı.");
                return current;
            }

            if (current == latest)
                return current;

            if (current > 0)
                BackupBeforeMigrate(conn, backupFolder, current);

            foreach (var m in Migrations.All.Where(x => x.Version > current).OrderBy(x => x.Version))
            {
                try
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        conn.Execute(m.Sql);
                        conn.Execute("INSERT INTO SchemaVersion (Version, Description, AppliedUtc) VALUES (@v, @d, @t)",
                            ("@v", m.Version), ("@d", m.Description), ("@t", DbTime.Now()));
                        tx.Commit();
                    }
                    AppLog.Info(Module, "Şema v" + m.Version + " uygulandı: " + m.Description);
                    current = m.Version;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Şema v" + m.Version + " (" + m.Description + ") uygulanamadı.", ex);
                }
            }

            return current;
        }

        private static void BackupBeforeMigrate(SQLiteConnection conn, string backupFolder, int currentVersion)
        {
            string file = Path.Combine(backupFolder,
                "guard_v" + currentVersion + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".db");

            using (var target = new SQLiteConnection("Data Source=" + file + ";Pooling=False"))
            {
                target.Open();
                conn.BackupDatabase(target, "main", "main", -1, null, 0);
            }
            AppLog.Info(Module, "Şema güncellemesi öncesi yedek alındı: " + file);
        }
    }
}
