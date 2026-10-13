namespace SecurityGuard.Shared.Enums
{
    /// <summary>Güvenlik olayının önem derecesi. Değerler DB'de sayı olarak saklanır; değiştirilmemeli.</summary>
    public enum Severity
    {
        Bilgi = 0,
        Uyari = 1,
        Kritik = 2
    }
}
