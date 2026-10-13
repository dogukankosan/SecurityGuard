using System;
using System.IO;
using SecurityGuard.Shared;

namespace SecurityGuardService.Core
{
    /// <summary>
    /// Servisin kullandığı klasörler. Her şey programın kendi klasöründe durur,
    /// ProgramData / Temp gibi dış yerlere hiçbir şey yazılmaz.
    ///
    ///   SecurityGuard\            (kök: Service\ klasörünün bir üstü)
    ///   ├─ Service\               servis exe
    ///   ├─ UI\                    arayüz exe
    ///   ├─ Database\guard.db      veritabanı
    ///   ├─ Database\Backup\       DB'nin kendi yedekleri
    ///   ├─ Logs\                  uygulama logu
    ///   └─ Temp\                  geçici dosyalar (iş bitince silinir; açılışta kalanlar temizlenir)
    /// </summary>
    internal static class AppPaths
    {
        public static readonly string RootFolder =
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));

        public static readonly string DatabaseFolder = Path.Combine(RootFolder, GuardConstants.DatabaseFolderName);

        public static readonly string DatabaseFile = Path.Combine(DatabaseFolder, GuardConstants.DatabaseFileName);

        public static readonly string BackupFolder = Path.Combine(DatabaseFolder, "Backup");

        public static readonly string LogFolder = Path.Combine(RootFolder, "Logs");

        /// <summary>Geçici dosyalar (Windows'un %TEMP% / AppData klasörü kullanılmaz). Test aracı da buraya yazar: Temp\SgAutoTest.</summary>
        public static readonly string TempFolder = Path.Combine(RootFolder, "Temp");

        /// <summary>Temp klasöründe benzersiz adlı geçici dosya yolu (klasör yoksa oluşturur; dosyayı çağıran siler).</summary>
        public static string TempFile(string prefix, string extension)
        {
            Directory.CreateDirectory(TempFolder);
            return Path.Combine(TempFolder, prefix + Guid.NewGuid().ToString("N") + extension);
        }

        /// <summary>Klasörler yoksa oluşturur. Zaten varsa bir şey yapmaz.</summary>
        public static void EnsureCreated()
        {
            Directory.CreateDirectory(DatabaseFolder);
            Directory.CreateDirectory(BackupFolder);
            Directory.CreateDirectory(LogFolder);
            Directory.CreateDirectory(TempFolder);
            CleanTemp();
        }

        /// <summary>Önceki çalışmadan kalmış (servis kapanırken silinememiş) geçici dosyaları siler. Alt klasörlere dokunmaz.</summary>
        private static void CleanTemp()
        {
            try
            {
                foreach (string file in Directory.GetFiles(TempFolder, "sg_*"))
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-1)) File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
