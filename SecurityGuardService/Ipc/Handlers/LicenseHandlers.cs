using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    /// <summary>Sistem → Lisans. Lisans burada oluşturulmaz (lisans sunucusunda tanımlanır); sadece durum, kontrol ve sunucu adresi.</summary>
    internal static class LicenseHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.LicenseGet, true, true, r => Task.FromResult(WithUpdate(r, r.Guard.License.Info())));

            router.Register(Commands.LicenseCheck, true, true, async r =>
            {
                r.Guard.Update?.CheckNow();   // lisans kontrolüyle birlikte güncellemeyi de hemen kontrol et (arka planda)
                return WithUpdate(r, await r.Guard.License.CheckAsync("elle (" + r.Session.UserName + ")").ConfigureAwait(false));
            });

            router.Register(Commands.LicenseSetApiUrl, true, true, async r =>
            {
                await r.Guard.License.SetApiUrlAsync(r.Payload<LicenseApiUrlRequest>().Url).ConfigureAwait(false);
                return WithUpdate(r, await r.Guard.License.CheckAsync("adres değişti (" + r.Session.UserName + ")").ConfigureAwait(false));
            });
        }

        /// <summary>Lisans bilgisine, hub'dan (lisans sunucusu adresinden) öğrenilen güncelleme durumunu ekler.</summary>
        private static PipeResponse WithUpdate(RequestContext r, LicenseInfoDto info)
        {
            var up = r.Guard.Update;
            if (info != null && up != null)
            {
                info.UpdateAvailableVersion = up.AvailableVersion;
                info.UpdateReady = up.Ready;
            }
            return PipeResponse.Ok(info);
        }
    }
}
