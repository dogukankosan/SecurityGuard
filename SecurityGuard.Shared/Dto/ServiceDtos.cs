namespace SecurityGuard.Shared.Dto
{
    /// <summary>Servis Yönetimi ekranındaki bilgiler.</summary>
    public sealed class ServiceInfoDto
    {
        public string Version { get; set; }
        public string StartedUtc { get; set; }
        public int ProcessId { get; set; }

        /// <summary>Windows servisi olarak mı çalışıyor (false = geliştirme / konsol modu).</summary>
        public bool IsWindowsService { get; set; }

        public string DatabaseFile { get; set; }
        public long DatabaseSizeBytes { get; set; }
        public int SchemaVersion { get; set; }
        public string LogFolder { get; set; }

        /// <summary>Gönderilmeyi bekleyen mail sayısı.</summary>
        public int MailPending { get; set; }

        /// <summary>Servis / klasör koruması durumu.</summary>
        public string Protection { get; set; }
    }

    /// <summary>Kritik işlemlerde (servisi durdurma, kaldırma) yönetici şifresinin tekrar girilmesi.</summary>
    public sealed class PasswordConfirmRequest
    {
        public string Password { get; set; }
    }
}
