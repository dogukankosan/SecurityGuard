using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Checks;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Events;
using static SecurityGuardService.Checks.SqlQuery;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Sql
{
    /// <summary>
    /// SQL Bakımı: "sadece öner" ilkesinin kullanıcının bilerek açtığı tek istisnası (dokümandaki 9.1).
    /// - Varsayılan KAPALI; açılınca da önce deneme modunda (hiçbir şey uygulanmaz, ne yapılacağı kaydedilir).
    /// - Sadece seçilen DB'lerde, bakım penceresinde: index REBUILD / REORGANIZE, istatistik güncelleme,
    ///   isteğe bağlı şişmiş LOG dosyası küçültme. Veri dosyası asla küçültülmez.
    /// - Süre dolunca yeni işleme başlanmaz; süren işlem pencere sonunda iptal edilir (SQL geri alır).
    /// - Yedek alınıyorsa bekler; diskte yer yoksa REBUILD yerine REORGANIZE yapar.
    /// - Enterprise'da REBUILD ONLINE, diğerlerinde offline (tablo o sırada kilitli: bakım penceresi bu yüzden şart).
    /// - Servis kapanırsa süren işlem iptal edilir; hata güvenlik izlemeyi etkilemez.
    /// </summary>
    internal sealed class SqlMaintenance : IDisposable
    {
        private const string Module = "SQLBakım";
        private const int KeepRuns = 200;
        private static readonly TimeSpan BackupWaitMax = TimeSpan.FromMinutes(30);

        private readonly GuardDatabase _db;
        private readonly SettingsStore _settings;
        private readonly SqlConnectionStore _sql;
        private readonly EventRecorder _events;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _lock = new object();
        private Thread _scheduler;
        private bool _running;
        private string _progress;

        public SqlMaintenance(GuardDatabase db, SettingsStore settings, SqlConnectionStore sql, EventRecorder events)
        {
            _db = db;
            _settings = settings;
            _sql = sql;
            _events = events;
        }

        public void Start()
        {
            _scheduler = new Thread(SchedulerLoop) { IsBackground = true, Name = "SqlMaintenance" };
            _scheduler.Start();
        }

        // ================================================================ Ayarlar

        public MaintenanceConfigDto LoadConfig()
        {
            string json = _settings.Get(SettingKeys.MaintenanceConfig);
            if (string.IsNullOrEmpty(json)) return new MaintenanceConfigDto();
            try
            {
                return PipeProtocol.FromJson<MaintenanceConfigDto>(json) ?? new MaintenanceConfigDto();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Bakım ayarları okunamadı, bakım kapalı sayılıyor.", ex);
                return new MaintenanceConfigDto();
            }
        }

        public async Task SaveConfigAsync(MaintenanceConfigDto cfg, string userName)
        {
            if (cfg == null) throw new GuardException(ErrorCodes.InvalidRequest, "Ayar eksik.");
            cfg.Days = (cfg.Days ?? new List<int>()).Where(d => d >= 0 && d <= 6).Distinct().OrderBy(d => d).ToList();
            cfg.Databases = (cfg.Databases ?? new List<string>()).Where(d => !string.IsNullOrWhiteSpace(d) && d.Contains("|")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            TimeSpan start;
            if (!TryParseTime(cfg.StartTime, out start)) throw new GuardException(ErrorCodes.Validation, "Başlangıç saati SS:dd biçiminde olmalı (ör. 01:00).");
            cfg.StartTime = start.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            if (cfg.MaxMinutes < 15 || cfg.MaxMinutes > 720) throw new GuardException(ErrorCodes.Validation, "Bakım süresi 15 - 720 dakika arasında olmalı.");
            if (cfg.Enabled)
            {
                if (cfg.Days.Count == 0) throw new GuardException(ErrorCodes.Validation, "Bakım açıkken en az bir gün seçin.");
                if (cfg.Databases.Count == 0) throw new GuardException(ErrorCodes.Validation, "Bakım açıkken en az bir veritabanı seçin.");
                if (!cfg.Indexes && !cfg.Statistics && !cfg.ShrinkLog) throw new GuardException(ErrorCodes.Validation, "Bakım açıkken en az bir işlem seçin.");
            }

            var old = LoadConfig();
            await _settings.SetAsync(SettingKeys.MaintenanceConfig, PipeProtocol.ToJson(cfg)).ConfigureAwait(false);
            string text = Describe(cfg);
            if (text != Describe(old))
            {
                AuditTrail.Record(_db, userName, "SQL bakımı ayarları değişti", Describe(old) + " → " + text);
                AppLog.Info(Module, "Ayarlar değişti (" + userName + "): " + text);
            }
        }

        private static readonly string[] DayNames = { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" };

        private static string Describe(MaintenanceConfigDto c)
        {
            if (!c.Enabled) return "kapalı";
            var ops = new List<string>();
            if (c.Indexes) ops.Add("index");
            if (c.Statistics) ops.Add("istatistik");
            if (c.ShrinkLog) ops.Add("log küçültme");
            return "açık" + (c.DryRun ? " (deneme modu)" : "") + ", " + string.Join(" ", c.Days.Select(d => DayNames[d])) + " " + c.StartTime
                   + " / " + c.MaxMinutes + " dk, " + string.Join(" + ", ops) + ", DB: " + string.Join(", ", c.Databases.Select(d => d.Replace("|", "/")));
        }

        // ================================================================ UI durumu

        public MaintenanceStateDto GetState()
        {
            var cfg = LoadConfig();
            var state = new MaintenanceStateDto
            {
                Config = cfg,
                Databases = new List<MaintenanceDbDto>(),
                Notes = new List<string>(),
                OtherJobs = new List<string>(),
                Runs = LoadRuns(),
                Thresholds = "Parçalanma: %" + GeneralSettings.GetInt(_settings, SettingKeys.PerfFragReorganizePercent) + " üstü düzenlenir (REORGANIZE), %"
                             + GeneralSettings.GetInt(_settings, SettingKeys.PerfFragRebuildPercent) + " üstü yeniden oluşturulur (REBUILD); "
                             + Fmt.Num(GeneralSettings.GetInt(_settings, SettingKeys.PerfMinPageCount)) + " sayfadan küçük index'lere dokunulmaz. (Genel Ayarlar → Performans ve bakım)"
            };
            lock (_lock)
            {
                state.IsRunning = _running;
                state.Progress = _running ? _progress : null;
            }
            var next = NextWindow(cfg, DateTime.Now);
            state.NextRunUtc = cfg.Enabled && next != null ? DbTime.ToDb(next.Value.ToUniversalTime()) : null;

            var selected = new HashSet<string>(cfg.Databases, StringComparer.OrdinalIgnoreCase);
            foreach (var instance in Instances())
            {
                string cs = _sql.GetConnectionString(instance);
                if (cs == null) continue;
                try
                {
                    using (var conn = Open(cs))
                    {
                        int engine = (int)Long(Scalar(conn, "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)", CancellationToken.None));
                        state.Notes.Add(instance + ": " + (engine == 3
                            ? "Enterprise / Developer edition: REBUILD çevrimiçi (ONLINE) yapılır, tablolar kullanılabilir kalır."
                            : "Standard / Express edition: REBUILD sırasında tablo kilitlenir; bakım penceresini kimsenin çalışmadığı saate koyun."));

                        foreach (var r in Rows(conn, @"
SELECT d.name, d.recovery_model_desc,
       (SELECT SUM(CAST(size AS bigint)) FROM sys.master_files mf WHERE mf.database_id = d.database_id AND mf.type = 0) / 128.0,
       (SELECT SUM(CAST(size AS bigint)) FROM sys.master_files mf WHERE mf.database_id = d.database_id AND mf.type = 1) / 128.0
FROM sys.databases d WHERE d.database_id > 4 AND d.state = 0 AND d.is_read_only = 0 AND d.name <> 'distribution' ORDER BY d.name", CancellationToken.None))
                        {
                            state.Databases.Add(new MaintenanceDbDto
                            {
                                Instance = instance,
                                Name = Str(r[0]),
                                RecoveryModel = Str(r[1]),
                                DataMb = Dbl(r[2]),
                                LogMb = Dbl(r[3]),
                                Selected = selected.Contains(instance + "|" + Str(r[0]))
                            });
                        }

                        if (engine != 4) state.OtherJobs.AddRange(OtherMaintenanceJobs(conn).Select(j => instance + ": " + j));
                    }
                }
                catch (Exception ex)
                {
                    state.Notes.Add(instance + ": bağlanılamadı (" + ex.Message + ")");
                }
            }

            // Seçili ama artık bulunamayan DB'ler de görünsün (silinmiş / adı değişmiş)
            foreach (var key in cfg.Databases.Where(k => !state.Databases.Any(d => string.Equals(d.Instance + "|" + d.Name, k, StringComparison.OrdinalIgnoreCase))))
            {
                var parts = key.Split('|');
                state.Databases.Add(new MaintenanceDbDto { Instance = parts[0], Name = parts[1], RecoveryModel = "bulunamadı", Selected = true });
            }
            return state;
        }

        private static List<string> OtherMaintenanceJobs(SqlConnection conn)
        {
            var rows = Rows(conn, @"
SELECT j.name, j.enabled, s.step_name, s.subsystem, CAST(s.command AS nvarchar(4000))
FROM msdb.dbo.sysjobs j JOIN msdb.dbo.sysjobsteps s ON s.job_id = j.job_id", CancellationToken.None);
            return rows.GroupBy(r => Str(r[0]))
                .Where(g => Long(g.First()[1]) == 1)
                .Where(g => Regex.IsMatch(string.Join("\n", g.Select(r => Str(r[2]) + " " + Str(r[3]) + " " + Str(r[4]))),
                    @"REBUILD|REORGANIZE|IndexOptimize|UPDATE\s+STATISTICS|sp_updatestats|Rebuild Index|Reorganize Index|Maintenance ?Plan|SSIS|SHRINKFILE", RegexOptions.IgnoreCase))
                .Select(g => g.Key).ToList();
        }

        public List<MaintenanceOpDto> LoadOps(string runId)
        {
            return _db.Read(c =>
            {
                var list = new List<MaintenanceOpDto>();
                using (var cmd = c.Create(@"SELECT TimeUtc, DatabaseName, ObjectName, Operation, BeforeText, AfterText, DurationMs, Result, Error
                                            FROM MaintenanceOps WHERE RunId = @r ORDER BY Id", ("@r", runId)))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new MaintenanceOpDto
                        {
                            TimeUtc = r.GetString(0),
                            Database = r.IsDBNull(1) ? null : r.GetString(1),
                            ObjectName = r.IsDBNull(2) ? null : r.GetString(2),
                            Operation = r.GetString(3),
                            Before = r.IsDBNull(4) ? null : r.GetString(4),
                            After = r.IsDBNull(5) ? null : r.GetString(5),
                            DurationMs = r.IsDBNull(6) ? 0 : r.GetInt64(6),
                            Result = r.GetString(7),
                            Error = r.IsDBNull(8) ? null : r.GetString(8)
                        });
                }
                return list;
            });
        }

        private List<MaintenanceRunDto> LoadRuns()
        {
            return _db.Read(c =>
            {
                var list = new List<MaintenanceRunDto>();
                using (var cmd = c.Create("SELECT RunId, Instance, TriggerText, DryRun, StartedUtc, FinishedUtc, Status, Summary FROM MaintenanceRuns ORDER BY StartedUtc DESC LIMIT 50"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new MaintenanceRunDto
                        {
                            RunId = r.GetString(0),
                            Instance = r.GetString(1),
                            Trigger = r.IsDBNull(2) ? null : r.GetString(2),
                            DryRun = r.GetInt64(3) != 0,
                            StartedUtc = r.GetString(4),
                            FinishedUtc = r.IsDBNull(5) ? null : r.GetString(5),
                            Status = r.GetString(6),
                            Summary = r.IsDBNull(7) ? null : r.GetString(7)
                        });
                }
                return list;
            });
        }

        // ================================================================ Çalıştırma

        /// <summary>Elle çalıştırma: pencere beklenmez, süre sınırı yine geçerli. Sürüyorsa false.</summary>
        public bool RunNow(bool dryRun, string userName)
        {
            var cfg = LoadConfig();
            if (cfg.Databases.Count == 0) throw new GuardException(ErrorCodes.Validation, "Önce bakım yapılacak veritabanlarını seçip kaydedin.");
            if (!cfg.Indexes && !cfg.Statistics && !cfg.ShrinkLog) throw new GuardException(ErrorCodes.Validation, "Önce en az bir işlem seçip kaydedin.");
            bool started = TryStart(cfg, dryRun, (dryRun ? "elle deneme (" : "elle uygulama (") + userName + ")", DateTime.Now.AddMinutes(cfg.MaxMinutes));
            if (started) AuditTrail.Record(_db, userName, dryRun ? "SQL bakımı denemesi başlatıldı" : "SQL bakımı elle başlatıldı", Describe(cfg));
            return started;
        }

        private bool TryStart(MaintenanceConfigDto cfg, bool dryRun, string trigger, DateTime deadline)
        {
            lock (_lock)
            {
                if (_running || _cts.IsCancellationRequested) return false;
                _running = true;
                _progress = "Başlıyor";
            }
            new Thread(() => Execute(cfg, dryRun, trigger, deadline)) { IsBackground = true, Name = "SqlMaintenanceRun", Priority = ThreadPriority.BelowNormal }.Start();
            return true;
        }

        private void Execute(MaintenanceConfigDto cfg, bool dryRun, string trigger, DateTime deadline)
        {
            bool gate = false;
            try
            {
                // Yedekleme sürüyorsa bitmesi beklenir (pencere içinde); deneme modu sadece okuduğu için beklemez
                if (!dryRun)
                {
                    gate = SqlJobGate.Enter("SQL bakımı", deadline - DateTime.Now, _cts.Token, holder =>
                    {
                        SetProgress(holder + " sürüyor, bitmesi bekleniyor");
                        AppLog.Info(Module, holder + " sürüyor; bakım bitmesini bekliyor.");
                    });
                    if (!gate)
                    {
                        if (!_cts.IsCancellationRequested)
                            AppLog.Warn(Module, "Bakım yapılmadı: " + (SqlJobGate.Holder ?? "başka bir SQL işi") + " bakım penceresi içinde bitmedi.");
                        return;
                    }
                }
                foreach (var group in cfg.Databases.Select(d => d.Split('|')).GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase))
                {
                    if (_cts.IsCancellationRequested) return;
                    new Run(this, cfg, group.Key, group.Select(p => p[1]).ToList(), dryRun, trigger, deadline).Execute();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Bakım çalıştırılamadı.", ex);
            }
            finally
            {
                if (gate) SqlJobGate.Exit();
                lock (_lock)
                {
                    _running = false;
                    _progress = null;
                }
            }
        }

        private void SetProgress(string text)
        {
            lock (_lock) _progress = text;
        }

        /// <summary>Bir instance'ta bir bakım çalıştırması.</summary>
        private sealed class Run
        {
            private readonly SqlMaintenance _owner;
            private readonly MaintenanceConfigDto _cfg;
            private readonly string _instance;
            private readonly List<string> _dbs;
            private readonly bool _dry;
            private readonly string _trigger;
            private readonly DateTime _deadline;
            private readonly string _runId = Guid.NewGuid().ToString("N");
            private readonly CancellationToken _token;
            private int _done, _planned, _skipped, _errors;
            private bool _timeUp;
            private bool _online;

            public Run(SqlMaintenance owner, MaintenanceConfigDto cfg, string instance, List<string> dbs, bool dry, string trigger, DateTime deadline)
            {
                _owner = owner;
                _cfg = cfg;
                _instance = instance;
                _dbs = dbs;
                _dry = dry;
                _trigger = trigger;
                _deadline = deadline;
                _token = owner._cts.Token;
            }

            private bool TimeUp
            {
                get
                {
                    if (!_timeUp && DateTime.Now >= _deadline) _timeUp = true;
                    return _timeUp;
                }
            }

            /// <summary>Komut zaman aşımı: pencerenin kalanı (en az 30 sn).</summary>
            private int Remaining { get { return (int)Math.Max(30, Math.Min(int.MaxValue / 1000, (_deadline - DateTime.Now).TotalSeconds)); } }

            public void Execute()
            {
                var started = DateTime.UtcNow;
                _owner._db.Writer.EnqueueAsync(c => c.Execute(
                    "INSERT INTO MaintenanceRuns (RunId, Instance, TriggerText, DryRun, StartedUtc, Status) VALUES (@r, @i, @t, @d, @s, 'Sürüyor')",
                    ("@r", _runId), ("@i", _instance), ("@t", _trigger), ("@d", _dry ? 1 : 0), ("@s", DbTime.ToDb(started)))).Wait();
                AppLog.Info(Module, _instance + ": bakım başladı (" + (_dry ? "deneme, hiçbir şey uygulanmayacak" : "uygulama") + ", " + _trigger + ", bitiş en geç " + _deadline.ToString("HH:mm") + ").");

                string status;
                string fatal = null;
                try
                {
                    string cs = _owner._sql.GetConnectionString(_instance);
                    if (cs == null) throw new InvalidOperationException("SQL bağlantısı tanımlı değil veya izleme kapalı.");
                    using (var conn = Open(cs))
                    {
                        _online = Long(Scalar(conn, "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)", _token)) == 3;
                        if (WaitForBackups(conn))
                        {
                            foreach (var db in _dbs)
                            {
                                if (_token.IsCancellationRequested || TimeUp) break;
                                Database(conn, db);
                            }
                        }
                    }
                    status = _token.IsCancellationRequested ? "Yarıda kesildi" : TimeUp ? "Süre doldu" : _errors > 0 ? "Hatalı" : "Tamamlandı";
                }
                catch (Exception ex) when (_token.IsCancellationRequested)
                {
                    AppLog.Info(Module, _instance + ": bakım servis kapandığı için yarıda bırakıldı (" + ex.Message + ").");
                    status = "Yarıda kesildi";
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, _instance + ": bakım başarısız.", ex);
                    fatal = ex.Message;
                    _errors++;
                    status = "Hatalı";
                }

                string summary = (_dry
                        ? _planned + " işlem yapılacak (deneme: hiçbir şey uygulanmadı)"
                        : _done + " işlem yapıldı")
                    + (_skipped > 0 ? ", " + _skipped + " atlandı" : "") + (_errors > 0 ? ", " + _errors + " hata" : "")
                    + (_timeUp ? "; bakım süresi doldu, kalan işler bir sonraki bakımda" : "")
                    + (fatal != null ? ": " + fatal : "");
                try
                {
                    _owner._db.Writer.EnqueueAsync(c =>
                    {
                        c.Execute("UPDATE MaintenanceRuns SET FinishedUtc = @f, Status = @s, Summary = @m WHERE RunId = @r",
                            ("@f", DbTime.Now()), ("@s", status), ("@m", summary), ("@r", _runId));
                        c.Execute("DELETE FROM MaintenanceOps WHERE RunId IN (SELECT RunId FROM MaintenanceRuns ORDER BY StartedUtc DESC LIMIT -1 OFFSET @k)", ("@k", KeepRuns));
                        c.Execute("DELETE FROM MaintenanceRuns WHERE RunId IN (SELECT RunId FROM MaintenanceRuns ORDER BY StartedUtc DESC LIMIT -1 OFFSET @k)", ("@k", KeepRuns));
                    }).Wait(TimeSpan.FromSeconds(30));
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Bakım sonucu kaydedilemedi.", ex);
                }

                AppLog.Info(Module, _instance + ": bakım bitti (" + status + "): " + summary);
                AuditTrail.Record(_owner._db, "SecurityGuard", _dry ? "SQL bakımı denemesi" : "SQL bakımı", _instance + ": " + status + ", " + summary);

                bool failed = status == "Hatalı" || status == "Yarıda kesildi";
                var e = new GuardEvent(failed ? "Sql.MaintenanceFailed" : "Sql.MaintenanceDone",
                        _instance + " SQL bakımı" + (_dry ? " denemesi" : "") + ": " + status.ToLowerInvariant() + ". " + summary + ".")
                    { Source = "SQL Bakımı (" + _instance + ")" }
                    .With("Başlangıç", started.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))
                    .With("Süre", (int)(DateTime.UtcNow - started).TotalMinutes + " dk")
                    .With("Tetikleyen", _trigger)
                    .With("Ayrıntı", "SecurityGuard arayüzü → SQL Server → Bakım sayfasında her işlem tek tek görünür.");
                _owner._events.Record(e);
            }

            /// <summary>O an yedek / geri yükleme sürüyorsa bitmesini bekler (en fazla 30 dk, pencere içinde). false = bakım yapılmaz.</summary>
            private bool WaitForBackups(SqlConnection conn)
            {
                var waitUntil = DateTime.Now + BackupWaitMax;
                bool logged = false;
                while (true)
                {
                    var active = Rows(conn, "SELECT command, DB_NAME(database_id) FROM sys.dm_exec_requests WHERE command LIKE 'BACKUP%' OR command LIKE 'RESTORE%' OR command LIKE 'DBCC%'", _token)
                        .Select(r => Str(r[0]).Trim() + " " + Str(r[1])).ToList();
                    if (active.Count == 0) return true;
                    if (!logged)
                    {
                        Op(null, null, "BEKLEME", string.Join(", ", active), null, 0, "Bekleniyor", "Sunucuda yedek / geri yükleme / DBCC sürüyor; bitmesi bekleniyor (en fazla 30 dk).");
                        logged = true;
                    }
                    if (DateTime.Now >= waitUntil || TimeUp)
                    {
                        _skipped++;
                        Op(null, null, "BEKLEME", string.Join(", ", active), null, 0, "Atlandı", "Yedek / DBCC bitmedi; bakım bu sefer yapılmadı.");
                        return false;
                    }
                    _owner.SetProgress(_instance + ": yedek / DBCC bitmesi bekleniyor");
                    if (_token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1))) return false;
                }
            }

            private void Database(SqlConnection conn, string db)
            {
                var exists = Rows(conn, "SELECT state, is_read_only FROM sys.databases WHERE name = " + N(db), _token);
                if (exists.Count == 0 || Long(exists[0][0]) != 0 || Long(exists[0][1]) == 1)
                {
                    _skipped++;
                    Op(db, null, "VERİTABANI", null, null, 0, "Atlandı", exists.Count == 0 ? "Veritabanı bulunamadı (silinmiş / adı değişmiş olabilir)." : "Veritabanı çevrimdışı veya salt okunur.");
                    return;
                }
                conn.ChangeDatabase(db);
                try
                {
                    var rebuilt = new HashSet<string>();
                    if (_cfg.Indexes) Indexes(conn, db, rebuilt);
                    if (_cfg.Statistics && !TimeUp) Statistics(conn, db, rebuilt);
                    if (_cfg.ShrinkLog && !TimeUp) ShrinkLog(conn, db);
                }
                finally
                {
                    try { conn.ChangeDatabase("master"); } catch { }
                }
            }

            // ------------------------------------------------ Index

            private void Indexes(SqlConnection conn, string db, HashSet<string> rebuilt)
            {
                int reorg = GeneralSettings.GetInt(_owner._settings, SettingKeys.PerfFragReorganizePercent);
                int rebuild = GeneralSettings.GetInt(_owner._settings, SettingKeys.PerfFragRebuildPercent);
                int minPages = GeneralSettings.GetInt(_owner._settings, SettingKeys.PerfMinPageCount);
                _owner.SetProgress(_instance + " / " + db + ": index parçalanması ölçülüyor");

                var list = Rows(conn, @"
SELECT s.name, o.name, i.name, ps.object_id, ps.index_id, ps.partition_number,
       (SELECT COUNT(*) FROM sys.partitions p WHERE p.object_id = ps.object_id AND p.index_id = ps.index_id),
       ps.avg_fragmentation_in_percent, ps.page_count
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps
JOIN sys.indexes i ON i.object_id = ps.object_id AND i.index_id = ps.index_id
JOIN sys.objects o ON o.object_id = ps.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE ps.index_id > 0 AND ps.alloc_unit_type_desc = 'IN_ROW_DATA' AND o.is_ms_shipped = 0 AND i.is_disabled = 0 AND i.is_hypothetical = 0
  AND ps.page_count >= " + minPages + " AND ps.avg_fragmentation_in_percent >= " + reorg + @"
ORDER BY ps.avg_fragmentation_in_percent * ps.page_count DESC", _token, Remaining);

                double freeMb = FreeDiskMb(conn);
                int n = 0;
                foreach (var r in list)
                {
                    n++;
                    if (_token.IsCancellationRequested) return;
                    if (TimeUp)
                    {
                        _skipped += list.Count - n + 1;
                        Op(db, null, "INDEX", null, null, 0, "Atlandı", "Bakım süresi doldu: kalan " + (list.Count - n + 1) + " index bir sonraki bakımda.");
                        return;
                    }

                    string name = Str(r[0]) + "." + Str(r[1]) + "." + Str(r[2]);
                    double frag = Dbl(r[7]);
                    long pages = Long(r[8]);
                    double sizeMb = pages / 128.0;
                    int partition = (int)Long(r[5]);
                    bool partitioned = Long(r[6]) > 1;
                    if (partitioned) name += " (bölüm " + partition + ")";

                    bool doRebuild = frag >= rebuild;
                    string note = null;
                    if (doRebuild && freeMb >= 0 && freeMb < sizeMb * 1.5)
                    {
                        doRebuild = false;
                        note = "Diskte yeterli boş yer yok (" + Fmt.Mb(freeMb) + "): REBUILD yerine REORGANIZE yapıldı.";
                    }
                    string op = doRebuild ? "REBUILD" + (_online ? " ONLINE" : "") : "REORGANIZE";
                    string before = Fmt.Pct(frag) + " (" + Fmt.Mb(sizeMb) + ")";
                    _owner.SetProgress(_instance + " / " + db + ": " + op + " " + name + " (" + n + "/" + list.Count + ")");

                    if (_dry)
                    {
                        _planned++;
                        Op(db, name, op, before, null, 0, "Yapılacak (deneme)", note);
                        continue;
                    }

                    string target = Q(Str(r[2])) + " ON " + Q(Str(r[0])) + "." + Q(Str(r[1]));
                    string part = partitioned ? " PARTITION = " + partition : "";
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        if (doRebuild)
                        {
                            try
                            {
                                Exec(conn, "ALTER INDEX " + target + " REBUILD" + part + (_online ? " WITH (ONLINE = ON)" : ""));
                            }
                            catch (SqlException ex) when (_online && !_token.IsCancellationRequested && ex.Number != -2)
                            {
                                // Bazı index'ler (eski text / image kolonları vb.) çevrimiçi yeniden oluşturulamaz
                                note = "Çevrimiçi yapılamadı (" + ex.Message + "), çevrimdışı yapıldı.";
                                op = "REBUILD";
                                Exec(conn, "ALTER INDEX " + target + " REBUILD" + part);
                            }
                            rebuilt.Add(Long(r[3]) + ":" + Long(r[4]));
                        }
                        else
                        {
                            Exec(conn, "ALTER INDEX " + target + " REORGANIZE" + part);
                        }
                        sw.Stop();
                        var after = Rows(conn, "SELECT avg_fragmentation_in_percent, page_count FROM sys.dm_db_index_physical_stats(DB_ID(), " + Long(r[3]) + ", " + Long(r[4])
                                               + ", " + partition + ", 'LIMITED') WHERE alloc_unit_type_desc = 'IN_ROW_DATA'", _token, Remaining);
                        string afterText = after.Count == 0 ? null : Fmt.Pct(Dbl(after[0][0])) + " (" + Fmt.Mb(Long(after[0][1]) / 128.0) + ")";
                        _done++;
                        Op(db, name, op, before, afterText, sw.ElapsedMilliseconds, "Yapıldı", note);
                        if (doRebuild) freeMb = FreeDiskMb(conn);
                    }
                    catch (Exception ex)
                    {
                        if (!Fail(db, name, op, before, sw, ex)) return;
                    }
                }
                if (list.Count == 0) Op(db, null, "INDEX", null, null, 0, "Gerek yok", "Parçalanmış index yok.");
            }

            /// <summary>DB dosyalarının bulunduğu disklerdeki en az boş alan (MB); ölçülemezse -1.</summary>
            private double FreeDiskMb(SqlConnection conn)
            {
                try
                {
                    var v = Scalar(conn, "SELECT MIN(vs.available_bytes) / 1048576.0 FROM sys.database_files f CROSS APPLY sys.dm_os_volume_stats(DB_ID(), f.file_id) vs", _token);
                    return v == null ? -1 : Dbl(v);
                }
                catch (SqlException)
                {
                    return -1;
                }
            }

            // ------------------------------------------------ İstatistik

            private void Statistics(SqlConnection conn, string db, HashSet<string> rebuilt)
            {
                _owner.SetProgress(_instance + " / " + db + ": istatistikler inceleniyor");
                var list = Rows(conn, @"
SELECT s.name, o.name, st.name, st.object_id, st.stats_id, sp.last_updated, sp.rows, sp.modification_counter
FROM sys.stats st
JOIN sys.objects o ON o.object_id = st.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
CROSS APPLY sys.dm_db_stats_properties(st.object_id, st.stats_id) sp
WHERE o.type = 'U' AND o.is_ms_shipped = 0 AND sp.rows >= 1000 AND sp.modification_counter > 0
  AND (sp.modification_counter >= sp.rows * 0.2
       OR (sp.last_updated < DATEADD(DAY, -30, GETDATE()) AND sp.modification_counter >= sp.rows * 0.05))
ORDER BY sp.modification_counter DESC", _token, Remaining)
                    .Where(r => !rebuilt.Contains(Long(r[3]) + ":" + Long(r[4]))) // REBUILD edilen index'in istatistiği zaten güncel
                    .ToList();

                int n = 0;
                foreach (var r in list)
                {
                    n++;
                    if (_token.IsCancellationRequested) return;
                    if (TimeUp)
                    {
                        _skipped += list.Count - n + 1;
                        Op(db, null, "UPDATE STATISTICS", null, null, 0, "Atlandı", "Bakım süresi doldu: kalan " + (list.Count - n + 1) + " istatistik bir sonraki bakımda.");
                        return;
                    }
                    string name = Str(r[0]) + "." + Str(r[1]) + " (" + Str(r[2]) + ")";
                    string before = "son: " + Fmt.Date(Date(r[5])) + ", değişen " + Fmt.Num(Long(r[7])) + " / " + Fmt.Num(Long(r[6]));
                    _owner.SetProgress(_instance + " / " + db + ": istatistik " + name + " (" + n + "/" + list.Count + ")");
                    if (_dry)
                    {
                        _planned++;
                        Op(db, name, "UPDATE STATISTICS", before, null, 0, "Yapılacak (deneme)", null);
                        continue;
                    }
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        Exec(conn, "UPDATE STATISTICS " + Q(Str(r[0])) + "." + Q(Str(r[1])) + " " + Q(Str(r[2])));
                        _done++;
                        Op(db, name, "UPDATE STATISTICS", before, "güncellendi", sw.ElapsedMilliseconds, "Yapıldı", null);
                    }
                    catch (Exception ex)
                    {
                        if (!Fail(db, name, "UPDATE STATISTICS", before, sw, ex)) return;
                    }
                }
                if (list.Count == 0) Op(db, null, "UPDATE STATISTICS", null, null, 0, "Gerek yok", "Eski istatistik yok.");
            }

            // ------------------------------------------------ Log küçültme

            private void ShrinkLog(SqlConnection conn, string db)
            {
                var r = Rows(conn, @"
SELECT d.recovery_model_desc, d.log_reuse_wait_desc,
       (SELECT SUM(CAST(size AS bigint)) FROM sys.database_files WHERE type = 1) / 128.0,
       (SELECT SUM(CAST(size AS bigint)) FROM sys.database_files WHERE type = 0) / 128.0,
       (SELECT TOP 1 name FROM sys.database_files WHERE type = 1 ORDER BY file_id),
       (SELECT used_log_space_in_percent FROM sys.dm_db_log_space_usage)
FROM sys.databases d WHERE d.database_id = DB_ID()", _token)[0];
                string model = Str(r[0]), wait = Str(r[1]), logName = Str(r[4]);
                double logMb = Dbl(r[2]), dataMb = Dbl(r[3]), used = Dbl(r[5]);
                double target = Math.Max(1024, Math.Min(logMb / 4, Math.Max(1024, dataMb / 4)));
                string before = Fmt.Mb(logMb) + ", dolu " + Fmt.Pct(used);

                if (logMb < 1024 || logMb <= dataMb / 2 || used >= 25)
                {
                    Op(db, logName, "LOG SHRINK", before, null, 0, "Gerek yok", "Log dosyası şişmemiş.");
                    return;
                }
                if (model != "SIMPLE" && wait == "LOG_BACKUP")
                {
                    _skipped++;
                    Op(db, logName, "LOG SHRINK", before, null, 0, "Atlandı", model + " recovery ve log yedeği bekleniyor: önce log yedeği alınmalı (veya DB SIMPLE yapılmalı).");
                    return;
                }
                if (_dry)
                {
                    _planned++;
                    Op(db, logName, "LOG SHRINK", before, "hedef " + Fmt.Mb(target), 0, "Yapılacak (deneme)", null);
                    return;
                }
                _owner.SetProgress(_instance + " / " + db + ": log dosyası küçültülüyor");
                var sw = Stopwatch.StartNew();
                try
                {
                    Exec(conn, "CHECKPOINT; DBCC SHRINKFILE (" + N(logName) + ", " + (long)target + ") WITH NO_INFOMSGS;");
                    double after = Dbl(Scalar(conn, "SELECT SUM(CAST(size AS bigint)) / 128.0 FROM sys.database_files WHERE type = 1", _token));
                    _done++;
                    Op(db, logName, "LOG SHRINK", before, Fmt.Mb(after), sw.ElapsedMilliseconds, "Yapıldı",
                        after > target * 1.2 ? "Hedefe kadar küçülmedi: logun sonu kullanımda (sonraki bakımda tekrar denenir)." : null);
                }
                catch (Exception ex)
                {
                    Fail(db, logName, "LOG SHRINK", before, sw, ex);
                }
            }

            // ------------------------------------------------ Yardımcılar

            private void Exec(SqlConnection conn, string sql)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = Remaining;
                    using (_token.Register(() => { try { cmd.Cancel(); } catch { } }))
                        cmd.ExecuteNonQuery();
                }
                _token.ThrowIfCancellationRequested();
            }

            /// <returns>false = çalıştırma durmalı (servis kapanıyor / süre doldu).</returns>
            private bool Fail(string db, string name, string op, string before, Stopwatch sw, Exception ex)
            {
                sw.Stop();
                if (_token.IsCancellationRequested)
                {
                    Op(db, name, op, before, null, sw.ElapsedMilliseconds, "İptal", "Servis kapandığı için iptal edildi (SQL Server yarım işlemi geri aldı).");
                    return false;
                }
                var sql = ex as SqlException;
                if (sql != null && sql.Number == -2)
                {
                    _timeUp = true;
                    _skipped++;
                    Op(db, name, op, before, null, sw.ElapsedMilliseconds, "İptal", "Bakım süresi doldu, işlem iptal edildi (SQL Server yarım işlemi geri aldı).");
                    return false;
                }
                _errors++;
                AppLog.Warn(Module, _instance + " / " + db + ": " + op + " " + name + " başarısız: " + ex.Message);
                Op(db, name, op, before, null, sw.ElapsedMilliseconds, "Hata", ex.Message);
                return true;
            }

            private void Op(string db, string obj, string op, string before, string after, long ms, string result, string error)
            {
                _owner._db.Writer.Enqueue(c => c.Execute(
                    @"INSERT INTO MaintenanceOps (RunId, TimeUtc, DatabaseName, ObjectName, Operation, BeforeText, AfterText, DurationMs, Result, Error)
                      VALUES (@r, @t, @d, @o, @op, @b, @a, @ms, @res, @e)",
                    ("@r", _runId), ("@t", DbTime.Now()), ("@d", db), ("@o", obj), ("@op", op), ("@b", before), ("@a", after),
                    ("@ms", ms), ("@res", result), ("@e", error)));
            }
        }

        private static SqlConnection Open(string connectionString)
        {
            // Bakım uzun sürer: havuzdaki bağlantıları meşgul etmesin, kendi bağlantısı olsun
            var b = new SqlConnectionStringBuilder(connectionString) { Pooling = false, ApplicationName = "SecurityGuard Bakım" };
            var conn = new SqlConnection(b.ConnectionString);
            try
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SET DEADLOCK_PRIORITY LOW; SET LOCK_TIMEOUT 60000;";
                    cmd.ExecuteNonQuery();
                }
                return conn;
            }
            catch
            {
                conn.Dispose();
                throw;
            }
        }

        private List<string> Instances()
        {
            return _sql.List().Where(i => i.IsConfigured && i.IsEnabled).Select(i => i.InstanceName).ToList();
        }

        // ================================================================ Zamanlama

        /// <summary>Şu an içinde bulunulan bakım penceresinin başlangıcı (yoksa null). Gece yarısını aşan pencere önceki güne aittir.</summary>
        internal static DateTime? CurrentWindow(MaintenanceConfigDto cfg, DateTime now)
        {
            TimeSpan start;
            if (!TryParseTime(cfg.StartTime, out start)) return null;
            foreach (var day in new[] { now.Date, now.Date.AddDays(-1) })
            {
                if (!cfg.Days.Contains((int)day.DayOfWeek)) continue;
                var begin = day + start;
                if (now >= begin && now < begin.AddMinutes(cfg.MaxMinutes)) return begin;
            }
            return null;
        }

        internal static DateTime? NextWindow(MaintenanceConfigDto cfg, DateTime now)
        {
            TimeSpan start;
            if (!TryParseTime(cfg.StartTime, out start) || cfg.Days == null || cfg.Days.Count == 0) return null;
            for (int i = 0; i <= 7; i++)
            {
                var begin = now.Date.AddDays(i) + start;
                if (begin > now && cfg.Days.Contains((int)begin.DayOfWeek)) return begin;
            }
            return null;
        }

        private void SchedulerLoop()
        {
            if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1))) return;
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var cfg = LoadConfig();
                    var window = cfg.Enabled && LicenseGate.Active ? CurrentWindow(cfg, DateTime.Now) : null;
                    if (window != null)
                    {
                        string key = window.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                        if (_settings.Get(SettingKeys.MaintenanceLastWindow) != key
                            && TryStart(cfg, cfg.DryRun, "zamanlanmış" + (cfg.DryRun ? " (deneme modu)" : ""), window.Value.AddMinutes(cfg.MaxMinutes)))
                        {
                            _settings.SetAsync(SettingKeys.MaintenanceLastWindow, key).Wait(TimeSpan.FromSeconds(30));
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Bakım zamanlayıcısı hata verdi.", ex);
                }
                if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1))) return;
            }
        }

        private static bool TryParseTime(string raw, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            DateTime dt;
            if (string.IsNullOrWhiteSpace(raw) || !DateTime.TryParseExact(raw.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return false;
            value = dt.TimeOfDay;
            return true;
        }

        public void Dispose()
        {
            _cts.Cancel();
            if (_scheduler != null) _scheduler.Join(TimeSpan.FromSeconds(5));
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (!_running) return;
                }
                Thread.Sleep(100);
            }
            AppLog.Warn(Module, "Süren bakım 15 sn içinde durmadı, bırakıldı.");
        }
    }
}
