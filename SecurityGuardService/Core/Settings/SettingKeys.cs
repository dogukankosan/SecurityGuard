namespace SecurityGuardService.Core.Settings
{
    /// <summary>
    /// Settings tablosundaki anahtar adları. Yeni ayar eklendikçe buraya eklenir.
    /// Anahtar adları DB'de saklandığı için yayınlandıktan sonra değiştirilmemeli.
    /// </summary>
    internal static class SettingKeys
    {
        /// <summary>Uygulama logu seviyesi (LogLevel adı: Debug, Info...).</summary>
        public const string LogLevel = "log.level";

        /// <summary>Lisans sunucusu adresi (boş = GuardConstants.LicenseDefaultApiUrl).</summary>
        public const string LicenseApiUrl = "license.apiUrl";

        /// <summary>EventLog HMAC zinciri anahtarı (şifreli, base64).</summary>
        public const string HmacKey = "security.hmacKey";

        /// <summary>UI girişinde hesabı kilitleyen hatalı deneme sayısı (varsayılan 5).</summary>
        public const string AuthMaxFailedLogins = "auth.maxFailedLogins";

        /// <summary>Hesap kilit süresi, dakika (varsayılan 15).</summary>
        public const string AuthLockMinutes = "auth.lockMinutes";

        /// <summary>İşlem yapılmazsa arayüz oturumunun kapanacağı süre, dakika (varsayılan 30).</summary>
        public const string AuthSessionIdleMinutes = "auth.sessionIdleMinutes";

        /// <summary>Günlük rapor ve sağlık / performans taraması saati, "HH:mm" (varsayılan 20:00).</summary>
        public const string ReportDailyTime = "report.dailyTime";
        public const string ReportMonthlyTime = "report.monthlyTime";

        /// <summary>Son gönderilen aylık raporun ayı ("2026-09").</summary>
        public const string ReportLastMonthly = "report.lastMonthly";

        /// <summary>SQL Server anlık görüntü karşılaştırma aralığı, dakika (varsayılan 5).</summary>
        public const string SqlBaselineIntervalMinutes = "sql.baselineIntervalMinutes";

        /// <summary>Bizim SQL Server Audit nesnesi durdurulursa yeniden başlatılsın mı (varsayılan açık).</summary>
        public const string SqlAuditAutoRestart = "sql.auditAutoRestart";

        /// <summary>Kritik olmayan olay maillerinin toplanma süresi, dakika (0 = hemen; varsayılan 2).</summary>
        public const string MailBatchMinutes = "mail.batchMinutes";

        /// <summary>Disk doluluk uyarı eşiği, yüzde (varsayılan 90).</summary>
        public const string DiskAlarmPercent = "disk.alarmPercent";

        /// <summary>Index parçalanması: bu yüzdeden itibaren REORGANIZE önerilir (varsayılan 5).</summary>
        public const string PerfFragReorganizePercent = "perf.fragReorganizePercent";

        /// <summary>Index parçalanması: bu yüzdeden itibaren REBUILD önerilir (varsayılan 30).</summary>
        public const string PerfFragRebuildPercent = "perf.fragRebuildPercent";

        /// <summary>Bu sayfa sayısından küçük index'ler parçalanma için dikkate alınmaz (varsayılan 1000 = ~8 MB).</summary>
        public const string PerfMinPageCount = "perf.minPageCount";

        /// <summary>SQL Bakımı ayarları (MaintenanceConfigDto JSON). Yoksa bakım kapalı.</summary>
        public const string MaintenanceConfig = "maint.config";

        /// <summary>Son otomatik bakımın bakım penceresi tarihi (aynı pencerede iki kez çalışmasın).</summary>
        public const string MaintenanceLastWindow = "maint.lastWindow";

        /// <summary>Son zamanlanmış günlük raporun dönem sonu (UTC, DbTime); sonraki rapor buradan başlar.</summary>
        public const string ReportLastPeriodEnd = "report.lastPeriodEnd";

        /// <summary>Son zamanlanmış raporun yerel tarihi (yyyy-MM-dd): aynı gün iki kez gönderilmesin.</summary>
        public const string ReportLastDate = "report.lastDate";

        /// <summary>Son otomatik bütünlük kontrolünde bulunan oynama özeti (aynı sorun her gün tekrar olay üretmesin).</summary>
        public const string ReportLastIntegrity = "report.lastIntegrity";

        /// <summary>SQL Yedekleme ayarları (BackupConfigDto JSON). Yoksa yedekleme kapalı.</summary>
        public const string BackupConfig = "backup.config";

        /// <summary>Yedek arşiv şifresi (şifreli).</summary>
        public const string BackupPassword = "backup.password";

        /// <summary>Son otomatik yedeğin planlanan zamanı (aynı planda iki kez çalışmasın).</summary>
        public const string BackupLastSlot = "backup.lastSlot";

        /// <summary>İşlemci kullanım uyarı eşiği, yüzde (varsayılan 90).</summary>
        public const string CpuAlarmPercent = "resource.cpuAlarmPercent";

        /// <summary>Bellek kullanım uyarı eşiği, yüzde (varsayılan 90).</summary>
        public const string RamAlarmPercent = "resource.ramAlarmPercent";

        /// <summary>Eşik bu kadar dakika aralıksız aşılırsa uyarı (varsayılan 10).</summary>
        public const string ResourceAlarmMinutes = "resource.alarmMinutes";

        // Servis koruması
        public const string ProtectService = "protect.service";
        public const string ProtectData = "protect.data";
        public const string ProtectBackup = "protect.backup";

        // Mesai saatleri (mesai dışı RDP girişi uyarısı)
        public const string WorkHoursAlarm = "workhours.alarm";
        public const string WorkDays = "workhours.days";
        public const string WorkStart = "workhours.start";
        public const string WorkEnd = "workhours.end";

        /// <summary>Kullanıcı istisnaları (JSON, RDP Oturumları sayfasından).</summary>
        public const string WorkHoursExceptions = "workhours.exceptions";

        /// <summary>UI teması: DevExpress skin adı.</summary>
        public const string UiThemeSkin = "ui.theme.skin";

        /// <summary>UI teması: renk paleti adı.</summary>
        public const string UiThemePalette = "ui.theme.palette";

        // --- Mail (SMTP)
        public const string MailHost = "mail.host";
        public const string MailPort = "mail.port";
        /// <summary>MailSecurity enum değeri (sayı).</summary>
        public const string MailSecurity = "mail.security";
        public const string MailUser = "mail.user";
        /// <summary>Şifreli.</summary>
        public const string MailPassword = "mail.password";
        public const string MailFromAddress = "mail.fromAddress";
        public const string MailFromName = "mail.fromName";
        /// <summary>Her mailin sonuna eklenen HTML imza.</summary>
        public const string MailSignatureHtml = "mail.signatureHtml";
    }
}
