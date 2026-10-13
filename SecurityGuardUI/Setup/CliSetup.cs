using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Forms;
using SecurityGuardUI.Helpers;
using SecurityGuardUI.Ipc;

namespace SecurityGuardUI.Setup
{
    /// <summary>
    /// Kurulum paketinin (Inno Setup) çağırdığı komut satırı modu.
    ///   SecurityGuardUI.exe --uninstall [--delete-data]
    /// Denetim Masası'ndan kaldırmada çalışır: SG yönetici girişi + şifre onayı ister, servise KİMLİKLİ
    /// kaldırma komutu (Service.Uninstall) yollar — servis kendini siler, bu yüzden koruma engel olmaz.
    /// Böylece rastgele bir Windows yöneticisi SG şifresi olmadan sessizce kaldıramaz.
    /// Güncellemede ise (çalışan exe kilitli olduğundan) önce kimlikli durdurma gerekir:
    ///   SecurityGuardUI.exe --stop        SG girişi + şifre ister, servisi durdurur (kaldırmaz). Güncelleme öncesi.
    /// Çıkış kodu: 0 = tamam / yapılacak bir şey yok; 2 = vazgeçildi / şifre yanlış (işlem durmalı).
    /// </summary>
    internal static class CliSetup
    {
        public const int Ok = 0;
        public const int Cancelled = 2;

        public static bool IsCliCommand(string[] args)
        {
            return args != null && args.Any(a =>
                string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--stop", StringComparison.OrdinalIgnoreCase));
        }

        public static int Run(string[] args)
        {
            if (args.Any(a => string.Equals(a, "--stop", StringComparison.OrdinalIgnoreCase)))
                return Stop();
            bool deleteData = args.Any(a => string.Equals(a, "--delete-data", StringComparison.OrdinalIgnoreCase));
            return Uninstall(deleteData);
        }

        /// <summary>Güncelleme öncesi: korumalı servisi kimlikli olarak durdurur (kaldırmaz).</summary>
        private static int Stop()
        {
            if (ServiceControl.GetState() != ServiceState.Running)
                return Ok; // kurulu değil / zaten durmuş: yapılacak bir şey yok

            using (var client = new GuardClient())
            {
                var status = client.GetStatusAsync().Result;
                if (!status.Success) return Ok; // servise ulaşılamıyor; kurulum devam etsin

                using (var login = new LoginForm(client, status.Value.MachineName,
                    "SecurityGuard'ı güncellemek için önce servis durdurulacak. Yönetici girişi yapın."))
                {
                    if (login.ShowDialog() != DialogResult.OK) return Cancelled;
                }

                string password;
                using (var confirm = new PasswordConfirmForm("SecurityGuard güncellenecek.",
                    "Güncelleme için servis geçici olarak durdurulur; işlem sonunda tekrar başlatılır."))
                {
                    if (confirm.ShowDialog() != DialogResult.OK) return Cancelled;
                    password = confirm.Password;
                }

                var res = client.SendAsync(Commands.ServiceStop, new PasswordConfirmRequest { Password = password }).Result;
                if (!res.Success)
                {
                    Ui.Error(null, "Servis durdurulamadı: " + res.ErrorMessage);
                    return Cancelled;
                }
                ServiceControl.WaitForStopped(TimeSpan.FromSeconds(60));
            }
            return Ok;
        }

        private static int Uninstall(bool deleteData)
        {
            // Servis Windows'a kurulu değilse kaldırılacak bir şey yok; dosya temizliğini kurulum paketi yapar.
            if (ServiceControl.GetState() == ServiceState.NotInstalled)
            {
                if (deleteData) DeleteData();
                return Ok;
            }

            using (var client = new GuardClient())
            {
                var status = client.GetStatusAsync().Result;
                if (!status.Success)
                {
                    Ui.Error(null, "SecurityGuard servisine bağlanılamadı. Kaldırmak için önce servisi başlatın "
                                   + "(Hizmetler → SecurityGuard → Başlat), sonra tekrar deneyin." + Environment.NewLine + Environment.NewLine
                                   + status.ErrorMessage);
                    return Cancelled;
                }

                // Yönetici girişi: kaldırma yetkisi SG yöneticisine aittir (Windows admini tek başına yetmez)
                using (var login = new LoginForm(client, status.Value.MachineName,
                    "SecurityGuard'ı kaldırmak için yönetici girişi yapın."))
                {
                    if (login.ShowDialog() != DialogResult.OK) return Cancelled;
                }

                // Şifre onayı (uygulama içindeki "Kaldır" düğmesiyle aynı; ek güvenlik)
                string password;
                using (var confirm = new PasswordConfirmForm("SecurityGuard kaldırılacak.",
                    deleteData
                        ? "Servis Windows'tan silinir VE veritabanı + loglar silinir (geri alınamaz)."
                        : "Servis Windows'tan silinir. Veritabanı ve loglar KORUNUR (tekrar kurulduğunda kullanılabilir)."))
                {
                    if (confirm.ShowDialog() != DialogResult.OK) return Cancelled;
                    password = confirm.Password;
                }

                var res = client.SendAsync(Commands.ServiceUninstall, new PasswordConfirmRequest { Password = password }).Result;
                if (!res.Success)
                {
                    Ui.Error(null, "Kaldırma başarısız: " + res.ErrorMessage);
                    return Cancelled;
                }

                ServiceControl.WaitForStopped(TimeSpan.FromSeconds(60));
            }

            if (deleteData) DeleteData();
            return Ok;
        }

        /// <summary>Servis durduktan sonra korumalı veri klasörlerini (SYSTEM sahipli) sahiplenip siler.</summary>
        private static void DeleteData()
        {
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            foreach (var name in new[] { "Database", "Logs", "Temp" })
            {
                string path = Path.Combine(root, name);
                if (!Directory.Exists(path)) continue;
                try
                {
                    Run("takeown.exe", "/f \"" + path + "\" /r /d y");
                    // *S-1-5-32-544 = Administrators (dil bağımsız)
                    Run("icacls.exe", "\"" + path + "\" /grant *S-1-5-32-544:(OI)(CI)F /t /c");
                    Directory.Delete(path, true);
                }
                catch
                {
                    // Silinemeyen kalırsa kurulum paketi devam eder; kullanıcı elle silebilir.
                }
            }
        }

        private static void Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
            }
        }
    }
}
