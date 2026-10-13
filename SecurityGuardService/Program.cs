using System;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService
{
    static class Program
    {
        static int Main(string[] args)
        {
            // Kurulum komutları (setup ve UI kullanır): --install / --start / --uninstall
            if (ServiceSetup.IsSetupCommand(args))
            {
                Console.OutputEncoding = Encoding.UTF8;
                return ServiceSetup.Run(args);
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            if (Environment.UserInteractive)
                RunInteractive();
            else
                ServiceBase.Run(new ServiceBase[] { new GuardService() });
            return 0;
        }

        private static void RunInteractive()
        {
#if DEBUG
            // Geliştirme modu: VS'ten F5 ile servis kurmadan çalışır, loglar konsola da yazılır
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "SecurityGuard Servis - konsol modu";
            AppLog.EchoToConsole = true;
            AppLog.MinLevel = LogLevel.Debug;

            var host = new GuardHost(isWindowsService: false);
            var stop = new ManualResetEvent(false);
            host.StopRequested += () => stop.Set(); // UI'den "Durdur"
            host.Start();

            Console.WriteLine("Durdurmak için Enter'a basın...");
            new Thread(() => { Console.ReadLine(); stop.Set(); }) { IsBackground = true }.Start();
            stop.WaitOne();
            host.Stop();
#else
            Console.WriteLine("SecurityGuard bir Windows servisidir; doğrudan çalıştırılamaz. Yönetim için SecurityGuard arayüzünü kullanın.");
            Console.WriteLine("Kurulum: SecurityGuardService.exe --install --start   Kaldırma: SecurityGuardService.exe --uninstall");
#endif
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            AppLog.Fatal("Program", "Yakalanmamış hata (süreç sonlanıyor: " + (e.IsTerminating ? "evet" : "hayır") + ").",
                e.ExceptionObject as Exception);
            if (e.IsTerminating)
                AppLog.Stop(TimeSpan.FromSeconds(3));
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLog.Error("Program", "Gözlenmeyen görev hatası.", e.Exception);
            e.SetObserved();
        }
    }
}
