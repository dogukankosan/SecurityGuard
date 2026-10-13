namespace SecurityGuard.Shared.Dto
{
    /// <summary>Günlük rapor (gönderilen veya önizleme).</summary>
    public sealed class ReportDto
    {
        /// <summary>"daily" veya "monthly".</summary>
        public string Kind { get; set; } = "daily";

        /// <summary>Kayıtlı raporda Id; önizlemede 0.</summary>
        public long Id { get; set; }

        public string CreatedUtc { get; set; }
        public string PeriodFromUtc { get; set; }
        public string PeriodToUtc { get; set; }
        public string Subject { get; set; }

        /// <summary>"zamanlanmış", "elle (kullanıcı)", "önizleme".</summary>
        public string Trigger { get; set; }

        /// <summary>Mail kuyruğa alındı mı (mail ayarı yoksa / kural kapalıysa hayır).</summary>
        public bool Mailed { get; set; }

        /// <summary>Neden gönderilmediği (gönderildiyse null).</summary>
        public string MailNote { get; set; }

        /// <summary>Listede null (sadece Report.Get / Preview / SendNow cevabında dolu).</summary>
        public string Html { get; set; }
    }

    public sealed class IdRequest
    {
        public long Id { get; set; }
    }
}
