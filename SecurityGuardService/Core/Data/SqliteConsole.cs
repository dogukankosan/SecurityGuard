using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Security;

namespace SecurityGuardService.Core.Data
{
    /// <summary>
    /// SQLite sorgu ekranı: yönetici guard.db üzerinde istediği SQL'i çalıştırır (yetki kısıtı yok, bilinçli karar).
    /// DB dosyasının kendisi korunur: servis çalıştıkça dosya kilitli olduğu için SQL ile silinemez / adı değiştirilemez.
    ///
    /// Çalıştırma yolu:
    /// - Sadece SELECT / EXPLAIN → ayrı okuma bağlantısı (servisin yazmalarını bekletmez).
    /// - Değişiklik yapan ifadeler → tek yazıcı kuyruğu üzerinden, tek parça: bir ifade hata verirse
    ///   aynı çalıştırmadaki tüm değişiklikler geri alınır.
    /// - Transaction içinde çalışamayan ifadeler (VACUUM, BEGIN/COMMIT, ATTACH, PRAGMA, SAVEPOINT...) → ayrı bağlantı;
    ///   açık kalan transaction çalıştırma sonunda geri alınır.
    /// </summary>
    internal sealed class SqliteConsole
    {
        private const string Module = "SQLiteKonsol";
        private const int MaxSqlLength = 1024 * 1024;
        private const int MaxRowsPerSet = 5000;
        private const int MaxResultSets = 20;
        private const int MaxCellChars = 20000;
        private const int MaxBlobPreviewBytes = 64;
        private const int CommandTimeoutSeconds = 120;

        // Metin, tırnaklı ad ve yorumlar sınıflandırmadan önce boşlukla değiştirilir (içlerindeki ; ve kelimeler sayılmasın)
        private static readonly Regex StringsAndComments = new Regex(
            @"'([^']|'')*'?|""[^""]*""?|\[[^\]]*\]?|`[^`]*`?|--[^\n]*|/\*.*?(\*/|$)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex FirstWord = new Regex(@"^\s*([A-Za-z]+)", RegexOptions.Compiled);

        private static readonly HashSet<string> ReadOnlyStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SELECT", "EXPLAIN", "VALUES" };

        private static readonly HashSet<string> OutsideTransactionStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "VACUUM", "BEGIN", "COMMIT", "END", "ROLLBACK", "ATTACH", "DETACH", "PRAGMA", "SAVEPOINT", "RELEASE"
        };

        private static readonly Regex EventLogTable = new Regex(@"\bEventLog\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly GuardDatabase _db;
        private readonly HmacChain _chain;

        public SqliteConsole(GuardDatabase db, HmacChain chain = null)
        {
            _db = db;
            _chain = chain;
        }

        private enum Route { Read, Writer, Separate }

        /// <summary>SQL'i çalıştırır. <paramref name="changed"/>: değişiklik yapmış olabilir (önbellekler yenilenmeli).</summary>
        public async Task<SqliteExecuteResultDto> ExecuteAsync(string sql, string userName, Action changed)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new GuardException(ErrorCodes.Validation, "Çalıştırılacak SQL yok.");
            if (sql.Length > MaxSqlLength)
                throw new GuardException(ErrorCodes.Validation, "SQL çok uzun (en fazla " + (MaxSqlLength / 1024) + " KB).");

            var route = Classify(sql);
            bool tracksEventLog = route != Route.Read && EventLogTable.IsMatch(sql);
            string detail = Shorten(sql.Trim(), 1000);
            var sw = Stopwatch.StartNew();
            SqliteExecuteResultDto result;
            try
            {
                switch (route)
                {
                    case Route.Read:
                        result = await Task.Run(() => _db.Read(conn => Run(conn, sql))).ConfigureAwait(false);
                        break;
                    case Route.Writer:
                        if (!tracksEventLog)
                        {
                            result = await _db.Writer.EnqueueAsync(conn => Run(conn, sql)).ConfigureAwait(false);
                            break;
                        }
                        // Olay loguna dokunuyorsa: silinen / değişen kayıtlar aynı transaction'da imzalı olarak kaydedilir
                        result = await _db.Writer.EnqueueAsync(conn =>
                        {
                            var before = EventLogAdminChanges.Snapshot(conn);
                            var r = Run(conn, sql);
                            Report(r, EventLogAdminChanges.Record(conn, _chain, before, EventLogAdminChanges.Snapshot(conn), userName));
                            return r;
                        }).ConfigureAwait(false);
                        break;
                    default:
                        if (!tracksEventLog)
                        {
                            result = await Task.Run(() => _db.Read(conn => RunSeparate(conn, sql))).ConfigureAwait(false);
                            break;
                        }
                        Dictionary<long, string> snapBefore = null, snapAfter = null;
                        result = await Task.Run(() => _db.Read(conn =>
                        {
                            snapBefore = EventLogAdminChanges.Snapshot(conn);
                            try
                            {
                                return RunSeparate(conn, sql);
                            }
                            finally
                            {
                                snapAfter = EventLogAdminChanges.Snapshot(conn);
                                var b = snapBefore;
                                var a = snapAfter;
                                // Ayrı bağlantıda hata olsa bile kısmen uygulanmış olabilir: fark her durumda kaydedilir
                                _db.Writer.EnqueueAsync(c => EventLogAdminChanges.Record(c, _chain, b, a, userName)).Wait();
                            }
                        })).ConfigureAwait(false);
                        Report(result, Diff(snapBefore, snapAfter));
                        break;
                }
            }
            catch (Exception ex)
            {
                var sqlite = ex as SQLiteException ?? ex.InnerException as SQLiteException;
                string message = sqlite != null ? Explain(sqlite) : ex.Message;
                if (route == Route.Writer) message += Environment.NewLine + "Bu çalıştırmadaki hiçbir değişiklik uygulanmadı (tamamı geri alındı).";

                AppLog.Warn(Module, "Sorgu hata verdi (" + userName + "): " + message);
                AuditTrail.Record(_db, userName, "SQLite sorgusu hata verdi", detail);
                if (route != Route.Read) changed(); // ayrı bağlantıda kısmen uygulanmış olabilir
                throw new GuardException(ErrorCodes.InvalidRequest, message);
            }

            result.ElapsedMs = sw.ElapsedMilliseconds;
            AppLog.Info(Module, "Sorgu çalıştırıldı (" + userName + ", " + result.ElapsedMs + " ms, etkilenen " + result.RecordsAffected + ").");
            AuditTrail.Record(_db, userName, "SQLite sorgusu çalıştırıldı", detail);
            if (route != Route.Read) changed();
            return result;
        }

        public SqliteSchemaDto GetSchema(int schemaVersion)
        {
            var tables = _db.Read(conn =>
            {
                var list = new List<SqliteTableDto>();
                using (var cmd = conn.Create("SELECT name, type FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY type, name"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new SqliteTableDto { Name = r.GetString(0), Type = r.GetString(1), RowCount = -1, Columns = new List<SqliteColumnDto>() });
                }

                foreach (var t in list)
                {
                    string quoted = "\"" + t.Name.Replace("\"", "\"\"") + "\"";
                    using (var cmd = conn.Create("PRAGMA table_info(" + quoted + ")"))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            t.Columns.Add(new SqliteColumnDto
                            {
                                Name = Convert.ToString(r["name"], CultureInfo.InvariantCulture),
                                Type = Convert.ToString(r["type"], CultureInfo.InvariantCulture),
                                NotNull = Convert.ToInt64(r["notnull"], CultureInfo.InvariantCulture) != 0,
                                IsPrimaryKey = Convert.ToInt64(r["pk"], CultureInfo.InvariantCulture) != 0
                            });
                        }
                    }
                    if (t.Type == "table")
                        t.RowCount = conn.Scalar<long>("SELECT count(*) FROM " + quoted);
                }
                return list;
            });

            long size = 0;
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var fi = new FileInfo(AppPaths.DatabaseFile + suffix);
                if (fi.Exists) size += fi.Length;
            }

            return new SqliteSchemaDto
            {
                FilePath = AppPaths.DatabaseFile,
                FileSizeBytes = size,
                SchemaVersion = schemaVersion,
                Tables = tables
            };
        }

        private static Tuple<int, int> Diff(Dictionary<long, string> before, Dictionary<long, string> after)
        {
            int deleted = 0, modified = 0;
            foreach (var p in before)
            {
                string v;
                if (!after.TryGetValue(p.Key, out v)) deleted++;
                else if (v != p.Value) modified++;
            }
            return Tuple.Create(deleted, modified);
        }

        private static void Report(SqliteExecuteResultDto result, Tuple<int, int> changes)
        {
            if (changes.Item1 == 0 && changes.Item2 == 0) return;
            string text = "Olay logunda " + (changes.Item1 > 0 ? changes.Item1 + " kayıt silindi" : "")
                          + (changes.Item1 > 0 && changes.Item2 > 0 ? ", " : "") + (changes.Item2 > 0 ? changes.Item2 + " kayıt değiştirildi" : "")
                          + ". Bütünlük kontrolünde \"yönetici tarafından (SQLite Sorgu ekranı)\" olarak görünecek, oynama alarmı vermeyecek.";
            AppLog.Warn(Module, text);
            result.Message = string.IsNullOrEmpty(result.Message) ? text : result.Message + Environment.NewLine + text;
        }

        // ================================================================ Sınıflandırma

        private static Route Classify(string sql)
        {
            string clean = StringsAndComments.Replace(sql, m => new string(' ', m.Length));
            var starts = clean.Split(';')
                .Select(s => FirstWord.Match(s))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .ToList();

            if (starts.Count == 0) return Route.Read; // sadece yorum / boşluk
            if (starts.Any(w => OutsideTransactionStarts.Contains(w))) return Route.Separate;
            if (starts.All(w => ReadOnlyStarts.Contains(w))) return Route.Read;
            return Route.Writer;
        }

        // ================================================================ Çalıştırma

        private static SqliteExecuteResultDto RunSeparate(SQLiteConnection conn, string sql)
        {
            try
            {
                return Run(conn, sql); // başarılıysa açık kalan transaction'ı Run geri alır ve bildirir
            }
            catch
            {
                RollbackIfOpen(conn, null);
                throw;
            }
        }

        /// <summary>Kullanıcının BEGIN ile açıp kapatmadığı transaction bağlantı havuza dönmeden geri alınır.</summary>
        private static void RollbackIfOpen(SQLiteConnection conn, SqliteExecuteResultDto result)
        {
            if (conn.AutoCommit) return;
            try
            {
                conn.Execute("ROLLBACK");
                if (result != null) result.Message = "Açık kalan transaction (COMMIT edilmemiş) geri alındı.";
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Açık kalan transaction geri alınamadı.", ex);
            }
        }

        private static SqliteExecuteResultDto Run(SQLiteConnection conn, string sql)
        {
            var result = new SqliteExecuteResultDto { ResultSets = new List<SqliteResultSetDto>() };
            bool autoCommitBefore = conn.AutoCommit;

            // RecordsAffected CREATE gibi ifadelerde önceki değeri tekrar sayar; bağlantının toplam değişiklik farkı kesin sonuçtur
            long changesBefore = conn.Scalar<long>("SELECT total_changes()");

            using (var cmd = new SQLiteCommand(sql, conn) { CommandTimeout = CommandTimeoutSeconds })
            using (var r = cmd.ExecuteReader())
            {
                do
                {
                    if (r.FieldCount == 0) continue;
                    if (result.ResultSets.Count >= MaxResultSets)
                    {
                        while (r.Read()) { } // ifadeler yine de çalışsın
                        continue;
                    }

                    var set = new SqliteResultSetDto { Columns = new List<string>(), Rows = new List<List<string>>() };
                    for (int i = 0; i < r.FieldCount; i++) set.Columns.Add(r.GetName(i));

                    while (r.Read())
                    {
                        set.TotalRows++;
                        if (set.Rows.Count >= MaxRowsPerSet)
                        {
                            set.Truncated = true;
                            continue;
                        }
                        var row = new List<string>(r.FieldCount);
                        for (int i = 0; i < r.FieldCount; i++) row.Add(Format(r.GetValue(i)));
                        set.Rows.Add(row);
                    }
                    result.ResultSets.Add(set);
                }
                while (r.NextResult());
            }
            result.RecordsAffected = (int)(conn.Scalar<long>("SELECT total_changes()") - changesBefore);

            // Ayrı bağlantıda kullanıcının açtığı transaction kapanmadıysa geri al ve bildir
            if (autoCommitBefore) RollbackIfOpen(conn, result);
            return result;
        }

        private static string Format(object value)
        {
            if (value == null || value is DBNull) return null;

            var bytes = value as byte[];
            if (bytes != null)
            {
                var sb = new StringBuilder("0x");
                foreach (var b in bytes.Take(MaxBlobPreviewBytes)) sb.Append(b.ToString("X2"));
                if (bytes.Length > MaxBlobPreviewBytes) sb.Append("… ");
                sb.Append(" (" + bytes.Length + " bayt)");
                return sb.ToString();
            }

            string text;
            if (value is double) text = ((double)value).ToString("R", CultureInfo.InvariantCulture);
            else if (value is float) text = ((float)value).ToString("R", CultureInfo.InvariantCulture);
            else if (value is DateTime) text = ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            else text = Convert.ToString(value, CultureInfo.InvariantCulture);

            return text.Length > MaxCellChars ? text.Substring(0, MaxCellChars) + "… (" + text.Length + " karakter)" : text;
        }

        private static string Explain(SQLiteException ex)
        {
            string msg = ex.Message;
            // "SQL logic error\r\nno such table: X" → sadece anlamlı kısım
            int nl = msg.LastIndexOf('\n');
            string core = nl >= 0 ? msg.Substring(nl + 1).Trim() : msg.Trim();

            switch (ex.ResultCode)
            {
                case SQLiteErrorCode.Busy:
                case SQLiteErrorCode.Locked:
                    return "Veritabanı şu an meşgul (başka bir yazma sürüyor). Biraz sonra tekrar deneyin. (" + core + ")";
                case SQLiteErrorCode.Constraint:
                    return "Kısıt ihlali (benzersiz alan, NOT NULL veya yabancı anahtar): " + core;
                case SQLiteErrorCode.ReadOnly:
                    return "Veritabanı salt okunur: " + core;
                case SQLiteErrorCode.Interrupt:
                    return "Sorgu zaman aşımına uğradı (" + CommandTimeoutSeconds + " sn).";
                default:
                    return "SQLite hatası: " + core;
            }
        }

        private static string Shorten(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
