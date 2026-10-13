using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class CanaryHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.CanaryGet, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.Canary.GetState())));

            router.Register(Commands.CanaryAddFolder, true, true, async r =>
            {
                var req = r.Payload<CanaryFolderRequest>() ?? new CanaryFolderRequest();
                await r.Guard.Canary.AddFolderAsync(req.Path, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(await Task.Run(() => r.Guard.Canary.GetState()).ConfigureAwait(false));
            });

            router.Register(Commands.CanaryRemoveFolder, true, true, async r =>
            {
                var req = r.Payload<CanaryFolderRequest>() ?? new CanaryFolderRequest();
                await r.Guard.Canary.RemoveFolderAsync(req.Id, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(await Task.Run(() => r.Guard.Canary.GetState()).ConfigureAwait(false));
            });

            router.Register(Commands.CanarySetEnabled, true, true, async r =>
            {
                var req = r.Payload<CanaryFolderRequest>() ?? new CanaryFolderRequest();
                await r.Guard.Canary.SetEnabledAsync(req.Id, req.Enabled, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(await Task.Run(() => r.Guard.Canary.GetState()).ConfigureAwait(false));
            });

            router.Register(Commands.CanaryCheck, true, true, async r =>
            {
                await r.Guard.Canary.SyncAsync("elle (Şimdi kontrol et)").ConfigureAwait(false);
                return PipeResponse.Ok(await Task.Run(() => r.Guard.Canary.GetState()).ConfigureAwait(false));
            });

            router.Register(Commands.CanaryTest, true, true, async r =>
                PipeResponse.Ok(await r.Guard.Canary.TestAsync(r.Session.UserName).ConfigureAwait(false)));
        }
    }
}
