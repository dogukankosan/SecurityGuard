using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Sunucuda dinleyen bir port ve dışarıya açıklığı (Koruma → Açık Portlar). Sadece okunur, hiçbir şey değiştirilmez.</summary>
    public sealed class OpenPortDto
    {
        /// <summary>"TCP" / "UDP".</summary>
        public string Protocol { get; set; }
        public int Port { get; set; }

        /// <summary>Bilinen kullanım: "RDP", "SQL Server", "SMB (dosya paylaşımı)"...; bilinmiyorsa boş.</summary>
        public string Label { get; set; }

        /// <summary>Dinlediği adresler ("tüm adresler", "127.0.0.1"...).</summary>
        public string Addresses { get; set; }

        public string Process { get; set; }
        public string ProcessPath { get; set; }
        public string Services { get; set; }

        /// <summary>"Herkese açık", "Kısıtlı", "SecurityGuard kısıtı", "Kapalı", "Sadece bu sunucu".</summary>
        public string Exposure { get; set; }

        /// <summary>Açıklık ayrıntısı (hangi firewall kuralı, hangi adresler).</summary>
        public string ExposureDetail { get; set; }

        /// <summary>"Yüksek", "Orta", "Düşük" veya boş.</summary>
        public string Risk { get; set; }

        /// <summary>Son 24 saatte bu porta ait protokolde başarısız giriş sayısı (bilinen protokoller için; yoksa -1).</summary>
        public int Failures24h { get; set; }

        public string Advice { get; set; }

        /// <summary>Port kısıtı eklenebilir mi (yerel / zaten kısıtlı değil).</summary>
        public bool CanRestrict { get; set; }
    }

    public sealed class OpenPortsDto
    {
        public List<OpenPortDto> Ports { get; set; } = new List<OpenPortDto>();

        /// <summary>Etkin profilde firewall kapalıysa uyarı.</summary>
        public string FirewallWarning { get; set; }

        /// <summary>İzlenen IIS logları ("FTP: Default", "Web: OWA"...).</summary>
        public List<string> WatchedLogs { get; set; } = new List<string>();

        public string GeneratedUtc { get; set; }
    }
}
