using System;
using System.Threading;
using System.Windows.Forms;
using DevExpress.XtraSplashScreen;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Forms;
using SecurityGuardUI.Helpers;
using SecurityGuardUI.Ipc;

namespace SecurityGuardUI
{
    /// <summary>
    /// Programın ekran akışı:
    /// servise bağlan → kayıtlı temayı uygula → (yönetici yoksa) ilk kurulum → giriş → ana form.
    /// Ana formdan çıkış yapılırsa veya oturum düşerse tekrar giriş ekranına dönülür.
    /// Servis arayüzden durdurulur / yeniden başlatılırsa en başa (bağlanma) dönülür.
    ///
    /// Servise bağlanılamazsa Windows servis yöneticisine bakılır:
    /// kurulu değilse "kur ve başlat", durmuşsa "başlat" önerilir (Debug'da sadece hatırlatılır).
    /// </summary>
    internal sealed class AppFlow
    {
        /// <summary>Servis açılırken (DB hazırlanırken) en fazla bu kadar beklenir.</summary>
        private static readonly TimeSpan ReadyWait = TimeSpan.FromSeconds(30);

        /// <summary>Servise hiç ulaşılamıyorsa (henüz başlamamış olabilir) bu kadar tekrar denenir.</summary>
        private static readonly TimeSpan UnavailableWait = TimeSpan.FromSeconds(6);

        private readonly GuardClient _client;

        public AppFlow(GuardClient client)
        {
            _client = client;
        }

        public void Run()
        {
            ThemeManager.Initialize();
            string loginMessage = null;

            while (true)
            {
                StatusDto status = Connect();
                if (status == null) return;

                ThemeManager.Apply(status.Theme);
                ThemeManager.TrackChanges(_client);

                if (!status.AdminExists)
                {
                    using (var setup = new SetupAdminForm(_client))
                        if (setup.ShowDialog() != DialogResult.OK) return;
                }

                if (RunSession(status, ref loginMessage) == MainExitReason.Closed) return;

                // Servis durduruldu / yeniden başlatıldı: oturum geçersiz, en baştan bağlanılır
                _client.ForgetSession();
            }
        }

        /// <summary>Giriş → ana form döngüsü. Program kapanırsa Closed, servis durursa ServiceStopped döner.</summary>
        private MainExitReason RunSession(StatusDto status, ref string loginMessage)
        {
            while (true)
            {
                if (!_client.IsLoggedIn)
                {
                    using (var login = new LoginForm(_client, status.MachineName, loginMessage))
                        if (login.ShowDialog() != DialogResult.OK) return MainExitReason.Closed;
                }

                // İlk kurulum bitmediyse önce sihirbaz (SQL ve mail zorunlu)
                var setup = _client.SendAsync<SetupStatusDto>(Commands.SetupStatus).Result;
                if (setup.IsUnauthorized)
                {
                    _client.ForgetSession();
                    continue;
                }
                bool unlicensed = !setup.Success && setup.IsUnlicensed;
                if (setup.Success && !setup.Value.Completed)
                {
                    using (var wizard = new SetupWizardForm(_client, setup.Value))
                    {
                        wizard.ShowDialog();
                        if (wizard.ExitReason == MainExitReason.Closed)
                        {
                            if (_client.IsLoggedIn) _client.LogoutAsync().Wait(TimeSpan.FromSeconds(2));
                            return MainExitReason.Closed;
                        }
                        if (wizard.ExitReason == MainExitReason.ServiceStopped) return MainExitReason.ServiceStopped;
                        if (wizard.ExitReason == MainExitReason.Reload) continue;
                        if (wizard.ExitReason == MainExitReason.SessionExpired)
                        {
                            loginMessage = wizard.ExitMessage;
                            _client.ForgetSession();
                            continue;
                        }
                    }
                }

                MainExitReason reason;
                using (var main = new MainForm(_client, status, unlicensed))
                {
                    Application.Run(main);
                    reason = main.ExitReason;
                    loginMessage = main.ExitMessage;
                }

                if (reason == MainExitReason.Closed)
                {
                    // Pencere kapatıldı: servis tarafındaki oturum da kapatılır
                    if (_client.IsLoggedIn) _client.LogoutAsync().Wait(TimeSpan.FromSeconds(2));
                    return reason;
                }
                if (reason == MainExitReason.ServiceStopped) return reason;
            }
        }

        /// <returns>Hazır servis durumu; kullanıcı vazgeçerse null.</returns>
        private StatusDto Connect()
        {
            while (true)
            {
                GuardResult<StatusDto> result = null;
                SplashScreenManager.ShowDefaultWaitForm("SecurityGuard", "Servise bağlanılıyor...");
                try
                {
                    DateTime start = DateTime.UtcNow;
                    while (true)
                    {
                        result = _client.GetStatusAsync().Result;
                        if (result.Success && result.Value.Ready) break;

                        // Servis henüz açılmamış olabilir (UI ile aynı anda başlatıldı): kısa süre sessizce tekrar dene.
                        // Servis açıldı ama DB hazır değilse daha uzun beklenir.
                        bool unavailable = !result.Success && result.ErrorCode == ErrorCodes.ServiceUnavailable;
                        TimeSpan limit = unavailable ? UnavailableWait : ReadyWait;
                        if ((!result.Success && !unavailable) || DateTime.UtcNow - start >= limit) break;
                        Thread.Sleep(500);
                    }
                }
                finally
                {
                    SplashScreenManager.CloseForm(false);
                }

                if (result.Success && result.Value.Ready) return result.Value;

                bool retry;
                if (result.Success)
                    retry = Ui.RetryOrCancel(null, "Servis çalışıyor ancak henüz hazır değil (veritabanı açılamamış olabilir). Servis logunu kontrol edin.");
                else if (result.ErrorCode == ErrorCodes.ServiceUnavailable)
                    retry = HandleServiceUnavailable();
                else
                    retry = Ui.RetryOrCancel(null, result.ErrorMessage);

                if (!retry) return null;
            }
        }

        /// <returns>Tekrar bağlanmayı denemek için true; vazgeçilirse false.</returns>
        private static bool HandleServiceUnavailable()
        {
#if DEBUG
            return Ui.RetryOrCancel(null,
                "Servise bağlanılamadı." + Environment.NewLine + Environment.NewLine +
                "Geliştirme modundasınız: SecurityGuardService projesini F5 ile başlatın " +
                "(Solution → Properties → Multiple startup projects).");
#else
            switch (ServiceControl.GetState())
            {
                case ServiceState.NotInstalled:
                    if (!Ui.Confirm(null, "SecurityGuard servisi bu bilgisayarda kurulu değil." + Environment.NewLine +
                        "Servis olmadan sunucu korunmaz ve program çalışmaz." + Environment.NewLine + Environment.NewLine +
                        "Servis şimdi kurulup başlatılsın mı?"))
                        return false;
                    return RunWithWaitForm("Servis kuruluyor...", ServiceControl.InstallAndStart);

                case ServiceState.Stopped:
                    if (!Ui.Confirm(null, "SecurityGuard servisi durmuş durumda; sunucu şu an korunmuyor." + Environment.NewLine + Environment.NewLine +
                        "Servis şimdi başlatılsın mı?"))
                        return false;
                    return RunWithWaitForm("Servis başlatılıyor...", ServiceControl.Start);

                case ServiceState.Starting:
                    return true; // biraz sonra tekrar bağlanmayı dene

                default:
                    return Ui.RetryOrCancel(null,
                        "SecurityGuard servisi çalışıyor görünüyor ama yanıt vermiyor." + Environment.NewLine +
                        "Birkaç saniye sonra tekrar deneyin. Sorun sürerse servis logunu kontrol edin:" + Environment.NewLine +
                        System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Logs")));
            }
#endif
        }

        /// <summary>Uzun süren servis işlemini bekleme penceresiyle çalıştırır; hata varsa gösterir.</summary>
        private static bool RunWithWaitForm(string caption, Func<string> action)
        {
            string error;
            SplashScreenManager.ShowDefaultWaitForm("SecurityGuard", caption);
            try
            {
                error = action();
            }
            finally
            {
                SplashScreenManager.CloseForm(false);
            }

            if (error == null) return true;
            return Ui.RetryOrCancel(null, error);
        }
    }
}
