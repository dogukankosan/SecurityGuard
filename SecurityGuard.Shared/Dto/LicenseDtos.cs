namespace SecurityGuard.Shared.Dto
{
    /// <summary>Sistem → Lisans sayfası.</summary>
    public sealed class LicenseInfoDto
    {
        /// <summary>Bu makinenin donanım kimliği (lisans bu değerle oluşturulur).</summary>
        public string HwId { get; set; }

        /// <summary>Koruma çalışıyor mu (geçerli lisans veya çevrimdışı tolerans içinde).</summary>
        public bool Active { get; set; }

        /// <summary>"Geçerli", "Süresi doldu", "İptal edildi", "Bu makine için lisans yok", "Sunucuya ulaşılamıyor"...</summary>
        public string StatusText { get; set; }

        /// <summary>Kullanıcıya açıklama / ne yapması gerektiği.</summary>
        public string Detail { get; set; }

        public string Customer { get; set; }

        /// <summary>Yerel saat, "dd.MM.yyyy". Lisans yoksa boş.</summary>
        public string Expires { get; set; }

        /// <summary>Bitişe kalan gün (lisans yoksa / bittiyse 0).</summary>
        public int DaysLeft { get; set; }

        /// <summary>Lisans sunucusuyla son başarılı görüşme (yerel saat).</summary>
        public string LastCheck { get; set; }

        /// <summary>Son denemede hata olduysa nedeni.</summary>
        public string LastError { get; set; }

        /// <summary>Sunucuya ulaşılamıyorsa çevrimdışı çalışmanın biteceği zaman (yerel saat); yoksa boş.</summary>
        public string OfflineUntil { get; set; }

        /// <summary>Donanım değişmiş (4 parçanın 3'ü eşleşti): lisans geçerli ama bilgi verilir.</summary>
        public bool HardwareChanged { get; set; }

        public string ApiUrl { get; set; }
    }

    public sealed class LicenseApiUrlRequest
    {
        public string Url { get; set; }
    }
}
