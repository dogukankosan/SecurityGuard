using System.Collections.Generic;
using System.Linq;
using SecurityGuard.Shared.Enums;

namespace SecurityGuardService.Engine.Rules
{
    /// <summary>İzlenebilen bir olay tipinin tanımı ve varsayılan kuralı.</summary>
    internal sealed class EventDefinition
    {
        public EventDefinition(string type, string category, string name, string source, Severity severity,
            bool mail, bool canBan = false, int threshold = 0)
        {
            Type = type;
            Category = category;
            Name = name;
            Source = source;
            DefaultSeverity = severity;
            DefaultMail = mail;
            CanBan = canBan;
            DefaultThreshold = threshold;
        }

        /// <summary>DB'deki anahtar. Yayınlandıktan sonra değiştirilmez.</summary>
        public string Type { get; private set; }
        public string Category { get; private set; }
        public string Name { get; private set; }
        public string Source { get; private set; }
        public Severity DefaultSeverity { get; private set; }
        public bool DefaultMail { get; private set; }
        public bool CanBan { get; private set; }
        public int DefaultThreshold { get; private set; }
    }

    /// <summary>
    /// İzlenen tüm olay tipleri (dokümandaki bölüm 2 ve 5.0).
    /// Kritik olaylarda mail varsayılan açık. Ban sadece dış RDP / SQL başarısız giriş kurallarında.
    /// Yeni olay eklemek için listeye satır eklemek yeterli; servis açılışta DB'ye ekler.
    /// </summary>
    internal static class EventCatalog
    {
        public const string CatLogin = "Giriş";
        public const string CatWindows = "Windows";
        public const string CatSql = "SQL Server";
        public const string CatGuard = "SecurityGuard";
        public const string CatReport = "Raporlar";

        public const string DailyReport = "Report.Daily";
        public const string MonthlyReport = "Report.Monthly";

        public const int DefaultBanThreshold = 10;

        // Kod içinde kullanılan olay tipleri (collector ve motorlar bunlarla olay üretir)
        public const string RdpLoginFailed = "Rdp.LoginFailed";
        public const string SqlLoginFailed = "Sql.LoginFailed";
        public const string NetLoginFailed = "Net.LoginFailed";
        public const string FtpLoginFailed = "Ftp.LoginFailed";
        public const string WebLoginFailed = "Web.LoginFailed";
        public const string SmtpLoginFailed = "Smtp.LoginFailed";

        public static readonly IReadOnlyList<EventDefinition> All = new List<EventDefinition>
        {
            // --- Giriş
            new EventDefinition(RdpLoginFailed, CatLogin, "RDP girişi başarısız", "RdpCoreTS 140, Security 4625 (giriş tipi 10)", Severity.Uyari, false, true, DefaultBanThreshold),
            new EventDefinition(SqlLoginFailed, CatLogin, "SQL girişi başarısız", "SQL 18456 (Application log)", Severity.Uyari, false, true, DefaultBanThreshold),
            new EventDefinition(NetLoginFailed, CatLogin, "Ağ girişi başarısız (uzak RDP/NLA, SMB, WinRM)", "Security 4625 (giriş tipi 3)", Severity.Uyari, false, true, DefaultBanThreshold),
            new EventDefinition(FtpLoginFailed, CatLogin, "FTP girişi başarısız", "IIS FTP logu (PASS 530)", Severity.Uyari, false, true, DefaultBanThreshold),
            new EventDefinition(WebLoginFailed, CatLogin, "Web / IIS girişi başarısız (Basic, OWA, WebDAV)", "IIS W3C logu (401.1)", Severity.Uyari, false, true, DefaultBanThreshold),
            new EventDefinition(SmtpLoginFailed, CatLogin, "SMTP girişi başarısız", "IIS SMTP logu (AUTH 535)", Severity.Uyari, false, true, DefaultBanThreshold),
            new EventDefinition("Ban.IpBanned", CatLogin, "IP banlandı", "Ban motoru", Severity.Kritik, true),
            new EventDefinition("Login.SuccessAfterFailures", CatLogin, "Başarısız denemelerden sonra başarılı giriş", "Security 4624 + 4625 geçmişi", Severity.Kritik, true),
            new EventDefinition("Login.Spraying", CatLogin, "Aynı kullanıcı adına çok IP'den deneme", "4625 / 18456 kullanıcı adı sayacı", Severity.Kritik, true),
            new EventDefinition("Login.GuardAccountExternal", CatLogin, "Dış IP'den securityGuardWar ile deneme", "SQL 18456", Severity.Kritik, true),
            new EventDefinition("Rdp.OffHoursLogin", CatLogin, "Mesai dışı RDP girişi", "Oturum olayları + mesai saatleri (Genel Ayarlar)", Severity.Uyari, true),
            new EventDefinition("Rdp.LoginNewIp", CatLogin, "Daha önce görülmemiş IP'den RDP girişi", "TerminalServices-LocalSessionManager 21 / 25", Severity.Uyari, true),
            new EventDefinition("Rdp.SessionStarted", CatLogin, "RDP yeni oturum", "TerminalServices-LocalSessionManager 21 / 25", Severity.Bilgi, false),
            new EventDefinition("Ui.AdminLockedOut", CatLogin, "SecurityGuard yönetici girişi kilitlendi", "SecurityGuard arayüzü", Severity.Kritik, true),

            // --- Windows
            new EventDefinition("Win.UserCreated", CatWindows, "Yeni Windows kullanıcısı", "Security 4720", Severity.Kritik, true),
            new EventDefinition("Win.PasswordReset", CatWindows, "Kullanıcı şifresi sıfırlandı / değiştirildi", "Security 4723 / 4724", Severity.Kritik, true),
            new EventDefinition("Win.AdminGroupAdded", CatWindows, "Administrators grubuna üye eklendi", "Security 4732", Severity.Kritik, true),
            new EventDefinition("Win.GroupMemberAdded", CatWindows, "Diğer gruplara üye eklendi", "Security 4728 / 4732 / 4756", Severity.Uyari, false),
            new EventDefinition("Win.UserEnabledDisabled", CatWindows, "Kullanıcı etkinleştirildi / devre dışı bırakıldı", "Security 4722 / 4725", Severity.Uyari, true),
            new EventDefinition("Win.ScheduledTaskCreated", CatWindows, "Zamanlanmış görev oluşturuldu / değiştirildi", "Görev listesi karşılaştırması, Security 4698", Severity.Kritik, true),
            new EventDefinition("Win.ServiceInstalled", CatWindows, "Yeni servis kuruldu", "System 7045, Security 4697", Severity.Kritik, true),
            new EventDefinition("Win.EventLogCleared", CatWindows, "Windows olay logu temizlendi", "Security 1102, System 104", Severity.Kritik, true),
            new EventDefinition("Win.AuditPolicyChanged", CatWindows, "Audit politikası değişti", "Security 4719", Severity.Kritik, true),
            new EventDefinition("Win.FirewallRuleChanged", CatWindows, "Firewall kuralı / portu açıldı, değişti veya silindi", "Firewall 2004 / 2005 / 2006, Security 4946-4948", Severity.Kritik, true),
            new EventDefinition("Win.FirewallDisabled", CatWindows, "Firewall profili kapatıldı", "Firewall 2003, Security 4950", Severity.Kritik, true),
            new EventDefinition("Win.DefenderChanged", CatWindows, "Defender kapatıldı / ayarı değişti", "Defender 5001 / 5007", Severity.Kritik, true),
            new EventDefinition("Win.RansomwarePrecursor", CatWindows, "Fidye yazılımı öncesi komut (gölge kopya / yedek silme, kurtarmayı kapatma)", "Security 4688 (işlem oluşturma)", Severity.Kritik, true),
            new EventDefinition("Win.HighCpu", CatWindows, "İşlemci kullanımı uzun süre yüksek", "İşlemci / bellek izleme (Genel Ayarlar)", Severity.Uyari, true),
            new EventDefinition("Win.HighMemory", CatWindows, "Bellek kullanımı uzun süre yüksek", "İşlemci / bellek izleme (Genel Ayarlar)", Severity.Uyari, true),
            new EventDefinition("Svc.Stopped", CatWindows, "İzlenen servis durdu / kaldırıldı", "Servis takibi", Severity.Kritik, true),
            new EventDefinition("Svc.Recovered", CatWindows, "İzlenen servis tekrar çalışıyor", "Servis takibi", Severity.Bilgi, true),
            new EventDefinition("Win.RemoteToolInstalled", CatWindows, "Uzaktan erişim programı kuruldu (AnyDesk, TeamViewer...)", "Program listesi", Severity.Kritik, true),
            new EventDefinition("Win.SoftwareInstalled", CatWindows, "Yeni program kuruldu", "Program listesi (Programlar ve Özellikler)", Severity.Uyari, true),
            new EventDefinition("Win.SoftwareRemoved", CatWindows, "Program kaldırıldı", "Program listesi (Programlar ve Özellikler)", Severity.Uyari, true),
            new EventDefinition("Win.UsbStorage", CatWindows, "USB bellek / harici disk takıldı", "USB izleme (WMI)", Severity.Uyari, true),
            new EventDefinition("Win.CanaryTouched", CatWindows, "Tuzak dosyaya dokunuldu (fidye yazılımı belirtisi)", "Tuzak dosyalar", Severity.Kritik, true),
            new EventDefinition("Win.RdpSettingsChanged", CatWindows, "RDP portu / NLA / RDP aç-kapa ayarı değişti", "Registry izleme", Severity.Kritik, true),

            // --- SQL Server
            new EventDefinition("Sql.LoginChanged", CatSql, "SQL login oluşturuldu / silindi / değiştirildi", "SQL Audit", Severity.Kritik, true),
            new EventDefinition("Sql.PasswordChanged", CatSql, "SQL login şifresi değişti (sa dahil)", "SQL Audit", Severity.Kritik, true),
            new EventDefinition("Sql.SaStateChanged", CatSql, "sa hesabı etkinleştirildi / devre dışı bırakıldı", "SQL Audit", Severity.Kritik, true),
            new EventDefinition("Sql.ServerRoleChanged", CatSql, "sysadmin / sunucu rolü üyeliği değişti", "SQL Audit", Severity.Kritik, true),
            new EventDefinition("Sql.PermissionChanged", CatSql, "Yetkili hesaba sunucu yetkisi verildi / alındı", "SQL Audit", Severity.Kritik, true),
            new EventDefinition("Sql.CredentialChanged", CatSql, "Credential oluşturuldu / değişti / silindi", "SQL Audit + baseline", Severity.Kritik, true),
            new EventDefinition("Sql.KeyChanged", CatSql, "Sertifika / anahtar / master key işlemi", "SQL Audit + baseline", Severity.Kritik, true),
            new EventDefinition("Sql.TdeEnabled", CatSql, "Veritabanı şifrelemesi (TDE) açıldı", "SQL Audit + baseline", Severity.Kritik, true),
            new EventDefinition("Sql.DatabaseDropped", CatSql, "Veritabanı silindi", "SQL Audit + baseline", Severity.Kritik, true),
            new EventDefinition("Sql.DatabaseChanged", CatSql, "Veritabanı oluşturuldu / değiştirildi", "SQL Audit", Severity.Uyari, false),
            new EventDefinition("Sql.ServerTriggerChanged", CatSql, "Sunucu trigger'ı değişti", "SQL Audit + baseline", Severity.Kritik, true),
            new EventDefinition("Sql.DangerousConfigChanged", CatSql, "Tehlikeli SQL ayarı değişti (xp_cmdshell, CLR, Ole Automation...)", "SQL Audit + baseline", Severity.Kritik, true),
            new EventDefinition("Sql.LinkedServerChanged", CatSql, "Linked server eklendi / silindi", "SQL Audit + baseline", Severity.Kritik, true),
            new EventDefinition("Sql.ErrorLogCleared", CatSql, "SQL error log döndürüldü / silindi", "SQL Audit, log klasörü izleme", Severity.Kritik, true),
            new EventDefinition("Sql.MaintenanceDone", CatSql, "SQL bakımı yapıldı", "SQL Bakımı", Severity.Bilgi, false),
            new EventDefinition("Sql.MaintenanceFailed", CatSql, "SQL bakımı hatalı / yarım kaldı", "SQL Bakımı", Severity.Uyari, true),
            new EventDefinition("Sql.AuditStopped", CatSql, "SQL izleme (Audit) durduruldu / silindi", "SQL Audit, periyodik kontrol", Severity.Kritik, true),
            new EventDefinition("Sql.Unreachable", CatSql, "SQL Server'a / izlemeye bağlanılamıyor (hesap veya servis)", "SQL izleme kontrolü", Severity.Kritik, true),
            new EventDefinition("Sql.Reconnected", CatSql, "SQL Server'a tekrar bağlanıldı, izleme devam ediyor", "SQL izleme kontrolü", Severity.Bilgi, true),

            // --- SecurityGuard'ın kendisi
            new EventDefinition("Guard.ServiceTamper", CatGuard, "Servisi durdurma / ayarını değiştirme denemesi", "System 7036 / 7040, Security 4670", Severity.Kritik, true),
            new EventDefinition("Guard.UnexpectedStop", CatGuard, "SecurityGuard beklenmedik şekilde kapandı (sonlandırıldı / çöktü / elektrik)", "Açılış kontrolü", Severity.Kritik, true),
            new EventDefinition("Guard.ServiceStopped", CatGuard, "SecurityGuard servisi durduruldu (arayüzden / sunucu kapanışı)", "Servis", Severity.Uyari, true),
            new EventDefinition("Guard.DataTamper", CatGuard, "Veritabanı / yedek klasörüne silme veya izin değiştirme denemesi", "Security 4663 / 4670", Severity.Kritik, true),
            new EventDefinition("Guard.LogIntegrity", CatGuard, "Olay logu bütünlüğü bozuldu", "HMAC zinciri", Severity.Kritik, true),
            new EventDefinition("Guard.BanRuleTampered", CatGuard, "Ban firewall kuralı silindi / değiştirildi", "Firewall kontrolü", Severity.Kritik, true),
            new EventDefinition("Guard.PortRuleTampered", CatGuard, "Port kısıtı firewall kuralı silindi / değiştirildi", "Firewall kontrolü", Severity.Kritik, true),
            new EventDefinition("Guard.PortRestrictionChanged", CatGuard, "Port kısıtı uygulandı / kapatıldı / silindi", "Port Kısıtları", Severity.Uyari, true),
            new EventDefinition("Guard.PortRestrictionReverted", CatGuard, "Port kısıtı onaylanmadığı için geri alındı", "Port Kısıtları", Severity.Uyari, true),
            new EventDefinition("Sql.BackupDone", CatSql, "SQL yedeği alındı", "SQL Yedekleme", Severity.Bilgi, false),
            new EventDefinition("Guard.BackupFailed", CatGuard, "SQL yedekleme başarısız", "Yedekleme modülü", Severity.Kritik, true),
            new EventDefinition("Guard.LicenseExpired", CatGuard, "Lisans bitti / iptal edildi", "Lisans doğrulama", Severity.Kritik, true),
            new EventDefinition("Guard.LicenseExpiring", CatGuard, "Lisansın bitmesine 7 / 3 / 1 gün kaldı", "Lisans doğrulama", Severity.Uyari, true),
            new EventDefinition("Guard.LicenseOffline", CatGuard, "Lisans sunucusuna 3 günden uzun süredir ulaşılamıyor", "Lisans doğrulama", Severity.Uyari, true),
            new EventDefinition("Guard.LicenseRestored", CatGuard, "Lisans tekrar geçerli, koruma devam ediyor", "Lisans doğrulama", Severity.Bilgi, true),
            new EventDefinition("Guard.DiskLow", CatGuard, "Disk dolmak üzere", "Disk kontrolü", Severity.Uyari, true),
            new EventDefinition("Guard.ModuleError", CatGuard, "Bir modül art arda hata veriyor", "Servis içi", Severity.Uyari, true),

            // --- Raporlar
            new EventDefinition(MonthlyReport, CatReport, "Aylık yönetici raporu", "Her ayın 1'i (saat Genel Ayarlar'dan)", Severity.Bilgi, true),
            new EventDefinition(DailyReport, CatReport, "Günlük güvenlik ve performans raporu", "Her gün (saat Genel Ayarlar'dan)", Severity.Bilgi, true),
            new EventDefinition("Check.NewCritical", CatReport, "Taramada yeni kritik bulgu", "Performans / Güvenlik Önerileri taraması", Severity.Kritik, true),
        };

        private static readonly Dictionary<string, EventDefinition> ByType = All.ToDictionary(d => d.Type);

        public static EventDefinition Find(string type)
        {
            EventDefinition d;
            return type != null && ByType.TryGetValue(type, out d) ? d : null;
        }
    }
}
