namespace SecurityGuard.Shared.Dto
{
    /// <summary>Servis durumu (giriş gerektirmez; UI açılışta bununla ne göstereceğine karar verir).</summary>
    public sealed class StatusDto
    {
        public string ServiceVersion { get; set; }

        /// <summary>Veritabanı açıldı, servis istek işleyebilir.</summary>
        public bool Ready { get; set; }

        /// <summary>Yönetici hesabı oluşturulmuş mu (false ise UI ilk kurulum sihirbazını açar).</summary>
        public bool AdminExists { get; set; }

        public string MachineName { get; set; }

        /// <summary>Lisans geçerli (veya çevrimdışı tolerans içinde). false ise UI girişten sonra lisans ekranını açar.</summary>
        public bool Licensed { get; set; }

        /// <summary>Kayıtlı UI teması (giriş ekranından önce uygulanır). Kayıt yoksa null → varsayılan tema.</summary>
        public ThemeDto Theme { get; set; }
    }
}
