using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Bir RDP / konsol oturumu (oturum açma → kapanma). Zamanlar UTC metin (<see cref="UtcTime"/>).</summary>
    public sealed class RdpSessionDto
    {
        public long Id { get; set; }
        public string User { get; set; }

        /// <summary>İlk bağlanılan IP ("konsol" = sunucunun başından).</summary>
        public string ClientIp { get; set; }

        /// <summary>Oturum boyunca bağlanılan tüm IP'ler (yeniden bağlanmalar dahil).</summary>
        public string Ips { get; set; }

        public string Country { get; set; }
        public string StartUtc { get; set; }
        public string EndUtc { get; set; }

        /// <summary>"Açık", "Bağlantı kopuk", "Kapandı", "Kapandı (bitiş bilinmiyor)".</summary>
        public string State { get; set; }

        public int Disconnects { get; set; }
        public int Reconnects { get; set; }

        /// <summary>Süre (saniye); açık oturumda şu ana kadar.</summary>
        public long DurationSeconds { get; set; }

        /// <summary>Açılış veya yeniden bağlanma mesai dışındaydı.</summary>
        public bool OffHours { get; set; }

        /// <summary>Başlangıç bilinmiyor (servis oturum sürerken kuruldu / başladı): süre eksik olabilir.</summary>
        public bool StartUnknown { get; set; }
    }

    public sealed class RdpSessionQuery
    {
        public string FromUtc { get; set; }
        public string ToUtc { get; set; }
        public string User { get; set; }
        public bool OnlyOffHours { get; set; }
        public bool IncludeConsole { get; set; } = true;
    }

    public sealed class RdpSessionResult
    {
        public List<RdpSessionDto> Rows { get; set; } = new List<RdpSessionDto>();
        public bool Truncated { get; set; }

        /// <summary>Genel mesai tanımı (Genel Ayarlar'dan), örn. "Hafta içi 08:00–19:00".</summary>
        public string WorkHours { get; set; }
    }

    /// <summary>Mesai dışı uyarısı için kullanıcı istisnası.</summary>
    public sealed class WorkHoursExceptionDto
    {
        /// <summary>"kullanıcı" veya "ETKİALANI\kullanıcı".</summary>
        public string User { get; set; }

        /// <summary>"always" = her zaman serbest (uyarı yok), "custom" = kendi saatleri.</summary>
        public string Mode { get; set; } = "always";

        /// <summary>Günler (System.DayOfWeek: 0 = Pazar ... 6 = Cumartesi), custom için.</summary>
        public List<int> Days { get; set; } = new List<int> { 1, 2, 3, 4, 5 };

        public string Start { get; set; } = "08:00";
        public string End { get; set; } = "19:00";
        public string Note { get; set; }
    }

    public sealed class WorkHoursDto
    {
        public List<WorkHoursExceptionDto> Exceptions { get; set; } = new List<WorkHoursExceptionDto>();

        /// <summary>Sadece servisten: genel mesai özeti ve uyarı açık mı.</summary>
        public string General { get; set; }
        public bool AlarmEnabled { get; set; }

        /// <summary>Sadece servisten: oturum geçmişinde görülen kullanıcılar (seçim için).</summary>
        public List<string> KnownUsers { get; set; } = new List<string>();
    }
}
