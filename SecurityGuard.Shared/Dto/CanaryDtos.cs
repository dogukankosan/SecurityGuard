using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>
    /// Tuzak dosyalar: paylaşılan klasörlere / yedek klasörüne konan gizli dosyalar. Yazılır, silinir veya adı değişirse
    /// (fidye yazılımı belirtisi) anında kritik mail gider.
    /// </summary>
    public sealed class CanaryStateDto
    {
        public List<CanaryFolderDto> Folders { get; set; } = new List<CanaryFolderDto>();

        /// <summary>Windows "Dosya sistemi" denetimi açık mı (kapalıysa mailde kullanıcı / program yazmaz).</summary>
        public bool FileAuditEnabled { get; set; }

        /// <summary>Denetimi açan komut (yönetici komut isteminde).</summary>
        public string AuditCommand { get; set; }

        /// <summary>Son tetiklenme (UTC metin, <see cref="UtcTime"/>); hiç olmadıysa null.</summary>
        public string LastIncidentUtc { get; set; }

        public string LastIncident { get; set; }

        /// <summary>Son kontrol (UTC metin).</summary>
        public string LastCheckUtc { get; set; }

        public string Message { get; set; }
    }

    public sealed class CanaryFolderDto
    {
        public long Id { get; set; }
        public string Path { get; set; }

        /// <summary>"Paylaşım: Muhasebe", "Yedek klasörü", "Elle eklendi".</summary>
        public string Source { get; set; }

        public bool Enabled { get; set; }

        /// <summary>Elle eklenen silinebilir; paylaşım / yedek klasörü sadece kapatılabilir.</summary>
        public bool CanRemove { get; set; }

        public string State { get; set; }

        public List<CanaryFileDto> Files { get; set; } = new List<CanaryFileDto>();
    }

    public sealed class CanaryFileDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string State { get; set; }
        public string CreatedUtc { get; set; }
    }

    public sealed class CanaryFolderRequest
    {
        public long Id { get; set; }
        public string Path { get; set; }
        public bool Enabled { get; set; }
    }
}
