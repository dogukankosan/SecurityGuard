using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Sunucudaki bir SQL Server instance'ı (otomatik tespit + kayıtlı bağlantı bilgisi).</summary>
    public sealed class SqlInstanceDto
    {
        /// <summary>Instance adı: varsayılan instance için "MSSQLSERVER".</summary>
        public string InstanceName { get; set; }

        /// <summary>Sunucuda kurulu olarak bulundu mu (false = kayıtlı ama artık yok).</summary>
        public bool Detected { get; set; }

        /// <summary>Windows servis durumu: Çalışıyor / Durmuş / Bilinmiyor.</summary>
        public string ServiceState { get; set; }

        public string ProductVersion { get; set; }
        public string Edition { get; set; }

        /// <summary>Registry'deki TCP portu (0 = bilinmiyor / dinamik).</summary>
        public int TcpPort { get; set; }

        /// <summary>Bağlantı bilgisi kaydedilmiş mi.</summary>
        public bool IsConfigured { get; set; }

        /// <summary>İzleme bu instance için açık mı.</summary>
        public bool IsEnabled { get; set; }

        /// <summary>SecurityGuard SQL izlemesi (Audit) kullanıcı tarafından kuruldu mu.</summary>
        public bool AuditInstalled { get; set; }

        /// <summary>Audit şu an çalışıyor mu (son kontrol).</summary>
        public bool AuditRunning { get; set; }

        /// <summary>Audit durum metni (Çalışıyor / Kurulu değil / Audit durdurulmuş...).</summary>
        public string AuditStatus { get; set; }

        // Kayıtlı bağlantı bilgisi (şifre asla dönmez)
        public string Server { get; set; }
        public int Port { get; set; }
        public string Login { get; set; }
        public bool HasPassword { get; set; }

        /// <summary>Kayıtlı değilse önerilen sunucu adı (localhost / localhost\INSTANCE).</summary>
        public string SuggestedServer { get; set; }
    }

    /// <summary>Test ve kayıt isteği. Şifre boşsa kayıtlı şifre kullanılır.</summary>
    public sealed class SqlConnectionRequest
    {
        public string InstanceName { get; set; }
        public string Server { get; set; }

        /// <summary>0 = sunucu adındaki / varsayılan port.</summary>
        public int Port { get; set; }

        public string Login { get; set; }
        public string Password { get; set; }
        public bool IsEnabled { get; set; }
    }

    /// <summary>Bağlantı testinin sonucu.</summary>
    public sealed class SqlTestResultDto
    {
        public string ServerName { get; set; }
        public string ProductVersion { get; set; }
        public string ProductLevel { get; set; }
        public string Edition { get; set; }

        /// <summary>Ana sürüm: 11 = 2012, 12 = 2014, 13 = 2016, 14 = 2017, 15 = 2019, 16 = 2022, 17 = 2025.</summary>
        public int MajorVersion { get; set; }

        /// <summary>"SQL Server 2022" gibi okunur ad.</summary>
        public string VersionName { get; set; }

        public bool IsSysadmin { get; set; }

        /// <summary>Uyarılar (tam yetkili değil, desteklenmeyen sürüm...). Boşsa her şey yolunda.</summary>
        public List<string> Warnings { get; set; }
    }

    /// <summary>Tek instance üzerinde işlem (izleme kur / kaldır).</summary>
    public sealed class SqlInstanceRequest
    {
        public string InstanceName { get; set; }
    }
}
