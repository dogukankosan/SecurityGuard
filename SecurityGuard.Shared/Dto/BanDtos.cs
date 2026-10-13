using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Banlı IP (firewall'da tüm portlardan engelli).</summary>
    public sealed class BannedIpDto
    {
        public string IpAddress { get; set; }

        /// <summary>"RDP", "SQL" veya elle banda boş.</summary>
        public string Protocol { get; set; }

        public string Reason { get; set; }
        public int FailCount { get; set; }
        public bool IsManual { get; set; }

        /// <summary>Hangi firewall kuralında (SecurityGuard-Block-001...).</summary>
        public string RuleName { get; set; }

        public string BannedUtc { get; set; }
    }

    /// <summary>IP başına giriş denemesi sayacı (kümülatif, başarılı giriş sıfırlamaz).</summary>
    public sealed class LoginAttemptDto
    {
        public string IpAddress { get; set; }
        public string Protocol { get; set; }
        public int FailCount { get; set; }
        public int SuccessCount { get; set; }
        public string LastUserName { get; set; }
        public string FirstUtc { get; set; }
        public string LastUtc { get; set; }

        /// <summary>Ban eşiği (kuraldan).</summary>
        public int Threshold { get; set; }

        public bool IsBanned { get; set; }

        /// <summary>İç ağ / localhost / sunucu IP'si / beyaz liste: asla banlanmaz.</summary>
        public bool IsProtected { get; set; }
    }

    public sealed class BanListResult
    {
        public List<BannedIpDto> Banned { get; set; }
        public List<LoginAttemptDto> Attempts { get; set; }

        /// <summary>Firewall kuralları DB ile uyumlu mu (son kontrol).</summary>
        public bool FirewallOk { get; set; }
        public string FirewallMessage { get; set; }
    }

    public sealed class BanAddRequest
    {
        public string IpAddress { get; set; }
        public string Reason { get; set; }
    }

    public sealed class IpListRequest
    {
        public List<string> IpAddresses { get; set; }

        /// <summary>Ban kaldırılırken başarısız deneme sayacı da sıfırlansın (yoksa bir sonraki denemede tekrar banlanır).</summary>
        public bool ResetCounter { get; set; }
    }
}
