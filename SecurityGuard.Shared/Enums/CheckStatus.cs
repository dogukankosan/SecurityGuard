namespace SecurityGuard.Shared.Enums
{
    /// <summary>Tarama maddesinin sonucu. Değerler DB'de sayı olarak saklanır; değiştirilmemeli.</summary>
    public enum CheckStatus
    {
        Gecti = 0,

        /// <summary>Durum bildirimi / liste (ör. en büyük tablolar); sorun değil, bilgi.</summary>
        Bilgi = 1,

        Uyari = 2,
        Kritik = 3,

        /// <summary>Kontrol yapılamadı (yetki, bağlantı, sürüm desteklemiyor...).</summary>
        Hata = 4
    }
}
