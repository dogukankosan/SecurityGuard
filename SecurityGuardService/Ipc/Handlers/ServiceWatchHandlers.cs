using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class ServiceWatchHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.ServiceWatchGet, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.Services.GetState())));

            router.Register(Commands.ServiceWatchSave, true, true, async r =>
            {
                await r.Guard.Services.SaveAsync(r.Payload<ServiceWatchSaveRequest>(), r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(await Task.Run(() => r.Guard.Services.GetState()).ConfigureAwait(false));
            });
        }
    }
}
