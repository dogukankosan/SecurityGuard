using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using SecurityGuard.Shared;

namespace SecurityGuardUI.Helpers
{
    internal enum ServiceState
    {
        NotInstalled,
        Stopped,
        Starting,
        Running,
        Stopping,
        Unknown
    }

    /// <summary>
    /// Windows servis yöneticisi üzerinden SecurityGuard servisinin durumu, başlatma ve kurma.
    /// Kurma, servis exe'sinin kendi komutuyla yapılır (SecurityGuardService.exe --install --start):
    /// setup ile aynı kod çalışır. UI yönetici olarak açıldığı için bu işlemler yapılabilir.
    /// </summary>
    internal static class ServiceControl
    {
        public static readonly string ServiceExe =
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Service", "SecurityGuardService.exe"));

        public static ServiceState GetState()
        {
            try
            {
                if (!ServiceController.GetServices().Any(s => string.Equals(s.ServiceName, GuardConstants.ServiceName, StringComparison.OrdinalIgnoreCase)))
                    return ServiceState.NotInstalled;

                using (var sc = new ServiceController(GuardConstants.ServiceName))
                {
                    switch (sc.Status)
                    {
                        case ServiceControllerStatus.Running: return ServiceState.Running;
                        case ServiceControllerStatus.Stopped: return ServiceState.Stopped;
                        case ServiceControllerStatus.StartPending:
                        case ServiceControllerStatus.ContinuePending: return ServiceState.Starting;
                        case ServiceControllerStatus.StopPending:
                        case ServiceControllerStatus.PausePending: return ServiceState.Stopping;
                        default: return ServiceState.Unknown;
                    }
                }
            }
            catch
            {
                return ServiceState.Unknown;
            }
        }

        /// <returns>Hata mesajı; başarılıysa null.</returns>
        public static string Start()
        {
            try
            {
                using (var sc = new ServiceController(GuardConstants.ServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                        sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
                }
                return null;
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                return "Servis 60 saniye içinde başlamadı. Servis logunu kontrol edin.";
            }
            catch (Exception ex)
            {
                return "Servis başlatılamadı: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
            }
        }

        /// <summary>Servis durana kadar bekler (UI'den durdurma sonrası).</summary>
        public static bool WaitForStopped(TimeSpan timeout)
        {
            try
            {
                using (var sc = new ServiceController(GuardConstants.ServiceName))
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
                return true;
            }
            catch (InvalidOperationException)
            {
                return true; // kayıt silinmiş (kaldırma sonrası): durmuş sayılır
            }
            catch
            {
                return false;
            }
        }

        /// <returns>Hata mesajı; başarılıysa null.</returns>
        public static string InstallAndStart()
        {
            if (!File.Exists(ServiceExe))
                return "Servis dosyası bulunamadı: " + ServiceExe;

            var psi = new ProcessStartInfo(ServiceExe, "--install --start")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    string error = p.StandardError.ReadToEnd();
                    p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(120000)) return "Kurulum zamanında tamamlanmadı.";
                    return p.ExitCode == 0 ? null : "Servis kurulamadı: " + error.Trim();
                }
            }
            catch (Exception ex)
            {
                return "Servis kurulamadı: " + ex.Message;
            }
        }
    }
}
