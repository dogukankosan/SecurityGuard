namespace SecurityGuard.Shared.Contracts
{
    /// <summary>Pipe cevaplarındaki hata kodları. UI bunlara göre davranır (ör. Unauthorized → giriş ekranı).</summary>
    public static class ErrorCodes
    {
        /// <summary>Servise bağlanılamadı (UI tarafında üretilir).</summary>
        public const string ServiceUnavailable = "ServiceUnavailable";

        /// <summary>Bağlanılan pipe bizim servisimiz değil (UI tarafında üretilir).</summary>
        public const string ServiceNotTrusted = "ServiceNotTrusted";

        /// <summary>Servis açılıyor, veritabanı henüz hazır değil.</summary>
        public const string NotReady = "NotReady";

        /// <summary>Oturum yok veya süresi doldu.</summary>
        public const string Unauthorized = "Unauthorized";

        public const string UnknownCommand = "UnknownCommand";
        public const string InvalidRequest = "InvalidRequest";

        /// <summary>Girilen bilgi kurala uymuyor (mesajda nedeni yazar).</summary>
        public const string Validation = "Validation";

        public const string NoAdmin = "NoAdmin";
        public const string AdminExists = "AdminExists";
        public const string LoginFailed = "LoginFailed";
        public const string LockedOut = "LockedOut";

        /// <summary>Mail gönderilemedi (mesajda sebep: bağlantı, kimlik doğrulama, TLS...).</summary>
        public const string MailFailed = "MailFailed";

        /// <summary>SQL Server'a bağlanılamadı (mesajda sebep).</summary>
        public const string SqlFailed = "SqlFailed";

        /// <summary>Lisans geçerli değil: koruma durdu, sadece giriş / lisans / servis sayfaları çalışır (UI lisans sayfasını açar).</summary>
        public const string Unlicensed = "Unlicensed";

        /// <summary>Beklenmeyen hata (detayı servis logunda).</summary>
        public const string Internal = "Internal";
    }
}
