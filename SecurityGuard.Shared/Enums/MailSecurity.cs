namespace SecurityGuard.Shared.Enums
{
    /// <summary>SMTP bağlantı güvenliği. Değerler DB'de sayı olarak saklanır; değiştirilmemeli.</summary>
    public enum MailSecurity
    {
        /// <summary>Porta göre otomatik (465 → SSL, diğerleri → sunucu destekliyorsa STARTTLS).</summary>
        Auto = 0,

        /// <summary>Bağlanır bağlanmaz SSL/TLS (genelde port 465).</summary>
        SslOnConnect = 1,

        /// <summary>Düz bağlanıp STARTTLS ile şifreli (genelde port 587).</summary>
        StartTls = 2,

        /// <summary>Şifresiz (önerilmez, sadece iç ağdaki mail sunucuları için).</summary>
        None = 3
    }
}
