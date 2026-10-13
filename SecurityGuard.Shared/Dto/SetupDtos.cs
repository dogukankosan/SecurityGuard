using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>İlk kurulum sihirbazının durumu: hangi zorunlu adım tamam, hangisi eksik.</summary>
    public sealed class SetupStatusDto
    {
        /// <summary>Sihirbaz bitirildi (bir daha gösterilmez).</summary>
        public bool Completed { get; set; }

        /// <summary>Sunucuda SQL Server bulundu (yoksa SQL adımı kendiliğinden geçilir).</summary>
        public bool SqlDetected { get; set; }

        /// <summary>En az bir instance'ın bağlantısı kayıtlı ve izleme açık.</summary>
        public bool SqlConnected { get; set; }

        /// <summary>Bağlantısı kayıtlı instance'ların hepsinde SQL izlemesi (Audit) kurulu.</summary>
        public bool SqlAuditInstalled { get; set; }

        /// <summary>Bağlantısı kurulmamış diğer instance'lar (bilgi).</summary>
        public List<string> SqlMissingInstances { get; set; }

        /// <summary>SMTP ayarı kayıtlı (kayıt için test maili şart).</summary>
        public bool MailConfigured { get; set; }

        public int ActiveRecipients { get; set; }

        /// <summary>Kullanıcının eklediği beyaz liste kayıtları (sistem kayıtları hariç).</summary>
        public int WhitelistEntries { get; set; }

        /// <summary>Otomatik korunan adresler (sunucunun kendi IP'leri, iç ağ...): bilgi için.</summary>
        public List<string> ProtectedAutomatically { get; set; }

        public bool SqlStepDone { get { return !SqlDetected || (SqlConnected && SqlAuditInstalled); } }
        public bool MailStepDone { get { return MailConfigured && ActiveRecipients > 0; } }
    }
}
