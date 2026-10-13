using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>SQL Yedekleme ayarları (varsayılan kapalı). Arşiv şifresi ayrı ve şifreli saklanır, burada yoktur.</summary>
    public sealed class BackupConfigDto
    {
        public bool Enabled { get; set; }

        /// <summary>Yedek günleri (System.DayOfWeek: 0 = Pazar ... 6 = Cumartesi). Varsayılan her gün.</summary>
        public List<int> Days { get; set; } = new List<int> { 0, 1, 2, 3, 4, 5, 6 };

        /// <summary>Başlangıç saati "HH:mm".</summary>
        public string StartTime { get; set; } = "22:00";

        /// <summary>Seçili DB'ler: "INSTANCE|DB". Yeni DB kendiliğinden eklenmez.</summary>
        public List<string> Databases { get; set; } = new List<string>();

        /// <summary>Arşivlerin yazılacağı yerel klasör (altında instance klasörleri).</summary>
        public string TargetFolder { get; set; }

        /// <summary>Bu günden eski arşivler silinir (her DB'nin son 3 başarılı yedeği her durumda kalır).</summary>
        public int RetentionDays { get; set; } = 14;

        /// <summary>"auto" (sunucuda başka yedek varsa COPY_ONLY), "always", "never".</summary>
        public string CopyOnlyMode { get; set; } = "auto";

        /// <summary>Rar.exe yolu; boşsa otomatik bulunur.</summary>
        public string RarPath { get; set; }
    }

    public sealed class BackupDbDto
    {
        public string Instance { get; set; }
        public string Name { get; set; }
        public string RecoveryModel { get; set; }
        public double DataMb { get; set; }
        public bool Selected { get; set; }

        /// <summary>SQL Server'ın kaydına göre son tam yedek (herhangi bir araçla, UTC).</summary>
        public string LastFullBackupUtc { get; set; }

        /// <summary>SecurityGuard'ın son başarılı yedeği (UTC).</summary>
        public string LastGuardBackupUtc { get; set; }
    }

    public sealed class BackupRunDto
    {
        public string RunId { get; set; }
        public string Instance { get; set; }
        public string Trigger { get; set; }
        public string StartedUtc { get; set; }
        public string FinishedUtc { get; set; }

        /// <summary>"Sürüyor", "Tamamlandı", "Hatalı", "Yarıda kesildi".</summary>
        public string Status { get; set; }

        public string Summary { get; set; }
    }

    public sealed class BackupFileDto
    {
        public string TimeUtc { get; set; }
        public string Database { get; set; }

        /// <summary>"YEDEK", "DOĞRULAMA", "ARŞİV", "TEMİZLİK"...</summary>
        public string Step { get; set; }

        public string ArchivePath { get; set; }
        public double SizeMb { get; set; }
        public long DurationMs { get; set; }

        /// <summary>"Tamam", "Hata", "Atlandı", "Silindi".</summary>
        public string Result { get; set; }

        public string Note { get; set; }
    }

    public sealed class BackupStateDto
    {
        public BackupConfigDto Config { get; set; }
        public List<BackupDbDto> Databases { get; set; }

        /// <summary>Bulunan Rar.exe (yoksa null: yedekleme yapılamaz).</summary>
        public string RarFound { get; set; }

        /// <summary>WinRAR durumu (bulunamadı / lisanssız görünüyor...).</summary>
        public string RarNote { get; set; }

        public bool HasPassword { get; set; }

        /// <summary>Instance bazında notlar: geçici yedek klasörü ve boş alanı, sıkıştırma, başka yedekleme aracı.</summary>
        public List<string> Notes { get; set; }

        public bool IsRunning { get; set; }
        public string Progress { get; set; }
        public string NextRunUtc { get; set; }

        /// <summary>Hedef klasörde boş alan / toplam arşiv boyutu.</summary>
        public string TargetInfo { get; set; }

        public List<BackupRunDto> Runs { get; set; }
    }

    public sealed class BackupPasswordRequest
    {
        /// <summary>Yeni arşiv şifresi (en az 12 karakter). Boşsa güçlü bir şifre üretilir.</summary>
        public string NewPassword { get; set; }

        /// <summary>Onay için yönetici şifresi.</summary>
        public string AdminPassword { get; set; }
    }
}
