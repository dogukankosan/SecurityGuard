using System;
using System.Threading.Tasks;
using DevExpress.XtraEditors;
using DevExpress.XtraSplashScreen;
using SecurityGuardUI.Helpers;
using SecurityGuardUI.Ipc;

namespace SecurityGuardUI.Modules
{
    /// <summary>Ana formun modüllere sunduğu işlemler.</summary>
    public interface IModuleHost
    {
        /// <summary>Servis "oturum geçersiz" dediğinde: ana form kapanır, giriş ekranına dönülür.</summary>
        void HandleSessionExpired();

        /// <summary>Servis durduruldu / yeniden başlatıldı: ana form kapanır, en baştan servise bağlanılır.</summary>
        void ReturnToStart(string message);

        /// <summary>Programı kapatır (ör. servis kaldırıldıktan sonra).</summary>
        void ExitApplication();

        /// <summary>Başka bir sayfayı açar (Panel'deki kutucuklar / yapılacaklar). Sayfa adları: DashboardPages.</summary>
        void Navigate(string page);

        /// <summary>Servis "lisans geçerli değil" dedi: lisans sayfası açılır.</summary>
        void HandleUnlicensed(string message);

        /// <summary>Lisans sayfasında lisans geçerli hale geldi (lisanssız açılmışsa kurulum / ana ekran baştan açılır).</summary>
        void LicenseActivated();
    }

    /// <summary>
    /// Tüm modüllerin (ana formun sağ tarafındaki sayfaların) tabanı.
    /// Servis çağrıları <see cref="CallAsync{T}"/> ile yapılır: yükleniyor göstergesi, hata mesajı ve
    /// oturum düşmesi tek yerden yönetilir; modül sadece başarılı sonucu işler.
    /// </summary>
    public class ModuleBase : XtraUserControl
    {
        private bool _loaded;

        protected GuardClient Client { get; private set; }

        protected IModuleHost Host { get; private set; }

        internal void Attach(IModuleHost host, GuardClient client)
        {
            Host = host;
            Client = client;
        }

        /// <summary>Modül ilk kez gösterildiğinde bir kez çağrılır (ilk veri yükleme).</summary>
        internal async void Activate()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                await OnFirstShowAsync();
            }
            catch (Exception ex)
            {
                Ui.Error(FindForm(), "Sayfa yüklenirken hata oluştu:" + Environment.NewLine + ex.Message);
            }
        }

        protected virtual Task OnFirstShowAsync()
        {
            return Task.FromResult(0);
        }

        /// <summary>Sonuç dönen servis çağrısı. Başarısızsa mesajı gösterir ve null döner.</summary>
        protected async Task<T> CallAsync<T>(Func<GuardClient, Task<GuardResult<T>>> call) where T : class
        {
            GuardResult<T> result = await WithOverlayAsync(() => call(Client));
            return HandleResult(result) ? result.Value : null;
        }

        /// <summary>Sonuç dönmeyen servis çağrısı. Başarılıysa true.</summary>
        protected async Task<bool> CallAsync(Func<GuardClient, Task<GuardResult>> call)
        {
            GuardResult result = await WithOverlayAsync(() => call(Client));
            return HandleResult(result);
        }

        /// <summary>
        /// Hata penceresi açmadan çağırır (sonucu ekranda kendisi gösterecek işlemler için, ör. test maili).
        /// Oturum düşmesi yine yönetilir.
        /// </summary>
        protected async Task<GuardResult> CallQuietAsync(Func<GuardClient, Task<GuardResult>> call)
        {
            GuardResult result = await WithOverlayAsync(() => call(Client));
            if (result.IsUnauthorized) Host.HandleSessionExpired();
            else if (result.IsUnlicensed) Host.HandleUnlicensed(result.ErrorMessage);
            return result;
        }

        /// <summary>Sonuç dönen, hata penceresi açmayan çağrı (sonuç / hata ekranda gösterilecekse).</summary>
        protected async Task<GuardResult<T>> CallQuietAsync<T>(Func<GuardClient, Task<GuardResult<T>>> call) where T : class
        {
            GuardResult<T> result = await WithOverlayAsync(() => call(Client));
            if (result.IsUnauthorized) Host.HandleSessionExpired();
            else if (result.IsUnlicensed) Host.HandleUnlicensed(result.ErrorMessage);
            return result;
        }

        private bool HandleResult(GuardResult result)
        {
            if (result.Success) return true;
            if (result.IsUnauthorized)
            {
                Host.HandleSessionExpired();
                return false;
            }
            if (result.IsUnlicensed)
            {
                Host.HandleUnlicensed(result.ErrorMessage);
                return false;
            }
            Ui.Error(FindForm(), result.ErrorMessage);
            return false;
        }

        private async Task<TResult> WithOverlayAsync<TResult>(Func<Task<TResult>> work)
        {
            IOverlaySplashScreenHandle overlay = null;
            try
            {
                if (IsHandleCreated && Visible)
                    overlay = SplashScreenManager.ShowOverlayForm(this);
                return await work();
            }
            finally
            {
                if (overlay != null) SplashScreenManager.CloseOverlayForm(overlay);
            }
        }
    }
}
