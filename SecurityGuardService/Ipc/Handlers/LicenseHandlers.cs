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
            router.Register(Commands.LicenseGet, true, true, r => Task.FromResult(PipeResponse.Ok(r.Guard.License.Info())));

            router.Register(Commands.LicenseCheck, true, true, async r =>
                PipeResponse.Ok(await r.Guard.License.CheckAsync("elle (" + r.Session.UserName + ")").ConfigureAwait(false)));

            router.Register(Commands.LicenseSetApiUrl, true, true, async r =>
            {
                await r.Guard.License.SetApiUrlAsync(r.Payload<LicenseApiUrlRequest>().Url).ConfigureAwait(false);
                return PipeResponse.Ok(await r.Guard.License.CheckAsync("adres değişti (" + r.Session.UserName + ")").ConfigureAwait(false));
            });
        }
    }
}
