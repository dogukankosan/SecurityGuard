using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Beyaz liste kaydı. Sistem kayıtları (iç ağ, localhost, sunucu IP'leri) otomatik eklenir, silinemez.</summary>
    public sealed class WhitelistEntry
    {
        public long Id { get; set; }

        /// <summary>Tek IP (1.2.3.4) veya aralık (1.2.3.0/24, IPv6 dahil).</summary>
        public string Cidr { get; set; }

        public string Note { get; set; }
        public bool IsSystem { get; set; }
        public string CreatedUtc { get; set; }
    }

    public sealed class WhitelistAddRequest
    {
        public string Cidr { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Birden fazla kayıt üzerinde işlem (toplu silme vb.).</summary>
    public sealed class IdListRequest
    {
        public List<long> Ids { get; set; }
    }
}
