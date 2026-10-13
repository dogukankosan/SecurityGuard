using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>SQL Bakımı ayarları. Varsayılan KAPALI; açılınca da önce deneme modunda.</summary>
    public sealed class MaintenanceConfigDto
    {
        public bool Enabled { get; set; }

        /// <summary>Deneme modu: hiçbir şey uygulanmaz, sadece ne yapılacağı kaydedilir.</summary>
        public bool DryRun { get; set; } = true;

        /// <summary>Bakım günleri (System.DayOfWeek: 0 = Pazar ... 6 = Cumartesi).</summary>
        public List<int> Days { get; set; } = new List<int> { 0 };

        /// <summary>Bakım penceresi başlangıcı "HH:mm".</summary>
        public string StartTime { get; set; } = "01:00";

        /// <summary>Bakım penceresi uzunluğu: süre dolunca yeni işleme başlanmaz.</summary>
        public int MaxMinutes { get; set; } = 180;

        public bool Indexes { get; set; } = true;
        public bool Statistics { get; set; } = true;

        /// <summary>Şişmiş log dosyasını küçült (veri dosyası asla küçültülmez).</summary>
        public bool ShrinkLog { get; set; }

        /// <summary>Seçili DB'ler: "INSTANCE|DB". Yeni oluşan DB kendiliğinden dahil olmaz.</summary>
        public List<string> Databases { get; set; } = new List<string>();
    }

    public sealed class MaintenanceDbDto
    {
        public string Instance { get; set; }
        public string Name { get; set; }
        public string RecoveryModel { get; set; }
        public double DataMb { get; set; }
        public double LogMb { get; set; }
        public bool Selected { get; set; }
    }

    /// <summary>Bir bakım çalıştırması (instance başına).</summary>
    public sealed class MaintenanceRunDto
    {
        public string RunId { get; set; }
        public string Instance { get; set; }
        public string Trigger { get; set; }
        public bool DryRun { get; set; }
        public string StartedUtc { get; set; }
        public string FinishedUtc { get; set; }

        /// <summary>"Sürüyor", "Tamamlandı", "Süre doldu", "Yarıda kesildi", "Hatalı".</summary>
        public string Status { get; set; }

        public string Summary { get; set; }
    }

    /// <summary>Bakımda yapılan (veya deneme modunda yapılacak) tek işlem.</summary>
    public sealed class MaintenanceOpDto
    {
        public string TimeUtc { get; set; }
        public string Database { get; set; }
        public string ObjectName { get; set; }

        /// <summary>"REBUILD", "REORGANIZE", "UPDATE STATISTICS", "LOG SHRINK", "BEKLEME" ...</summary>
        public string Operation { get; set; }

        public string Before { get; set; }
        public string After { get; set; }
        public long DurationMs { get; set; }

        /// <summary>"Yapıldı", "Yapılacak (deneme)", "Atlandı", "Hata", "İptal".</summary>
        public string Result { get; set; }

        public string Error { get; set; }
    }

    public sealed class MaintenanceStateDto
    {
        public MaintenanceConfigDto Config { get; set; }

        /// <summary>İzlenen instance'lardaki kullanıcı DB'leri (canlı okunur).</summary>
        public List<MaintenanceDbDto> Databases { get; set; }

        /// <summary>Instance bazında notlar: bağlanılamadı, edition (online rebuild var / yok), sunucudaki diğer bakım işleri.</summary>
        public List<string> Notes { get; set; }

        /// <summary>Sunucuda zaten bakım yapan SQL Agent işi / bakım planı var (aynı iş iki kez yapılmasın).</summary>
        public List<string> OtherJobs { get; set; }

        public bool IsRunning { get; set; }
        public string Progress { get; set; }

        /// <summary>Açıksa bir sonraki bakım penceresi (UTC).</summary>
        public string NextRunUtc { get; set; }

        /// <summary>Parçalanma eşikleri (Genel Ayarlar'dan; bilgi için).</summary>
        public string Thresholds { get; set; }

        public List<MaintenanceRunDto> Runs { get; set; }
    }

    public sealed class MaintenanceRunRequest
    {
        public bool DryRun { get; set; }
    }

    public sealed class RunIdRequest
    {
        public string RunId { get; set; }
    }
}
