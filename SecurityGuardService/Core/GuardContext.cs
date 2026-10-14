using System;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Checks;
using SecurityGuardService.Checks.Performance;
using SecurityGuardService.Checks.Security;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Security;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Ban;
using SecurityGuardService.Engine.Canary;
using SecurityGuardService.Engine.Collectors;
using SecurityGuardService.Engine.Events;
using SecurityGuardService.Engine.Firewall;
using SecurityGuardService.Engine.Health;
using SecurityGuardService.Engine.Reports;
using SecurityGuardService.Engine.Rules;
using SecurityGuardService.Engine.Update;
using SecurityGuardService.Licensing;
using SecurityGuardService.Mail;
using SecurityGuardService.Sql;

namespace SecurityGuardService.Core
{
    /// <summary>
    /// Modüllerin ortak kullandığı çekirdek servisler (DB, ayarlar, log zinciri).
    /// Servis açılışında bir kez oluşturulur ve modüllere verilir.
    /// </summary>
    internal sealed class GuardContext : IDisposable
    {
        private const string Module = "Çekirdek";

        private GuardContext(GuardDatabase database, SettingsStore settings, HmacChain chain, bool isWindowsService)
        {
            Database = database;
            Settings = settings;
            Chain = chain;
            Admin = new AdminAccount(database, settings);
            Whitelist = new Whitelist(database);
            Rules = new RuleStore(database);
            Mail = new MailService(database, settings);
            Mail.RecipientsDeleted += () => Rules.Reload();
            Sql = new SqlConnectionStore(database);
            Sqlite = new SqliteConsole(database, chain);
            Events = new EventRecorder(database, Rules, Mail, chain,
                new MailBatcher(Mail, () => GeneralSettings.GetInt(settings, SettingKeys.MailBatchMinutes)));
            License = new LicenseManager(database, settings, Events);
            Ban = new BanManager(database, Whitelist, Rules, Events, new WindowsBanFirewall());
            SqlAudit = new SqlAuditManager(database, settings, Sql, Events);
            SqlMonitor = new SqlMonitor(settings, SqlAudit, new SqlBaseline(database, Sql, SqlAudit, Events));
            SqlConfigTrail.Changed = SqlMonitor.CheckSoon; // ayar değişikliği kaydı gelince karşılaştırma beklemeden çalışsın
            Maintenance = new SqlMaintenance(database, settings, Sql, Events);
            Backup = new SqlBackup(database, settings, Sql, Events);
            Checks = new CheckRunner(database, settings, Events);
            Checks.Register(CheckScopes.Performance, PerformanceScan.DisplayName, ctx => PerformanceScan.Run(ctx, Sql));
            Checks.Register(CheckScopes.Security, SecurityScan.DisplayName, ctx => SecurityScan.Run(ctx, Sql, SqlAudit));
            Geo = new GeoIpDatabase();
            PortRestrictions = new PortRestrictions(database, Events, new WindowsPortFirewall(), Geo);
            RdpSessions = new RdpSessionTracker(database, settings, Geo);
            Services = new ServiceWatch(database, Events);
            Protection = new SelfProtection(database, settings, Events, () => Backup.LoadConfig().TargetFolder, isWindowsService);
            Canary = new CanaryFiles(database, Events, () => Backup.LoadConfig().TargetFolder);
            var processor = new WindowsEventProcessor(Events, Ban, () => { SqlAudit.AuditEventSeen(); SqlMonitor.CheckSoon(); });
            processor.PortRuleTouched = PortRestrictions.RequestSync;
            processor.Sessions = RdpSessions;
            Collectors = new CollectorHost(database, processor);
            IisLogs = new IisLogCollector(settings, processor.ExternalFailure);

            Admin.LockedOut += (user, failed, minutes) => Events.Record(
                new GuardEvent("Ui.AdminLockedOut", "SecurityGuard arayüzüne art arda " + failed + " hatalı giriş yapıldı, hesap " + minutes + " dakika kilitlendi.")
                {
                    Source = "SecurityGuard arayüzü",
                    UserName = user
                }
                .With("Denenen kullanıcı adı", user)
                .With("Kilit süresi", minutes + " dakika")
                .With("Öneri", "Bu denemeleri siz yapmadıysanız sunucuya RDP ile kimin bağlı olduğunu kontrol edin."));

            Reports = new DailyReport(this);
            MonthlyReports = new MonthlyReport(this);
            Watch = new SystemWatch(database, settings, Events);
            Central = new CentralReporter(this);
            Update = new Updater(this);
        }

        /// <summary>Merkezi panele durum bildirimi (heartbeat, outbound; varsayılan açık).</summary>
        public CentralReporter Central { get; private set; }

        /// <summary>Merkezi versiyon güncelleme (Faz A: kontrol + indir + doğrula; uygulama Faz B).</summary>
        public Updater Update { get; private set; }

        /// <summary>Lisans doğrulama; geçerli değilse izleme / koruma durur (LicenseGate).</summary>
        public LicenseManager License { get; private set; }

        /// <summary>SecurityGuard SQL Server Audit nesnesi (kurma / kaldırma / nöbet).</summary>
        public SqlAuditManager SqlAudit { get; private set; }

        /// <summary>IP → ülke veritabanı (port kısıtı, oturum geçmişi).</summary>
        public GeoIpDatabase Geo { get; private set; }

        /// <summary>Servis / klasör koruması, kapanış ve beklenmedik kapanma bildirimi.</summary>
        public SelfProtection Protection { get; private set; }

        /// <summary>Seçilen Windows servislerinin takibi (durursa mail, isteğe bağlı yeniden başlatma).</summary>
        public ServiceWatch Services { get; private set; }

        /// <summary>RDP / konsol oturum geçmişi ve mesai dışı giriş kontrolü.</summary>
        public RdpSessionTracker RdpSessions { get; private set; }

        /// <summary>IIS FTP / web / SMTP loglarından başarısız girişler.</summary>
        public IisLogCollector IisLogs { get; private set; }

        /// <summary>Tuzak dosyalar (fidye yazılımı erken uyarısı).</summary>
        public CanaryFiles Canary { get; private set; }

        /// <summary>Port IP kısıtları (sadece kullanıcı kaydedince firewall'a yazılır).</summary>
        public PortRestrictions PortRestrictions { get; private set; }

        /// <summary>SQL Yedekleme (varsayılan kapalı).</summary>
        public SqlBackup Backup { get; private set; }

        /// <summary>RDP ayarı / disk / modül hatası nöbeti.</summary>
        public SystemWatch Watch { get; private set; }

        /// <summary>Aylık yönetici raporu.</summary>
        public MonthlyReport MonthlyReports { get; private set; }

        /// <summary>Günlük rapor maili.</summary>
        public DailyReport Reports { get; private set; }

        /// <summary>SQL Bakımı (varsayılan kapalı; kullanıcı açarsa bakım penceresinde uygular).</summary>
        public SqlMaintenance Maintenance { get; private set; }

        /// <summary>Performans / Güvenlik Önerileri taramaları (sadece okur).</summary>
        public CheckRunner Checks { get; private set; }

        /// <summary>SQL periyodik kontrolleri.</summary>
        public SqlMonitor SqlMonitor { get; private set; }

        /// <summary>Windows olay kanallarını izleyen toplayıcılar.</summary>
        public CollectorHost Collectors { get; private set; }

        /// <summary>Başarısız giriş sayacı, ban / ban kaldırma, firewall kuralları.</summary>
        public BanManager Ban { get; private set; }

        /// <summary>Güvenlik olaylarını kurala göre loga yazar / mail atar.</summary>
        public EventRecorder Events { get; private set; }

        public SqlConnectionStore Sql { get; private set; }

        /// <summary>SQLite sorgu ekranı.</summary>
        public SqliteConsole Sqlite { get; private set; }

        public MailService Mail { get; private set; }

        public RuleStore Rules { get; private set; }

        public AdminAccount Admin { get; private set; }

        public Whitelist Whitelist { get; private set; }

        public GuardDatabase Database { get; private set; }

        public SettingsStore Settings { get; private set; }

        /// <summary>EventLog bütünlük zinciri. Anahtar çözülemezse null (zincir yazılmaz, hata loglanır).</summary>
        public HmacChain Chain { get; private set; }

        /// <summary>DB'yi açar, ayarları yükler, HMAC anahtarını hazırlar. Hata olursa açılan her şeyi kapatıp fırlatır.</summary>
        public static GuardContext Open(bool isWindowsService = false)
        {
            var db = new GuardDatabase(AppPaths.DatabaseFile, AppPaths.BackupFolder);
            try
            {
                db.Open();
                AppLog.Info(Module, "Veritabanı hazır: " + AppPaths.DatabaseFile + " (şema v" + db.SchemaVersion + ").");

                var settings = new SettingsStore(db);
                settings.Load();
                ApplyLogLevel(settings);
                settings.Changed += key =>
                {
                    if (key == SettingKeys.LogLevel) ApplyLogLevel(settings);
                };

                var context = new GuardContext(db, settings, LoadOrCreateChain(settings), isWindowsService);
                context.Whitelist.SyncSystemEntries();
                context.Whitelist.Reload();
                context.Rules.SeedMissing();
                context.Rules.Reload();
                context.Mail.Start();
                context.Events.Batcher.Start();

                // Kayıtlı lisans durumu, izleyiciler başlamadan önce okunur (lisans yoksa olaylar işlenmez)
                context.License.Start();

                // Kapalıyken ban kuralları silinmiş / değiştirilmiş olabilir: açılışta kontrol (açılışı bekletmez)
                // ve çalışırken her 5 dakikada bir + bizim kurala dokunan firewall olayında hemen
                context.Ban.StartWatchdog();
                context.PortRestrictions.Start();
                context.Canary.Start();
                context.Collectors.Start();
                context.IisLogs.Start();
                context.RdpSessions.Start();
                context.Services.Start();
                context.Protection.Start();
                context.SqlMonitor.Start();
                context.Checks.Start();
                context.Maintenance.Start();
                context.Backup.Start();
                context.Reports.Start();
                context.MonthlyReports.Start();
                context.Watch.Start();
                context.Central.Start();
                context.Update.Start();
                return context;
            }
            catch
            {
                db.Dispose();
                throw;
            }
        }

        private static void ApplyLogLevel(SettingsStore settings)
        {
            if (!settings.Contains(SettingKeys.LogLevel)) return; // ayar yoksa mevcut seviye kalır (konsol modunda Debug)
            AppLog.MinLevel = settings.GetEnum(SettingKeys.LogLevel, LogLevel.Info);
        }

        private static HmacChain LoadOrCreateChain(SettingsStore settings)
        {
            if (!settings.Contains(SettingKeys.HmacKey))
            {
                byte[] key = HmacChain.NewKey();
                // Anahtar DB'ye kalıcı yazılmadan kullanılmaz (yoksa bir sonraki açılışta zincir tutmaz)
                if (!settings.SetSecretAsync(SettingKeys.HmacKey, Convert.ToBase64String(key)).Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Log bütünlük anahtarı veritabanına yazılamadı.");
                AppLog.Info(Module, "Log bütünlük anahtarı oluşturuldu.");
                return new HmacChain(key);
            }

            string stored = settings.GetSecret(SettingKeys.HmacKey);
            if (stored == null)
            {
                // Anahtar var ama çözülemiyor (DB başka kullanıcı/makineden gelmiş olabilir). Yeni anahtar üretilmez:
                // üretilirse eski zincir doğrulanamaz hale gelir. Zincir kapalı kalır, durum loglanır.
                AppLog.Error(Module, "Log bütünlük anahtarı çözülemedi. Olay logu bütünlük zinciri devre dışı.");
                return null;
            }
            return new HmacChain(Convert.FromBase64String(stored));
        }

        public void Dispose()
        {
            // Önce DB kullanan arka plan işleri durur, sonra DB kapanır
            try
            {
                if (Ban != null) Ban.StopWatchdog();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Firewall kontrolü durdurulamadı.", ex);
            }
            try
            {
                if (License != null) License.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Lisans doğrulama durdurulamadı.", ex);
            }
            try
            {
                if (Protection != null) Protection.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Koruma durdurulamadı.", ex);
            }
            try
            {
                if (Services != null) Services.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Servis takibi durdurulamadı.", ex);
            }
            try
            {
                if (RdpSessions != null) RdpSessions.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Oturum takibi durdurulamadı.", ex);
            }
            try
            {
                if (IisLogs != null) IisLogs.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "IIS log okuyucu durdurulamadı.", ex);
            }
            try
            {
                if (Canary != null) Canary.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Tuzak dosya izleme durdurulamadı.", ex);
            }
            try
            {
                if (PortRestrictions != null) PortRestrictions.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Port kısıtı nöbeti durdurulamadı.", ex);
            }
            try
            {
                if (Update != null) Update.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Güncelleme kontrolü durdurulamadı.", ex);
            }
            try
            {
                if (Central != null) Central.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Merkezi panel bildirimi durdurulamadı.", ex);
            }
            try
            {
                if (Watch != null) Watch.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Sistem nöbeti durdurulamadı.", ex);
            }
            try
            {
                if (MonthlyReports != null) MonthlyReports.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Aylık rapor durdurulamadı.", ex);
            }
            try
            {
                if (Reports != null) Reports.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Günlük rapor durdurulamadı.", ex);
            }
            try
            {
                if (Backup != null) Backup.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "SQL yedekleme durdurulamadı.", ex);
            }
            try
            {
                if (Maintenance != null) Maintenance.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "SQL bakımı durdurulamadı.", ex);
            }
            try
            {
                if (Checks != null) Checks.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Taramalar durdurulamadı.", ex);
            }
            try
            {
                if (SqlMonitor != null) SqlMonitor.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "SQL izleme durdurulamadı.", ex);
            }
            try
            {
                if (Collectors != null) Collectors.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Olay toplayıcılar durdurulamadı.", ex);
            }
            try
            {
                // Toplanmayı bekleyen olay mailleri kuyruğa alınır (mail servisi kapanmadan önce)
                if (Events != null) Events.Batcher.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Bekleyen olay mailleri kuyruğa alınamadı.", ex);
            }
            try
            {
                if (Mail != null)
                {
                    Mail.FlushBeforeStop(TimeSpan.FromSeconds(10)); // kapanış / kritik mailler dışarı çıksın
                    Mail.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Mail servisi durdurulamadı.", ex);
            }
            if (Database != null) Database.Dispose();
        }
    }
}
