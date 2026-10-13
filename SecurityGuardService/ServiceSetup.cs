using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.ServiceProcess;
using SecurityGuard.Shared;

namespace SecurityGuardService
{
    /// <summary>
    /// Servisin Windows'a kurulumu / kaldırılması (komut satırı). Setup ve UI aynı kodu kullanır:
    ///   SecurityGuardService.exe --install [--start]   kur (varsa yolunu günceller), kurtarma ayarlarını yap, isteğe bağlı başlat
    ///   SecurityGuardService.exe --start               başlat
    ///   SecurityGuardService.exe --uninstall           durdur ve Windows kaydını sil (veritabanı ve loglar kalır)
    /// Yönetici olarak çalıştırılmalı. Çıkış kodu 0 = başarılı; hata mesajı stderr'e Türkçe yazılır.
    /// </summary>
    internal static class ServiceSetup
    {
        public const int ExitOk = 0;
        public const int ExitError = 1;
        public const int ExitNotElevated = 5;

        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

        public static bool IsSetupCommand(string[] args)
        {
            return args.Any(a => a == "--install" || a == "--uninstall" || a == "--start");
        }

        public static int Run(string[] args)
        {
            try
            {
                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    Console.Error.WriteLine("Bu işlem için yönetici yetkisi gerekiyor (yönetici olarak çalıştırın).");
                    return ExitNotElevated;
                }

                if (args.Contains("--uninstall"))
                {
                    Uninstall();
                    Console.WriteLine("SecurityGuard servisi kaldırıldı. Veritabanı ve loglar korunuyor.");
                    return ExitOk;
                }
                if (args.Contains("--install"))
                {
                    Install();
                    Console.WriteLine("SecurityGuard servisi kuruldu.");
                }
                if (args.Contains("--start"))
                {
                    Start();
                    Console.WriteLine("SecurityGuard servisi çalışıyor.");
                }
                return ExitOk;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitError;
            }
        }

        public static bool IsInstalled()
        {
            return ServiceController.GetServices().Any(s => string.Equals(s.ServiceName, GuardConstants.ServiceName, StringComparison.OrdinalIgnoreCase));
        }

        public static void Install()
        {
            string exe = Assembly.GetExecutingAssembly().Location;
            string binPath = "binPath= \"\\\"" + exe + "\\\"\"";

            if (IsInstalled())
                Sc("config " + GuardConstants.ServiceName + " " + binPath + " start= auto");
            else
                Sc("create " + GuardConstants.ServiceName + " " + binPath + " start= auto DisplayName= \"SecurityGuard\"");

            Sc("description " + GuardConstants.ServiceName + " \"SecurityGuard: RDP ve SQL Server saldırılarını izler, dış saldırganları banlar, kritik olaylarda mail atar.\"");
            // Çökerse 5 sn, 10 sn, 30 sn sonra yeniden başlat; sayaç 1 günde sıfırlanır
            Sc("failure " + GuardConstants.ServiceName + " reset= 86400 actions= restart/5000/restart/10000/restart/30000");
            // Hata koduyla durursa da (çökme olmadan) kurtarma devreye girsin
            Sc("failureflag " + GuardConstants.ServiceName + " 1");
        }

        public static void Start()
        {
            using (var sc = new ServiceController(GuardConstants.ServiceName))
            {
                if (sc.Status == ServiceControllerStatus.Running) return;
                if (sc.Status != ServiceControllerStatus.StartPending) sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, WaitTimeout);
            }
        }

        public static void Uninstall()
        {
            if (!IsInstalled()) return;
            using (var sc = new ServiceController(GuardConstants.ServiceName))
            {
                if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                    sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, WaitTimeout);
            }
            Sc("delete " + GuardConstants.ServiceName);
        }

        /// <summary>
        /// Servis kaydını silinmek üzere işaretler (servis kendi içinden çağırır; kayıt servis durunca silinir).
        /// </summary>
        public static void MarkForDeletion()
        {
            Sc("delete " + GuardConstants.ServiceName);
        }

        private static void Sc(string arguments)
        {
            var psi = new ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                if (!p.WaitForExit(30000)) throw new System.TimeoutException("sc.exe zamanında cevap vermedi: " + arguments);
                if (p.ExitCode != 0)
                    throw new InvalidOperationException("sc.exe " + arguments.Split(' ')[0] + " başarısız (kod " + p.ExitCode + "): " + output.Trim());
            }
        }
    }
}
