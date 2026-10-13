namespace SecurityGuard.Shared
{
    /// <summary>
    /// Service ve UI'nin ortak kullandığı sabitler.
    /// Bu projede sadece veri tanımları olur: iş mantığı, SQLite, DevExpress referansı yok.
    /// </summary>
    public static class GuardConstants
    {
        /// <summary>Windows servis adı. Kurulum, DACL ve kurtarma ayarları buna bağlı; değiştirilmemeli.</summary>
        public const string ServiceName = "SecurityGuard";

        /// <summary>UI - Service yönetim kanalı.</summary>
        public const string PipeName = "SecurityGuard.Pipe";

        /// <summary>SQL Server login'i (tam yetkili): izleme, audit, tarama, bakım ve yedekleme bu hesapla yapılır.</summary>
        public const string SqlLoginName = "securityGuardWar";

        /// <summary>Program kök klasörü altındaki veritabanı klasörü.</summary>
        public const string DatabaseFolderName = "Database";

        public const string DatabaseFileName = "guard.db";

        /// <summary>Ban firewall kurallarının ön eki: SecurityGuard-Block-001, -002...</summary>
        public const string FirewallRulePrefix = "SecurityGuard-Block";

        /// <summary>Port IP kısıtı kurallarının ön eki: SecurityGuard-Restrict-001-TCP (gelen, engelle: izinli IP'ler dışındakiler).</summary>
        public const string PortRestrictionRulePrefix = "SecurityGuard-Restrict";

        /// <summary>Canlı lisans doğrulama aralığı (dakika).</summary>
        public const int LicenseValidateIntervalMinutes = 5;

        /// <summary>Lisans sunucusuna ulaşılamazsa son geçerli doğrulamadan sonra çalışmaya devam süresi (gün).</summary>
        public const int LicenseOfflineToleranceDays = 7;

        /// <summary>Çevrimdışı uyarısı bu kadar gün ulaşılamayınca başlar (sonra günde bir).</summary>
        public const int LicenseOfflineWarnDays = 3;

        /// <summary>
        /// Lisans sunucusu varsayılan adresi (Lisans sayfasından değiştirilebilir).
        /// Gerçek adres git'e girmeyen LicenseServer.Local.cs dosyasındadır; yoksa LicenseServer.cs içindeki yer tutucu kullanılır.
        /// </summary>
        public const string LicenseDefaultApiUrl = LicenseServer.DefaultApiUrl;
    }
}
