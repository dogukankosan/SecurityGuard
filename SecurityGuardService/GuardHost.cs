using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Ipc;
using SecurityGuardService.Ipc.Handlers;

namespace SecurityGuardService
{
    /// <summary>
    /// Servisin yaşam döngüsü. Windows servisi (GuardService) ve konsol modu (F5) aynı sınıfı kullanır.
    /// Start hızlı döner (Windows servis başlatma süresini aşmamak için); asıl açılış arka planda,
    /// hata olursa artan bekleme ile tekrar denenir. Servis hiçbir hatada çökmez.
    /// Yönetim kanalı (pipe) açılıştan önce başlar: UI açılış sürerken de "servis açılıyor" durumunu görebilir.
    /// </summary>
    internal sealed class GuardHost
    {
        private const string Module = "Host";
        private static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)
        };

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _startup;
        private volatile GuardContext _context;
        private PipeServer _pipeServer;
        private int _stopRequested;
        private volatile string _stopReason;

        public GuardHost(bool isWindowsService)
        {
            IsWindowsService = isWindowsService;
        }

        /// <summary>Windows servisi olarak mı çalışıyor (false = F5 / konsol modu).</summary>
        public bool IsWindowsService { get; private set; }

        public DateTime StartedUtc { get; private set; }

        /// <summary>UI'den durdurma istendiğinde tetiklenir; çalıştıran taraf (servis / konsol) durdurmayı yapar.</summary>
        public event Action StopRequested;

        /// <summary>
        /// Servisin kendini durdurmasını ister. Kısa gecikmeyle çalışır: isteği yapan UI'ye cevap yazılabilsin.
        /// </summary>
        public void RequestStop(string reason)
        {
            if (Interlocked.Exchange(ref _stopRequested, 1) != 0) return;
            _stopReason = reason;
            AppLog.Warn(Module, "Durdurma istendi: " + reason);
            Task.Delay(1000).ContinueWith(_ =>
            {
                var handler = StopRequested;
                if (handler != null) handler();
            });
        }

        public void Start()
        {
            StartedUtc = DateTime.UtcNow;
            try
            {
                AppPaths.EnsureCreated();
            }
            catch
            {
                // Açılış döngüsünde tekrar denenecek
            }

            AppLog.Start(AppPaths.LogFolder);
            AppLog.Info(Module, "Servis başlıyor. Sürüm " + Assembly.GetExecutingAssembly().GetName().Version
                + " (build " + BuildStamp() + "), " + (Environment.Is64BitProcess ? "64" : "32") + "-bit, makine " + Environment.MachineName + ".");

            try
            {
                var router = new CommandRouter(() => _context, new SessionManager());
                SystemHandlers.Register(router);
                AuditTrailHandlers.Register(router);
                WhitelistHandlers.Register(router);
                RuleHandlers.Register(router);
                MailHandlers.Register(router);
                ServiceHandlers.Register(router, this);
                SqlHandlers.Register(router);
                SqliteHandlers.Register(router);
                EventLogHandlers.Register(router);
                BanHandlers.Register(router);
                GeneralSettingsHandlers.Register(router);
                CheckHandlers.Register(router);
                MaintenanceHandlers.Register(router);
                ReportHandlers.Register(router);
                BackupHandlers.Register(router);
                PortRestrictionHandlers.Register(router);
                CanaryHandlers.Register(router);
                OpenPortsHandlers.Register(router);
                RdpSessionHandlers.Register(router);
                ServiceWatchHandlers.Register(router);
                DashboardHandlers.Register(router);
                SetupHandlers.Register(router);
                LicenseHandlers.Register(router);
                _pipeServer = new PipeServer(router);
                _pipeServer.Start();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Yönetim kanalı başlatılamadı.", ex);
            }

            _startup = Task.Run(() => StartupLoop(_cts.Token));
        }

        private void StartupLoop(CancellationToken token)
        {
            int attempt = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    AppPaths.EnsureCreated();
                    _context = GuardContext.Open(IsWindowsService);

                    // Sonraki adımlarda modüller (collector, ban, mail, lisans...) burada başlatılacak.

                    AppLog.Info(Module, "Servis çalışıyor.");
                    return;
                }
                catch (Exception ex)
                {
                    var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                    attempt++;
                    AppLog.Error(Module, "Açılış başarısız (deneme " + attempt + "), " + delay.TotalSeconds + " sn sonra tekrar denenecek.", ex);

                    if (token.WaitHandle.WaitOne(delay)) return;
                }
            }
        }

        public void Stop(bool shutdown = false)
        {
            AppLog.Info(Module, "Servis durduruluyor" + (shutdown ? " (sunucu kapanıyor)." : "."));
            try
            {
                var ctx = _context;
                if (ctx != null && ctx.Protection != null) ctx.Protection.OnStopping(_stopReason, _stopReason != null, shutdown);
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Kapanış bildirimi yazılamadı.", ex);
            }
            try
            {
                _cts.Cancel();
                if (_pipeServer != null) _pipeServer.Dispose();
                if (_startup != null) _startup.Wait(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Arka plan görevleri durdurulamadı.", ex);
            }

            try
            {
                if (_context != null) _context.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Veritabanı kapatılamadı.", ex);
            }

            AppLog.Info(Module, "Servis durdu.");
            AppLog.Stop(TimeSpan.FromSeconds(5));
        }

        /// <summary>Exe'nin derlenme / kopyalanma zamanı: sunucuda hangi sürümün çalıştığı logdan anlaşılsın.</summary>
        private static string BuildStamp()
        {
            try
            {
                return System.IO.File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return "?";
            }
        }
    }
}
