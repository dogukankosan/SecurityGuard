using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
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
    /// SQL Yedekleme (varsayılan kapalı; kullanıcı açarsa seçtiği DB'leri seçtiği gün / saatte yedekler).
    /// Akış (DB başına): disk boş alanı → BACKUP DATABASE (CHECKSUM, destekliyorsa COMPRESSION, gerekirse COPY_ONLY)
    /// → RESTORE VERIFYONLY → WinRAR ile şifreli arşiv (RAR5, AES-256, -hp: dosya adları da şifreli) → arşiv testi
    /// → açık .bak silinir. Sonunda saklama süresi geçen arşivler silinir (her DB'nin son 3 arşivi her durumda kalır;
    /// sadece bizim SG_*.rar dosyalarımıza dokunulur). Başarısız yedek = Kritik olay (mail).
    /// .bak dosyası SQL Server'ın kendi yedek klasörüne yazılır (SQL servis hesabının yazabildiği yer), arşiv hedef klasöre.
    /// </summary>
    internal sealed class SqlBackup : IDisposable
    {
        private const string Module = "SQLYedek";
        private const int KeepRuns = 200;
        private const int KeepAlways = 3;
        private static readonly TimeSpan GateWait = TimeSpan.FromHours(3);
        private static readonly Regex OurArchive = new Regex(@"^SG_(?<db>.+)_(?<stamp>\d{8}_\d{4})\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly GuardDatabase _db;
        private readonly SettingsStore _settings;
        private readonly SqlConnectionStore _sql;
        private readonly EventRecorder _events;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _lock = new object();
        private Thread _scheduler;
        private bool _running;
        private string _progress;

        public SqlBackup(GuardDatabase db, SettingsStore settings, SqlConnectionStore sql, EventRecorder events)
        {
            _db = db;
            _settings = settings;
            _sql = sql;
            _events = events;
        }

        public void Start()
        {
            _scheduler = new Thread(SchedulerLoop) { IsBackground = true, Name = "SqlBackup" };
            _scheduler.Start();
        }

        // ================================================================ Ayarlar / şifre

        public BackupConfigDto LoadConfig()
        {
            string json = _settings.Get(SettingKeys.BackupConfig);
            if (string.IsNullOrEmpty(json)) return new BackupConfigDto();
            try
            {
                return PipeProtocol.FromJson<BackupConfigDto>(json) ?? new BackupConfigDto();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Yedekleme ayarları okunamadı, yedekleme kapalı sayılıyor.", ex);
                return new BackupConfigDto();
            }
        }

        public async Task SaveConfigAsync(BackupConfigDto cfg, string userName)
        {
            if (cfg == null) throw new GuardException(ErrorCodes.InvalidRequest, "Ayar eksik.");
            cfg.Days = (cfg.Days ?? new List<int>()).Where(d => d >= 0 && d <= 6).Distinct().OrderBy(d => d).ToList();
            cfg.Databases = (cfg.Databases ?? new List<string>()).Where(d => !string.IsNullOrWhiteSpace(d) && d.Contains("|")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            TimeSpan start;
            if (!TryParseTime(cfg.StartTime, out start)) throw new GuardException(ErrorCodes.Validation, "Yedek saati SS:dd biçiminde olmalı (ör. 22:00).");
            cfg.StartTime = start.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            if (cfg.RetentionDays < 1 || cfg.RetentionDays > 3650) throw new GuardException(ErrorCodes.Validation, "Saklama süresi 1 - 3650 gün arasında olmalı.");
            if (cfg.CopyOnlyMode != "always" && cfg.CopyOnlyMode != "never") cfg.CopyOnlyMode = "auto";
            cfg.TargetFolder = (cfg.TargetFolder ?? "").Trim();
            cfg.RarPath = string.IsNullOrWhiteSpace(cfg.RarPath) ? null : cfg.RarPath.Trim().Trim('"');

            if (cfg.TargetFolder.Length > 0)
            {
                if (!Path.IsPathRooted(cfg.TargetFolder) || cfg.TargetFolder.StartsWith(@"\\"))
                    throw new GuardException(ErrorCodes.Validation, "Hedef klasör bu sunucuda yerel bir klasör olmalı (ör. D:\\SQLYedek).");
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (cfg.TargetFolder.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                    throw new GuardException(ErrorCodes.Validation, "Hedef klasör Windows klasörünün içinde olamaz.");
            }
            if (cfg.RarPath != null && !File.Exists(cfg.RarPath))
                throw new GuardException(ErrorCodes.Validation, "Rar.exe bulunamadı: " + cfg.RarPath);
            if (cfg.Enabled)
            {
                if (cfg.Days.Count == 0) throw new GuardException(ErrorCodes.Validation, "Yedekleme açıkken en az bir gün seçin.");
                if (cfg.Databases.Count == 0) throw new GuardException(ErrorCodes.Validation, "Yedekleme açıkken en az bir veritabanı seçin.");
                if (cfg.TargetFolder.Length == 0) throw new GuardException(ErrorCodes.Validation, "Yedeklerin yazılacağı klasörü seçin.");
                if (FindRar(cfg) == null) throw new GuardException(ErrorCodes.Validation, "WinRAR (Rar.exe) bulunamadı: yedekleme şifreli arşiv için WinRAR gerektirir. WinRAR'ı kurun veya Rar.exe yolunu girin.");
                if (_settings.GetSecret(SettingKeys.BackupPassword) == null) throw new GuardException(ErrorCodes.Validation, "Önce arşiv şifresini belirleyin.");
            }

            var old = LoadConfig();
            await _settings.SetAsync(SettingKeys.BackupConfig, PipeProtocol.ToJson(cfg)).ConfigureAwait(false);
            if (Describe(cfg) != Describe(old))
            {
                AuditTrail.Record(_db, userName, "SQL yedekleme ayarları değişti", Describe(old) + " → " + Describe(cfg));
                AppLog.Info(Module, "Ayarlar değişti (" + userName + "): " + Describe(cfg));
            }
        }

        private static readonly string[] DayNames = { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" };

        private static string Describe(BackupConfigDto c)
        {
            return (c.Enabled ? "açık" : "kapalı") + ", " + string.Join(" ", c.Days.Select(d => DayNames[d])) + " " + c.StartTime + ", klasör " + (c.TargetFolder ?? "-")
                   + ", " + c.RetentionDays + " gün, COPY_ONLY " + c.CopyOnlyMode + ", DB: " + string.Join(", ", c.Databases.Select(d => d.Replace("|", "/")));
        }

        /// <summary>Arşiv şifresini belirler (boşsa güçlü şifre üretir). Yeni şifre döner.</summary>
        public async Task<string> SetPasswordAsync(string password, string userName)
        {
            password = (password ?? "").Trim();
            if (password.Length == 0) password = Generate();
            if (password.Length < 12) throw new GuardException(ErrorCodes.Validation, "Arşiv şifresi en az 12 karakter olmalı.");
            if (password.Any(ch => ch == '"' || char.IsControl(ch))) throw new GuardException(ErrorCodes.Validation, "Arşiv şifresinde çift tırnak (\") kullanılamaz.");
            bool had = _settings.GetSecret(SettingKeys.BackupPassword) != null;
            await _settings.SetSecretAsync(SettingKeys.BackupPassword, password).ConfigureAwait(false);
            AuditTrail.Record(_db, userName, had ? "Yedek arşiv şifresi değiştirildi" : "Yedek arşiv şifresi belirlendi",
                "Eski arşivler eski şifreyle açılır; yeni şifre bundan sonraki yedeklerde geçerli.");
            AppLog.Warn(Module, "Arşiv şifresi " + (had ? "değiştirildi" : "belirlendi") + " (" + userName + ").");
            return password;
        }

        public string GetPassword(string userName)
        {
            string password = _settings.GetSecret(SettingKeys.BackupPassword);
            if (password == null) throw new GuardException(ErrorCodes.Validation, "Arşiv şifresi henüz belirlenmedi.");
            AuditTrail.Record(_db, userName, "Yedek arşiv şifresi görüntülendi");
            return password;
        }

        private static string Generate()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_.!@#%+=";
            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
        }

        // ================================================================ WinRAR

        /// <summary>Rar.exe: ayardaki yol, WinRAR kayıt defteri, Program Files.</summary>
        public static string FindRar(BackupConfigDto cfg)
        {
            if (cfg != null && !string.IsNullOrEmpty(cfg.RarPath) && File.Exists(cfg.RarPath)) return cfg.RarPath;
            var candidates = new List<string>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = hklm.OpenSubKey(@"SOFTWARE\WinRAR"))
                    {
                        foreach (var name in new[] { "exe64", "exe32" })
                        {
                            string exe = key == null ? null : key.GetValue(name) as string;
                            if (!string.IsNullOrEmpty(exe)) candidates.Add(Path.Combine(Path.GetDirectoryName(exe), "Rar.exe"));
                        }
                    }
                }
                catch { }
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"WinRAR\Rar.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"WinRAR\Rar.exe"));
            return candidates.FirstOrDefault(File.Exists);
        }

        // ================================================================ UI durumu

        public BackupStateDto GetState()
        {
            var cfg = LoadConfig();
            string rar = FindRar(cfg);
            var state = new BackupStateDto
            {
                Config = cfg,
                Databases = new List<BackupDbDto>(),
                Notes = new List<string>(),
                RarFound = rar,
                RarNote = rar == null ? "WinRAR bulunamadı: yedekleme şifreli arşiv için WinRAR gerektirir (lisanslı WinRAR kurun veya Rar.exe yolunu girin)."
                        : File.Exists(Path.Combine(Path.GetDirectoryName(rar), "rarreg.key")) ? null
                        : "WinRAR lisans dosyası (rarreg.key) bulunamadı: deneme sürümü olabilir. Ticari kullanım için lisans gerekir.",
                HasPassword = _settings.GetSecret(SettingKeys.BackupPassword) != null,
                Runs = LoadRuns()
            };
            lock (_lock)
            {
                state.IsRunning = _running;
                state.Progress = _running ? _progress : null;
            }
            var next = NextSlot(cfg, DateTime.Now);
            state.NextRunUtc = cfg.Enabled && next != null ? DbTime.ToDb(next.Value.ToUniversalTime()) : null;
            state.TargetInfo = TargetInfo(cfg);

            var guardLast = _db.Read(c =>
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = c.Create(@"SELECT r.Instance || '|' || f.DatabaseName, MAX(f.TimeUtc) FROM BackupFiles f JOIN BackupRuns r ON r.RunId = f.RunId
                                            WHERE f.Step = 'ARŞİV' AND f.Result = 'Tamam' GROUP BY r.Instance, f.DatabaseName"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) map[r.GetString(0)] = r.GetString(1);
                }
                return map;
            });

            var selected = new HashSet<string>(cfg.Databases, StringComparer.OrdinalIgnoreCase);
            foreach (var instance in Instances())
            {
                string cs = _sql.GetConnectionString(instance);
                if (cs == null) continue;
                try
                {
                    using (var conn = new SqlConnection(cs))
                    {
                        conn.Open();
                        var info = Rows(conn, "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int), CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(512))", CancellationToken.None)[0];
                        string staging = Str(info[1]);
                        bool compression = Long(info[0]) != 4;
                        string free = "";
                        try
                        {
                            if (!string.IsNullOrEmpty(staging)) free = ", boş " + Fmt.Bytes(new DriveInfo(Path.GetPathRoot(staging)).AvailableFreeSpace);
                        }
                        catch { }
                        state.Notes.Add(instance + ": geçici .bak klasörü " + (staging ?? "?") + free + "; " + (compression ? "SQL sıkıştırması kullanılır" : "Express: SQL sıkıştırması yok (WinRAR sıkıştırır)") + ".");
                        var others = OtherBackups(conn);
                        if (others.Count > 0)
                            state.Notes.Add(instance + ": DİKKAT, son 30 günde başka bir araç da tam yedek almış (" + string.Join(", ", others.Take(3)) + "). "
                                            + (cfg.CopyOnlyMode == "never" ? "COPY_ONLY kapalı: diğer aracın yedek zincirini (fark / log yedekleri) bozabilirsiniz." : "Yedekler COPY_ONLY alınır, diğer aracın yedek zinciri bozulmaz."));

                        foreach (var r in Rows(conn, @"
SELECT d.name, d.recovery_model_desc,
       (SELECT SUM(CAST(size AS bigint)) FROM sys.master_files mf WHERE mf.database_id = d.database_id AND mf.type = 0) / 128.0,
       (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset b WHERE b.database_name = d.name AND b.type = 'D')
FROM sys.databases d WHERE d.name <> 'tempdb' AND d.state = 0 AND d.source_database_id IS NULL ORDER BY CASE WHEN d.database_id <= 4 THEN 1 ELSE 0 END, d.name", CancellationToken.None))
                        {
                            string key = instance + "|" + Str(r[0]);
                            string guard;
                            guardLast.TryGetValue(key, out guard);
                            var last = Date(r[3]);
                            state.Databases.Add(new BackupDbDto
                            {
                                Instance = instance,
                                Name = Str(r[0]),
                                RecoveryModel = Str(r[1]),
                                DataMb = Dbl(r[2]),
                                Selected = selected.Contains(key),
                                LastFullBackupUtc = last == null ? null : DbTime.ToDb(last.Value),
                                LastGuardBackupUtc = guard
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    state.Notes.Add(instance + ": bağlanılamadı (" + ex.Message + ")");
                }
            }
            foreach (var key in cfg.Databases.Where(k => !state.Databases.Any(d => string.Equals(d.Instance + "|" + d.Name, k, StringComparison.OrdinalIgnoreCase))))
            {
                var parts = key.Split('|');
                state.Databases.Add(new BackupDbDto { Instance = parts[0], Name = parts[1], RecoveryModel = "bulunamadı", Selected = true });
            }
            return state;
        }

        private static string TargetInfo(BackupConfigDto cfg)
        {
            if (string.IsNullOrEmpty(cfg.TargetFolder)) return "Hedef klasör seçilmedi.";
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(cfg.TargetFolder));
                if (!drive.IsReady) return cfg.TargetFolder + ": UYARI: sürücüye ulaşılamıyor (takılı değil veya sürücü harfi değişmiş). Bu haliyle yedek alınamaz.";
                long total = 0;
                int count = 0;
                if (Directory.Exists(cfg.TargetFolder))
                    foreach (var f in Directory.EnumerateFiles(cfg.TargetFolder, "SG_*.rar", SearchOption.AllDirectories))
                    {
                        if (!OurArchive.IsMatch(Path.GetFileName(f))) continue;
                        total += new FileInfo(f).Length;
                        count++;
                    }
                string system = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                return cfg.TargetFolder + ": " + count + " arşiv, " + Fmt.Bytes(total) + "; diskte boş " + Fmt.Bytes(drive.AvailableFreeSpace) + " / " + Fmt.Bytes(drive.TotalSize)
                       + (string.Equals(drive.Name, system, StringComparison.OrdinalIgnoreCase) ? ". UYARI: Windows'un diski; disk bozulursa yedek de gider." : ".");
            }
            catch (Exception ex)
            {
                return cfg.TargetFolder + ": okunamadı (" + ex.Message + ")";
            }
        }

        /// <summary>Son 30 günde SecurityGuard dışında (COPY_ONLY olmayan) tam yedek alan araçlar.</summary>
        private static List<string> OtherBackups(SqlConnection conn)
        {
            return Rows(conn, @"
SELECT DISTINCT ISNULL(NULLIF(b.name, ''), ISNULL(b.user_name, '?')) + ' (' + b.database_name + ')'
FROM msdb.dbo.backupset b
WHERE b.type = 'D' AND b.is_copy_only = 0 AND ISNULL(b.name, '') <> N'SecurityGuard'
  AND b.backup_finish_date > DATEADD(DAY, -30, GETDATE())", CancellationToken.None).Select(r => Str(r[0])).ToList();
        }

        public List<BackupFileDto> LoadFiles(string runId)
        {
            return _db.Read(c =>
            {
                var list = new List<BackupFileDto>();
                using (var cmd = c.Create("SELECT TimeUtc, DatabaseName, Step, ArchivePath, SizeMb, DurationMs, Result, Note FROM BackupFiles WHERE RunId = @r ORDER BY Id", ("@r", runId)))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new BackupFileDto
                        {
                            TimeUtc = r.GetString(0),
                            Database = r.IsDBNull(1) ? null : r.GetString(1),
                            Step = r.GetString(2),
                            ArchivePath = r.IsDBNull(3) ? null : r.GetString(3),
                            SizeMb = r.IsDBNull(4) ? 0 : r.GetDouble(4),
                            DurationMs = r.IsDBNull(5) ? 0 : r.GetInt64(5),
                            Result = r.GetString(6),
                            Note = r.IsDBNull(7) ? null : r.GetString(7)
                        });
                }
                return list;
            });
        }

        private List<BackupRunDto> LoadRuns()
        {
            return _db.Read(c =>
            {
                var list = new List<BackupRunDto>();
                using (var cmd = c.Create("SELECT RunId, Instance, TriggerText, StartedUtc, FinishedUtc, Status, Summary FROM BackupRuns ORDER BY StartedUtc DESC LIMIT 50"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new BackupRunDto
                        {
                            RunId = r.GetString(0),
                            Instance = r.GetString(1),
                            Trigger = r.IsDBNull(2) ? null : r.GetString(2),
                            StartedUtc = r.GetString(3),
                            FinishedUtc = r.IsDBNull(4) ? null : r.GetString(4),
                            Status = r.GetString(5),
                            Summary = r.IsDBNull(6) ? null : r.GetString(6)
                        });
                }
                return list;
            });
        }

        private List<string> Instances()
        {
            return _sql.List().Where(i => i.IsConfigured && i.IsEnabled).Select(i => i.InstanceName).ToList();
        }

        // ================================================================ Çalıştırma

        public bool RunNow(string userName)
        {
            var cfg = LoadConfig();
            CheckReady(cfg);
            bool started = TryStart(cfg, "elle (" + userName + ")");
            if (started) AuditTrail.Record(_db, userName, "SQL yedeği elle başlatıldı", string.Join(", ", cfg.Databases.Select(d => d.Replace("|", "/"))));
            return started;
        }

        private void CheckReady(BackupConfigDto cfg)
        {
            if (cfg.Databases.Count == 0) throw new GuardException(ErrorCodes.Validation, "Önce yedeklenecek veritabanlarını seçip kaydedin.");
            if (string.IsNullOrEmpty(cfg.TargetFolder)) throw new GuardException(ErrorCodes.Validation, "Önce yedek klasörünü seçip kaydedin.");
            if (FindRar(cfg) == null) throw new GuardException(ErrorCodes.Validation, "WinRAR (Rar.exe) bulunamadı.");
            if (_settings.GetSecret(SettingKeys.BackupPassword) == null) throw new GuardException(ErrorCodes.Validation, "Önce arşiv şifresini belirleyin.");
        }

        private bool TryStart(BackupConfigDto cfg, string trigger)
        {
            lock (_lock)
            {
                if (_running || _cts.IsCancellationRequested) return false;
                _running = true;
                _progress = "Başlıyor";
            }
            new Thread(() => Execute(cfg, trigger)) { IsBackground = true, Name = "SqlBackupRun", Priority = ThreadPriority.BelowNormal }.Start();
            return true;
        }

        private void SetProgress(string text)
        {
            lock (_lock) _progress = text;
        }

        private void Execute(BackupConfigDto cfg, string trigger)
        {
            bool gate = false;
            try
            {
                gate = SqlJobGate.Enter("SQL yedekleme", GateWait, _cts.Token, holder =>
                {
                    SetProgress(holder + " sürüyor, bitmesi bekleniyor");
                    AppLog.Info(Module, holder + " sürüyor; yedekleme bitmesini bekliyor.");
                });
                if (!gate)
                {
                    if (!_cts.IsCancellationRequested)
                        Fail(null, "Yedek alınamadı: " + (SqlJobGate.Holder ?? "başka bir SQL işi") + " 3 saat içinde bitmedi.", trigger);
                    return;
                }
                string password = _settings.GetSecret(SettingKeys.BackupPassword);
                string rar = FindRar(cfg);
                foreach (var group in cfg.Databases.Select(d => d.Split('|')).GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase))
                {
                    if (_cts.IsCancellationRequested) return;
                    new Run(this, cfg, group.Key, group.Select(p => p[1]).ToList(), trigger, rar, password).Execute();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Yedekleme çalıştırılamadı.", ex);
                Fail(null, "Yedekleme çalıştırılamadı: " + ex.Message, trigger);
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

        private void Fail(string instance, string summary, string trigger)
        {
            _events.Record(new GuardEvent("Guard.BackupFailed", summary) { Source = "SQL Yedekleme" + (instance != null ? " (" + instance + ")" : "") }
                .With("Tetikleyen", trigger)
                .With("Ne yapmalı", "SecurityGuard → SQL Server → Yedekleme sayfasında hata ayrıntısına bakın. Bir sonraki yedek gelene kadar elde güncel yedek yok."));
        }

        /// <summary>Bir instance'ın seçili DB'lerinin yedeklenmesi.</summary>
        private sealed class Run
        {
            private readonly SqlBackup _owner;
            private readonly BackupConfigDto _cfg;
            private readonly string _instance;
            private readonly List<string> _dbs;
            private readonly string _trigger;
            private readonly string _rar;
            private readonly string _password;
            private readonly string _runId = Guid.NewGuid().ToString("N");
            private readonly CancellationToken _token;
            private readonly List<string> _failed = new List<string>();
            private int _ok;
            private double _totalMb;

            public Run(SqlBackup owner, BackupConfigDto cfg, string instance, List<string> dbs, string trigger, string rar, string password)
            {
                _owner = owner;
                _cfg = cfg;
                _instance = instance;
                _dbs = dbs;
                _trigger = trigger;
                _rar = rar;
                _password = password;
                _token = owner._cts.Token;
            }

            public void Execute()
            {
                var started = DateTime.UtcNow;
                _owner._db.Writer.EnqueueAsync(c => c.Execute(
                    "INSERT INTO BackupRuns (RunId, Instance, TriggerText, StartedUtc, Status) VALUES (@r, @i, @t, @s, 'Sürüyor')",
                    ("@r", _runId), ("@i", _instance), ("@t", _trigger), ("@s", DbTime.ToDb(started)))).Wait();
                AppLog.Info(Module, _instance + ": yedekleme başladı (" + _trigger + "): " + string.Join(", ", _dbs));

                string status;
                string fatal = null;
                try
                {
                    string cs = _owner._sql.GetConnectionString(_instance);
                    if (cs == null) throw new InvalidOperationException("SQL bağlantısı tanımlı değil veya izleme kapalı.");
                    using (var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { Pooling = false, ApplicationName = "SecurityGuard Yedek" }.ConnectionString))
                    {
                        conn.Open();
                        var info = Rows(conn, "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int), CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(512))", _token)[0];
                        bool compression = Long(info[0]) != 4;
                        string staging = Str(info[1]);
                        if (string.IsNullOrEmpty(staging) || !Directory.Exists(staging))
                            throw new InvalidOperationException("SQL Server'ın yedek klasörü bulunamadı (" + (staging ?? "boş") + ").");
                        bool copyOnly = _cfg.CopyOnlyMode == "always" || (_cfg.CopyOnlyMode == "auto" && OtherBackups(conn).Count > 0);
                        string folder = Path.Combine(_cfg.TargetFolder, Safe(_instance));
                        try
                        {
                            Directory.CreateDirectory(folder);
                        }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
                        {
                            // Harici disk takılı değil / sürücü harfi değişmiş / klasöre yazma izni yok: kullanıcıya anlaşılır neden
                            throw new InvalidOperationException("Yedek klasörüne ulaşılamıyor (" + folder + "): sürücü takılı değil, sürücü harfi değişmiş veya klasöre yazma izni yok. ["
                                                                + ex.Message + "]");
                        }

                        foreach (var db in _dbs)
                        {
                            if (_token.IsCancellationRequested) break;
                            Database(conn, db, staging, folder, compression, copyOnly);
                        }
                        if (!_token.IsCancellationRequested) Cleanup(folder);
                    }
                    status = _token.IsCancellationRequested ? "Yarıda kesildi" : _failed.Count > 0 ? "Hatalı" : "Tamamlandı";
                }
                catch (Exception ex) when (_token.IsCancellationRequested)
                {
                    AppLog.Info(Module, _instance + ": yedekleme servis kapandığı için yarıda bırakıldı (" + ex.Message + ").");
                    status = "Yarıda kesildi";
                }
                catch (InvalidOperationException ex)
                {
                    // Beklenen çalışma hatası (hedef klasör yok, SQL bağlantısı tanımsız...): neden yeterli, yığın izi gerekmez
                    AppLog.Error(Module, _instance + ": yedekleme başarısız: " + ex.Message);
                    fatal = ex.Message;
                    status = "Hatalı";
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, _instance + ": yedekleme başarısız.", ex);
                    fatal = ex.Message;
                    status = "Hatalı";
                }

                string summary = _ok + " DB yedeklendi (" + Fmt.Mb(_totalMb) + ")" + (_failed.Count > 0 ? ", başarısız: " + string.Join(", ", _failed) : "") + (fatal != null ? ": " + fatal : "");
                try
                {
                    _owner._db.Writer.EnqueueAsync(c =>
                    {
                        c.Execute("UPDATE BackupRuns SET FinishedUtc = @f, Status = @s, Summary = @m WHERE RunId = @r", ("@f", DbTime.Now()), ("@s", status), ("@m", summary), ("@r", _runId));
                        c.Execute("DELETE FROM BackupFiles WHERE RunId IN (SELECT RunId FROM BackupRuns ORDER BY StartedUtc DESC LIMIT -1 OFFSET @k)", ("@k", KeepRuns));
                        c.Execute("DELETE FROM BackupRuns WHERE RunId IN (SELECT RunId FROM BackupRuns ORDER BY StartedUtc DESC LIMIT -1 OFFSET @k)", ("@k", KeepRuns));
                    }).Wait(TimeSpan.FromSeconds(30));
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Yedek sonucu kaydedilemedi.", ex);
                }
                AppLog.Info(Module, _instance + ": yedekleme bitti (" + status + "): " + summary);
                AuditTrail.Record(_owner._db, "SecurityGuard", "SQL yedeği", _instance + ": " + status + ", " + summary);

                if (status == "Tamamlandı")
                    _owner._events.Record(new GuardEvent("Sql.BackupDone", _instance + ": " + summary + ".") { Source = "SQL Yedekleme (" + _instance + ")" }
                        .With("Süre", (int)(DateTime.UtcNow - started).TotalMinutes + " dk").With("Klasör", Path.Combine(_cfg.TargetFolder, Safe(_instance))));
                else if (status != "Yarıda kesildi")
                    _owner.Fail(_instance, _instance + " SQL yedeği " + status.ToLowerInvariant() + ": " + summary + ".", _trigger);
                else
                    _owner.Fail(_instance, _instance + " SQL yedeği servis kapandığı için yarıda kaldı: " + summary + ".", _trigger);
            }

            private void Database(SqlConnection conn, string db, string staging, string folder, bool compression, bool copyOnly)
            {
                var exists = Rows(conn, "SELECT state FROM sys.databases WHERE name = " + N(db), _token);
                if (exists.Count == 0 || Long(exists[0][0]) != 0)
                {
                    _failed.Add(db);
                    Step(db, "YEDEK", null, 0, 0, "Hata", exists.Count == 0 ? "Veritabanı bulunamadı (silinmiş / adı değişmiş olabilir)." : "Veritabanı çevrimiçi değil.");
                    return;
                }

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
                string bak = Path.Combine(staging, "SG_" + Safe(db) + "_" + stamp + ".bak");
                string archive = Path.Combine(folder, "SG_" + Safe(db) + "_" + stamp + ".rar");
                try
                {
                    // Boş alan: veri dosyalarının ayrılmış boyutu üst sınır (sıkıştırmayla genelde yarısından az)
                    double dataMb = Dbl(Scalar(conn, "SELECT SUM(CAST(size AS bigint)) / 128.0 FROM sys.master_files WHERE database_id = DB_ID(" + N(db) + ") AND type = 0", _token));
                    double need = dataMb * (compression ? 0.6 : 1.0) + 100;
                    CheckSpace(staging, need, "geçici yedek klasöründe");
                    CheckSpace(folder, need, "hedef klasörde");

                    // 1) BACKUP
                    _owner.SetProgress(_instance + " / " + db + ": yedek alınıyor");
                    var sw = Stopwatch.StartNew();
                    Exec(conn, "BACKUP DATABASE " + Q(db) + " TO DISK = " + N(bak) + " WITH CHECKSUM, INIT, FORMAT, NAME = N'SecurityGuard', DESCRIPTION = " + N(archive)
                               + (compression ? ", COMPRESSION" : "") + (copyOnly ? ", COPY_ONLY" : ""));
                    double bakMb = new FileInfo(bak).Length / 1024.0 / 1024.0;
                    Step(db, "YEDEK", bak, bakMb, sw.ElapsedMilliseconds, "Tamam", (compression ? "sıkıştırmalı" : "sıkıştırmasız") + (copyOnly ? ", COPY_ONLY" : ""));

                    // 2) Doğrulama
                    _owner.SetProgress(_instance + " / " + db + ": yedek doğrulanıyor");
                    sw.Restart();
                    Exec(conn, "RESTORE VERIFYONLY FROM DISK = " + N(bak) + " WITH CHECKSUM");
                    Step(db, "DOĞRULAMA", null, 0, sw.ElapsedMilliseconds, "Tamam", "RESTORE VERIFYONLY: yedek okunabilir ve sağlam.");

                    // 3) Şifreli arşiv + test
                    _owner.SetProgress(_instance + " / " + db + ": şifreli arşivleniyor");
                    sw.Restart();
                    // Şifre komut satırında DEĞİL: "-hp" (değersiz) ile Rar.exe şifreyi stdin'den okur (RunRar yazar).
                    RunRar("a -ep -ma5 -m1 -y -idq -hp \"" + archive + "\" \"" + bak + "\"", TimeSpan.FromHours(6));
                    RunRar("t -y -idq -hp \"" + archive + "\"", TimeSpan.FromHours(3));
                    double archiveMb = new FileInfo(archive).Length / 1024.0 / 1024.0;
                    _totalMb += archiveMb;
                    _ok++;
                    Step(db, "ARŞİV", archive, archiveMb, sw.ElapsedMilliseconds, "Tamam", "RAR5, AES-256, dosya adları şifreli; arşiv testi başarılı.");
                }
                catch (Exception ex)
                {
                    if (_token.IsCancellationRequested) throw new OperationCanceledException(_token);
                    _failed.Add(db);
                    AppLog.Error(Module, _instance + " / " + db + ": yedek başarısız.", ex);
                    Step(db, "YEDEK", null, 0, 0, "Hata", ex.Message);
                    TryDelete(archive); // yarım arşiv kalmasın (temizlikte "başarılı yedek" sanılmasın)
                }
                finally
                {
                    TryDelete(bak); // şifresiz .bak hiçbir durumda bırakılmaz
                }
            }

            private void CheckSpace(string folder, double needMb, string where)
            {
                var drive = new DriveInfo(Path.GetPathRoot(folder));
                double freeMb = drive.AvailableFreeSpace / 1024.0 / 1024.0;
                if (freeMb < needMb)
                    throw new InvalidOperationException("Diskte yer yok (" + where + " " + drive.Name + "): gereken yaklaşık " + Fmt.Mb(needMb) + ", boş " + Fmt.Mb(freeMb) + ".");
            }

            private void Exec(SqlConnection conn, string sql)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = 0; // büyük DB'lerin yedeği saatler sürebilir; servis kapanınca iptal edilir
                    using (_token.Register(() => { try { cmd.Cancel(); } catch { } }))
                        cmd.ExecuteNonQuery();
                }
                _token.ThrowIfCancellationRequested();
            }

            /// <summary>
            /// Rar.exe çalıştırır. Şifre komut satırında DEĞİL stdin'den verilir: "-hp" (değersiz) ile Rar.exe
            /// şifreyi sorar, biz stdin'den yazarız. Böylece şifre 4688 / Sysmon / EDR komut satırı kayıtlarında görünmez
            /// ve şifre '\' ile bitse bile argüman ayrımı bozulmaz. Rar.exe, redirect edilmiş stdin'den şifreyi
            /// EOF'a kadar TEK PARÇA okur; bu yüzden şifre TEK KEZ, "\n" ile ve BOM'suz yazılır (bkz. aşağıdaki not).
            /// </summary>
            private void RunRar(string args, TimeSpan timeout)
            {
                var psi = new ProcessStartInfo(_rar, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    try
                    {
                        // Rar.exe, redirect edilmiş stdin'den şifreyi EOF'a kadar TEK PARÇA okur (satır satır değil)
                        // ve yalnızca sondaki tek newline'ı atar. Bu yüzden şifre TEK KEZ, "\n" ile ve BOM'SUZ
                        // yazılmalı. Aksi halde şifre sessizce bozulur (servisin kendi testi aynı yolu kullandığı
                        // için geçer ama WinRAR'da elle açılmaz):
                        //   - İKİ kez yazılırsa RAR "parola\nparola" okur → gömülü "\n".  (asıl bulunan bug)
                        //   - WriteLine "\r\n" yazarsa sondaki "\r" şifreye girer.
                        //   - UTF-8 BOM yazılırsa şifrenin başına görünmez 3 bayt eklenir.
                        byte[] pw = new UTF8Encoding(false).GetBytes(_password + "\n");
                        p.StandardInput.BaseStream.Write(pw, 0, pw.Length);
                        p.StandardInput.BaseStream.Flush();
                    }
                    catch (IOException)
                    {
                        // Rar şifreyi okuyup stdin'i kapatmış olabilir; sorun değil
                    }
                    finally
                    {
                        try { p.StandardInput.Close(); } catch { }
                    }

                    var output = p.StandardOutput.ReadToEndAsync();
                    var error = p.StandardError.ReadToEndAsync();
                    var deadline = DateTime.UtcNow + timeout;
                    while (!p.WaitForExit(1000))
                    {
                        if (_token.IsCancellationRequested || DateTime.UtcNow > deadline)
                        {
                            try { p.Kill(); } catch { }
                            if (_token.IsCancellationRequested) throw new OperationCanceledException(_token);
                            throw new TimeoutException("WinRAR " + (int)timeout.TotalHours + " saatte bitmedi.");
                        }
                    }
                    if (p.ExitCode != 0)
                    {
                        string msg = (error.Result + " " + output.Result).Replace(_password, "***").Trim();
                        throw new InvalidOperationException("WinRAR hata verdi (kod " + p.ExitCode + ": " + RarExit(p.ExitCode) + ")" + (msg.Length > 0 ? ": " + msg : ""));
                    }
                }
            }

            private static string RarExit(int code)
            {
                switch (code)
                {
                    case 1: return "uyarı";
                    case 2: return "ölümcül hata";
                    case 3: return "arşiv bozuk (CRC)";
                    case 5: return "yazma hatası";
                    case 6: return "dosya açılamadı";
                    case 8: return "bellek yetersiz";
                    case 9: return "dosya oluşturulamadı";
                    case 11: return "yanlış şifre";
                    default: return "bilinmeyen";
                }
            }

            /// <summary>Saklama süresi geçen arşivler (sadece SG_*.rar). Her DB'nin en yeni 3 arşivi her durumda kalır.</summary>
            private void Cleanup(string folder)
            {
                var limit = DateTime.Now.AddDays(-_cfg.RetentionDays);
                var files = Directory.GetFiles(folder, "SG_*.rar")
                    .Select(f => new { Path = f, M = OurArchive.Match(System.IO.Path.GetFileName(f)) })
                    .Where(x => x.M.Success)
                    .Select(x =>
                    {
                        DateTime t;
                        DateTime.TryParseExact(x.M.Groups["stamp"].Value, "yyyyMMdd_HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
                        return new { x.Path, Db = x.M.Groups["db"].Value, Time = t };
                    })
                    .Where(x => x.Time != DateTime.MinValue);
                foreach (var group in files.GroupBy(f => f.Db, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var old in group.OrderByDescending(f => f.Time).Skip(KeepAlways).Where(f => f.Time < limit))
                    {
                        try
                        {
                            double mb = new FileInfo(old.Path).Length / 1024.0 / 1024.0;
                            File.Delete(old.Path);
                            Step(group.Key, "TEMİZLİK", old.Path, mb, 0, "Silindi", _cfg.RetentionDays + " günden eski arşiv.");
                        }
                        catch (Exception ex)
                        {
                            Step(group.Key, "TEMİZLİK", old.Path, 0, 0, "Hata", "Eski arşiv silinemedi: " + ex.Message);
                        }
                    }
                }
            }

            private static void TryDelete(string file)
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (Exception ex)
                {
                    AppLog.Warn(Module, "Dosya silinemedi: " + file + " (" + ex.Message + ")");
                }
            }

            private void Step(string db, string step, string path, double mb, long ms, string result, string note)
            {
                _owner._db.Writer.Enqueue(c => c.Execute(
                    @"INSERT INTO BackupFiles (RunId, TimeUtc, DatabaseName, Step, ArchivePath, SizeMb, DurationMs, Result, Note)
                      VALUES (@r, @t, @d, @s, @p, @mb, @ms, @res, @n)",
                    ("@r", _runId), ("@t", DbTime.Now()), ("@d", db), ("@s", step), ("@p", path), ("@mb", mb), ("@ms", ms), ("@res", result), ("@n", note)));
            }
        }

        /// <summary>Dosya / klasör adında kullanılamayan karakterler "_" olur.</summary>
        internal static string Safe(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? "").Select(c => invalid.Contains(c) || c == '$' ? '_' : c).ToArray());
        }

        // ================================================================ Zamanlama

        internal static DateTime? NextSlot(BackupConfigDto cfg, DateTime now)
        {
            TimeSpan start;
            if (!TryParseTime(cfg.StartTime, out start) || cfg.Days == null || cfg.Days.Count == 0) return null;
            for (int i = 0; i <= 7; i++)
            {
                var slot = now.Date.AddDays(i) + start;
                if (slot > now && cfg.Days.Contains((int)slot.DayOfWeek)) return slot;
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
                    TimeSpan start;
                    var now = DateTime.Now;
                    if (cfg.Enabled && LicenseGate.Active && TryParseTime(cfg.StartTime, out start) && cfg.Days.Contains((int)now.DayOfWeek))
                    {
                        var slot = now.Date + start;
                        string key = slot.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                        // Saat geldiyse (servis o saatte kapalıysa aynı gün açılınca) bir kez
                        if (now >= slot && _settings.Get(SettingKeys.BackupLastSlot) != key)
                        {
                            try
                            {
                                CheckReady(cfg);
                                if (TryStart(cfg, "zamanlanmış" + (now - slot > TimeSpan.FromMinutes(30) ? " (kaçırılan " + cfg.StartTime + " yedeği)" : "")))
                                    _settings.SetAsync(SettingKeys.BackupLastSlot, key).Wait(TimeSpan.FromSeconds(30));
                            }
                            catch (GuardException ex)
                            {
                                _settings.SetAsync(SettingKeys.BackupLastSlot, key).Wait(TimeSpan.FromSeconds(30));
                                Fail(null, "Zamanlanmış SQL yedeği başlatılamadı: " + ex.Message, "zamanlanmış");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Yedekleme zamanlayıcısı hata verdi.", ex);
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
            AppLog.Warn(Module, "Süren yedekleme 15 sn içinde durmadı, bırakıldı.");
        }
    }
}
