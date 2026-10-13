using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Sunucudaki bir Windows servisi ve izlenme ayarı (İzleme → Servis Takibi).</summary>
    public sealed class WatchedServiceDto
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }

        /// <summary>"Çalışıyor", "Durdu", "Başlatılıyor"...</summary>
        public string Status { get; set; }

        /// <summary>"Otomatik", "Elle", "Devre dışı"...</summary>
        public string StartType { get; set; }

        public bool Watched { get; set; }

        /// <summary>Durursa SecurityGuard yeniden başlatsın (varsayılan kapalı).</summary>
        public bool AutoRestart { get; set; }

        /// <summary>İzlenmesi önerilen servis (SQL, LOGO, IIS, Firewall, Defender, Olay Günlüğü, RDP).</summary>
        public bool Recommended { get; set; }

        /// <summary>Son durma / düzelme bilgisi (izlenenler için).</summary>
        public string LastEvent { get; set; }
    }

    public sealed class ServiceWatchStateDto
    {
        public List<WatchedServiceDto> Services { get; set; } = new List<WatchedServiceDto>();
    }

    public sealed class ServiceWatchSaveRequest
    {
        /// <summary>Sadece Name, Watched, AutoRestart kullanılır.</summary>
        public List<WatchedServiceDto> Items { get; set; } = new List<WatchedServiceDto>();
    }
}
