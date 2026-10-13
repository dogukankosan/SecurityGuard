using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class CheckHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.ChecksLatest, true, true, r => Task.Run(() =>
            {
                var req = r.Payload<CheckScopeRequest>() ?? new CheckScopeRequest();
                return PipeResponse.Ok(r.Guard.Checks.Latest(req.Scope));
            }));

            router.Register(Commands.ChecksRun, true, true, r => Task.Run(() =>
            {
                var req = r.Payload<CheckScopeRequest>() ?? new CheckScopeRequest();
                r.Guard.Checks.RunNow(req.Scope, r.Session.UserName); // sürüyorsa yenisi başlamaz, durum yine döner
                return PipeResponse.Ok(r.Guard.Checks.Latest(req.Scope));
            }));

            router.Register(Commands.ChecksIgnore, true, true, async r =>
            {
                var req = r.Payload<CheckIgnoreRequest>();
                await r.Guard.Checks.SetIgnoreAsync(req, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Checks.Latest(req.Scope));
            });
        }
    }
}
