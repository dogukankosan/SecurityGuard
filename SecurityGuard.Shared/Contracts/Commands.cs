namespace SecurityGuard.Shared.Contracts
{
    /// <summary>
    /// Pipe komut adları. Yeni ekran / işlem geldikçe buraya eklenir.
    /// </summary>
    public static class Commands
    {
        // --- Girişsiz
        /// <summary>Servis durumu: sürüm, hazır mı, yönetici var mı. Payload yok → <see cref="Dto.StatusDto"/>.</summary>
        public const string GetStatus = "System.GetStatus";

        /// <summary>İlk kurulumda tek yönetici hesabını oluşturur (sadece hiç yönetici yokken). → <see cref="Dto.CreateAdminRequest"/>.</summary>
        public const string CreateAdmin = "Auth.CreateAdmin";

        /// <summary>Giriş. <see cref="Dto.LoginRequest"/> → <see cref="Dto.LoginResult"/>.</summary>
        public const string Login = "Auth.Login";

        // --- Giriş gerekli
        public const string Logout = "Auth.Logout";

        /// <summary>Oturum hâlâ geçerli mi (ve süresini uzatır).</summary>
        public const string Ping = "System.Ping";

        /// <summary>UI temasını kaydeder. <see cref="Dto.ThemeDto"/>.</summary>
        public const string SetTheme = "Ui.SetTheme";

        // --- İlk kurulum sihirbazı
        /// <summary>→ <see cref="Dto.SetupStatusDto"/>.</summary>
        public const string SetupStatus = "Setup.Status";

        /// <summary>Zorunlu adımlar tamamsa kurulumu bitirir ve ilk güvenlik taramasını başlatır → <see cref="Dto.SetupStatusDto"/>.</summary>
        public const string SetupComplete = "Setup.Complete";

        // --- Panel
        /// <summary>→ <see cref="Dto.DashboardDto"/> (durum kutucukları, son olaylar, yapılacaklar).</summary>
        public const string DashboardGet = "Dashboard.Get";

        // --- İşlem kaydı
        /// <summary><see cref="Dto.AuditTrailQuery"/> → <see cref="Dto.AuditTrailResult"/>.</summary>
        public const string AuditTrailQuery = "AuditTrail.Query";

        // --- Beyaz liste
        /// <summary>→ List&lt;<see cref="Dto.WhitelistEntry"/>&gt;.</summary>
        public const string WhitelistList = "Whitelist.List";

        /// <summary><see cref="Dto.WhitelistAddRequest"/> → eklenen <see cref="Dto.WhitelistEntry"/>.</summary>
        public const string WhitelistAdd = "Whitelist.Add";

        /// <summary><see cref="Dto.IdListRequest"/>. Sistem kayıtları silinemez.</summary>
        public const string WhitelistDelete = "Whitelist.Delete";

        // --- Ban listesi
        /// <summary>→ <see cref="Dto.BanListResult"/> (banlılar + deneme sayaçları + firewall durumu).</summary>
        public const string BanList = "Ban.List";

        /// <summary><see cref="Dto.BanAddRequest"/>: elle ban. Korunan adresler (iç ağ, beyaz liste...) reddedilir.</summary>
        public const string BanAdd = "Ban.Add";

        /// <summary><see cref="Dto.IpListRequest"/>: ban kaldır (isteğe bağlı sayaç sıfırlama).</summary>
        public const string BanRemove = "Ban.Remove";

        /// <summary><see cref="Dto.IpListRequest"/>: deneme sayaçlarını sil (banı etkilemez).</summary>
        public const string BanResetAttempts = "Ban.ResetAttempts";

        /// <summary>Firewall kurallarını DB'ye göre kontrol eder / onarır → <see cref="Dto.BanListResult"/>.</summary>
        public const string BanSyncFirewall = "Ban.SyncFirewall";

        // --- Olay kuralları
        /// <summary>→ List&lt;<see cref="Dto.EventRuleDto"/>&gt;.</summary>
        public const string RulesList = "Rules.List";

        /// <summary><see cref="Dto.RuleSaveRequest"/> (sadece değişen kurallar).</summary>
        public const string RulesSave = "Rules.Save";

        /// <summary>Tüm kuralları varsayılan değerlere döndürür.</summary>
        public const string RulesReset = "Rules.Reset";

        // --- Mail
        /// <summary>→ <see cref="Dto.MailSettingsDto"/> (şifre dönmez).</summary>
        public const string MailGetSettings = "Mail.GetSettings";

        /// <summary><see cref="Dto.MailSettingsDto"/>.</summary>
        public const string MailSaveSettings = "Mail.SaveSettings";

        /// <summary><see cref="Dto.MailTestRequest"/>. Başarısızsa ErrorMessage'da sebep.</summary>
        public const string MailTest = "Mail.Test";

        /// <summary>→ List&lt;<see cref="Dto.RecipientDto"/>&gt;.</summary>
        public const string MailRecipientsList = "Mail.Recipients.List";

        /// <summary><see cref="Dto.RecipientAddRequest"/>.</summary>
        public const string MailRecipientAdd = "Mail.Recipients.Add";

        /// <summary><see cref="Dto.IdListRequest"/>.</summary>
        public const string MailRecipientDelete = "Mail.Recipients.Delete";

        /// <summary><see cref="Dto.RecipientSetEnabledRequest"/>.</summary>
        public const string MailRecipientSetEnabled = "Mail.Recipients.SetEnabled";

        /// <summary>→ <see cref="Dto.SignatureDto"/>.</summary>
        public const string MailSignatureGet = "Mail.Signature.Get";

        /// <summary><see cref="Dto.SignatureSaveRequest"/>.</summary>
        public const string MailSignatureSave = "Mail.Signature.Save";

        /// <summary><see cref="Dto.SignatureImageDto"/> (FileName + Base64) → ContentId atanmış <see cref="Dto.SignatureImageDto"/>.</summary>
        public const string MailSignatureAddImage = "Mail.Signature.AddImage";

        /// <summary><see cref="Dto.ContentIdRequest"/>.</summary>
        public const string MailSignatureDeleteImage = "Mail.Signature.DeleteImage";

        // --- SQL Server bağlantısı
        /// <summary>→ List&lt;<see cref="Dto.SqlInstanceDto"/>&gt; (tespit edilen + kayıtlı instance'lar).</summary>
        public const string SqlInstances = "Sql.Instances";

        /// <summary><see cref="Dto.SqlConnectionRequest"/> → <see cref="Dto.SqlTestResultDto"/>. Başarısızsa ErrorMessage'da sebep.</summary>
        public const string SqlTest = "Sql.Test";

        /// <summary><see cref="Dto.SqlConnectionRequest"/>. Aynı bilgilerle başarılı test şart.</summary>
        public const string SqlSave = "Sql.Save";

        // --- Olay logu
        /// <summary><see cref="Dto.EventLogQuery"/> → <see cref="Dto.EventLogResult"/> (en yeni önce).</summary>
        public const string EventLogQuery = "EventLog.Query";

        /// <summary>→ <see cref="Dto.EventLogVerifyResult"/> (HMAC zinciri kontrolü).</summary>
        public const string EventLogVerify = "EventLog.Verify";

        /// <summary><see cref="Dto.PasswordConfirmRequest"/>: şu anki bütünlük sorunlarını "incelendi, kabul edildi" olarak imzalar → <see cref="Dto.EventLogVerifyResult"/>.</summary>
        public const string EventLogAcceptIntegrity = "EventLog.AcceptIntegrity";

        // --- Genel ayarlar
        /// <summary>→ List&lt;<see cref="Dto.GeneralSettingDto"/>&gt; (grup, açıklama, varsayılan, sınırlar ile).</summary>
        public const string GeneralSettingsList = "Settings.General.List";

        /// <summary><see cref="Dto.GeneralSettingsSaveRequest"/> (sadece değişenler) → güncel liste.</summary>
        public const string GeneralSettingsSave = "Settings.General.Save";

        // --- SQLite sorgu ekranı (tamamen serbest; DB dosyası servis çalıştıkça kilitli olduğu için silinemez)
        /// <summary>→ <see cref="Dto.SqliteSchemaDto"/>.</summary>
        public const string SqliteSchema = "Sqlite.Schema";

        /// <summary><see cref="Dto.SqliteExecuteRequest"/> → <see cref="Dto.SqliteExecuteResultDto"/>.</summary>
        public const string SqliteExecute = "Sqlite.Execute";

        /// <summary><see cref="Dto.SqlInstanceRequest"/>: SecurityGuard SQL izlemesini (Audit) kurar → güncel liste.</summary>
        public const string SqlAuditInstall = "Sql.Audit.Install";

        /// <summary><see cref="Dto.SqlInstanceRequest"/>: SQL izlemesini kaldırır → güncel liste.</summary>
        public const string SqlAuditUninstall = "Sql.Audit.Uninstall";

        /// <summary>→ string: kurulum scripti (SSMS'te elle çalıştırmak için).</summary>
        public const string SqlAuditScript = "Sql.Audit.Script";

        // --- Taramalar (Performans / Güvenlik Önerileri)
        /// <summary><see cref="Dto.CheckScopeRequest"/> → <see cref="Dto.CheckRunDto"/> (son tarama + sürüyor mu).</summary>
        public const string ChecksLatest = "Checks.Latest";

        /// <summary><see cref="Dto.CheckScopeRequest"/>: taramayı arka planda başlatır (sürüyorsa yenisi başlamaz) → <see cref="Dto.CheckRunDto"/>.</summary>
        public const string ChecksRun = "Checks.Run";

        /// <summary><see cref="Dto.CheckIgnoreRequest"/>: maddeyi görmezden gel / geri al → <see cref="Dto.CheckRunDto"/>.</summary>
        public const string ChecksIgnore = "Checks.Ignore";

        // --- SQL Bakımı (varsayılan kapalı)
        /// <summary>→ <see cref="Dto.MaintenanceStateDto"/> (ayarlar + DB listesi + son çalıştırmalar).</summary>
        public const string MaintenanceGet = "Maintenance.Get";

        /// <summary><see cref="Dto.MaintenanceConfigDto"/> → <see cref="Dto.MaintenanceStateDto"/>.</summary>
        public const string MaintenanceSave = "Maintenance.Save";

        /// <summary><see cref="Dto.MaintenanceRunRequest"/>: şimdi çalıştır (deneme veya uygulama) → <see cref="Dto.MaintenanceStateDto"/>.</summary>
        public const string MaintenanceRun = "Maintenance.Run";

        /// <summary><see cref="Dto.RunIdRequest"/> → List&lt;<see cref="Dto.MaintenanceOpDto"/>&gt;.</summary>
        public const string MaintenanceOps = "Maintenance.Ops";

        // --- SQL Yedekleme (varsayılan kapalı)
        /// <summary>→ <see cref="Dto.BackupStateDto"/>.</summary>
        public const string BackupGet = "Backup.Get";

        /// <summary><see cref="Dto.BackupConfigDto"/> → <see cref="Dto.BackupStateDto"/>.</summary>
        public const string BackupSave = "Backup.Save";

        /// <summary>Seçili DB'leri şimdi yedekler → <see cref="Dto.BackupStateDto"/>.</summary>
        public const string BackupRun = "Backup.Run";

        /// <summary><see cref="Dto.RunIdRequest"/> → List&lt;<see cref="Dto.BackupFileDto"/>&gt;.</summary>
        public const string BackupFiles = "Backup.Files";

        /// <summary><see cref="Dto.BackupPasswordRequest"/>: arşiv şifresini belirler / üretir (yönetici şifresi ister) → string (yeni şifre).</summary>
        public const string BackupSetPassword = "Backup.SetPassword";

        /// <summary><see cref="Dto.PasswordConfirmRequest"/>: arşiv şifresini gösterir (yönetici şifresi ister) → string.</summary>
        public const string BackupGetPassword = "Backup.GetPassword";

        // --- Lisans (lisanssız da çalışır: arayüzden lisans durumu görülebilsin)
        /// <summary>→ <see cref="Dto.LicenseInfoDto"/>.</summary>
        public const string LicenseGet = "License.Get";

        /// <summary>Lisans sunucusuna şimdi sorar → <see cref="Dto.LicenseInfoDto"/>.</summary>
        public const string LicenseCheck = "License.Check";

        /// <summary><see cref="Dto.LicenseApiUrlRequest"/>: lisans sunucusu adresi (boş = varsayılan) → <see cref="Dto.LicenseInfoDto"/>.</summary>
        public const string LicenseSetApiUrl = "License.SetApiUrl";

        // --- Servis takibi
        /// <summary>→ <see cref="Dto.ServiceWatchStateDto"/> (tüm servisler + izlenme ayarı).</summary>
        public const string ServiceWatchGet = "ServiceWatch.Get";

        /// <summary><see cref="Dto.ServiceWatchSaveRequest"/> → <see cref="Dto.ServiceWatchStateDto"/>.</summary>
        public const string ServiceWatchSave = "ServiceWatch.Save";

        // --- RDP oturum geçmişi / mesai saatleri
        /// <summary><see cref="Dto.RdpSessionQuery"/> → <see cref="Dto.RdpSessionResult"/>.</summary>
        public const string RdpSessionsList = "RdpSessions.List";

        /// <summary>→ <see cref="Dto.WorkHoursDto"/> (kullanıcı istisnaları + genel mesai özeti).</summary>
        public const string WorkHoursGet = "WorkHours.Get";

        /// <summary><see cref="Dto.WorkHoursDto"/> (Exceptions) → <see cref="Dto.WorkHoursDto"/>.</summary>
        public const string WorkHoursSave = "WorkHours.Save";

        // --- Açık portlar
        /// <summary>Dinleyen portlar, firewall durumu, son 24 saat başarısız giriş → <see cref="Dto.OpenPortsDto"/>. Sadece okur.</summary>
        public const string OpenPortsGet = "OpenPorts.Get";

        // --- Tuzak dosyalar
        /// <summary>→ <see cref="Dto.CanaryStateDto"/>.</summary>
        public const string CanaryGet = "Canary.Get";

        /// <summary><see cref="Dto.CanaryFolderRequest"/> (Path): klasör ekle → <see cref="Dto.CanaryStateDto"/>.</summary>
        public const string CanaryAddFolder = "Canary.AddFolder";

        /// <summary><see cref="Dto.CanaryFolderRequest"/> (Id): elle eklenen klasörü kaldır (tuzak dosyaları silinir) → <see cref="Dto.CanaryStateDto"/>.</summary>
        public const string CanaryRemoveFolder = "Canary.RemoveFolder";

        /// <summary><see cref="Dto.CanaryFolderRequest"/> (Id, Enabled): klasörü aç / kapat → <see cref="Dto.CanaryStateDto"/>.</summary>
        public const string CanarySetEnabled = "Canary.SetEnabled";

        /// <summary>Şimdi kontrol et (paylaşımlar yeniden bulunur, dosyalar doğrulanır) → <see cref="Dto.CanaryStateDto"/>.</summary>
        public const string CanaryCheck = "Canary.Check";

        /// <summary>Bir tuzak dosyaya kendisi dokunur: alarm + mail zinciri denenir → string (açıklama).</summary>
        public const string CanaryTest = "Canary.Test";

        // --- Port IP kısıtları
        /// <summary>→ <see cref="Dto.PortRestrictionStateDto"/>.</summary>
        public const string PortRestrictGet = "PortRestrict.Get";

        /// <summary><see cref="Dto.PortRestrictionSaveRequest"/>: kaydetmeden kontrol → <see cref="Dto.PortRestrictionCheckDto"/>.</summary>
        public const string PortRestrictCheck = "PortRestrict.Check";

        /// <summary><see cref="Dto.PortRestrictionSaveRequest"/>: kaydet + uygula (onay bekler) → <see cref="Dto.IdRequest"/> (kaydın Id'si).</summary>
        public const string PortRestrictSave = "PortRestrict.Save";

        /// <summary><see cref="Dto.IdRequest"/>: uygulanan değişikliği onayla (onaylanmazsa süre sonunda geri alınır).</summary>
        public const string PortRestrictConfirm = "PortRestrict.Confirm";

        /// <summary><see cref="Dto.IdRequest"/>: onay bekleyen değişikliği hemen geri al (süreyi beklemeden).</summary>
        public const string PortRestrictRevert = "PortRestrict.Revert";

        /// <summary><see cref="Dto.IdRequest"/>: kısıtı kapat (firewall kuralı silinir, kayıt kalır).</summary>
        public const string PortRestrictDisable = "PortRestrict.Disable";

        /// <summary><see cref="Dto.IdRequest"/>: kısıtı sil.</summary>
        public const string PortRestrictDelete = "PortRestrict.Delete";

        // --- Günlük rapor
        /// <summary>→ List&lt;<see cref="Dto.ReportDto"/>&gt; (son raporlar, HTML'siz).</summary>
        public const string ReportList = "Report.List";

        /// <summary><see cref="Dto.IdRequest"/> → <see cref="Dto.ReportDto"/> (HTML ile).</summary>
        public const string ReportGet = "Report.Get";

        /// <summary>Şu anki durumla rapor üretir, kaydetmez / göndermez → <see cref="Dto.ReportDto"/>.</summary>
        public const string ReportPreview = "Report.Preview";

        /// <summary>Raporu şimdi üretir, kaydeder ve (mail ayarı ve kural açıksa) gönderir → <see cref="Dto.ReportDto"/>.</summary>
        public const string ReportSendNow = "Report.SendNow";

        /// <summary>Aylık rapor önizlemesi: <see cref="Dto.IdRequest"/> Id 0 = önceki ay, 1 = bu ay (şimdiye kadar) → <see cref="Dto.ReportDto"/>.</summary>
        public const string ReportMonthlyPreview = "Report.MonthlyPreview";

        /// <summary>Önceki ayın raporunu şimdi üretir, kaydeder, gönderir → <see cref="Dto.ReportDto"/>.</summary>
        public const string ReportMonthlySendNow = "Report.MonthlySendNow";

        // --- Servis yönetimi
        /// <summary>→ <see cref="Dto.ServiceInfoDto"/>.</summary>
        public const string ServiceInfo = "Service.Info";

        /// <summary><see cref="Dto.PasswordConfirmRequest"/>. Cevap gönderildikten sonra servis kendini durdurur.</summary>
        public const string ServiceStop = "Service.Stop";

        /// <summary><see cref="Dto.PasswordConfirmRequest"/>. Servis Windows kaydını siler ve durur (veritabanı ve loglar kalır).</summary>
        public const string ServiceUninstall = "Service.Uninstall";
    }
}
