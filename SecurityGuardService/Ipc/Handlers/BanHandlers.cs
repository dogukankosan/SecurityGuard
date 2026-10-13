using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class BanHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.BanList, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.Ban.List())));

            router.Register(Commands.BanAdd, true, true, async r =>
            {
                var req = r.Payload<BanAddRequest>() ?? new BanAddRequest();
                await r.Guard.Ban.BanManualAsync(req.IpAddress, req.Reason, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Ban.List());
            });

            router.Register(Commands.BanRemove, true, true, async r =>
            {
                var req = r.Payload<IpListRequest>() ?? new IpListRequest();
                await r.Guard.Ban.UnbanAsync(req.IpAddresses, req.ResetCounter, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Ban.List());
            });

            router.Register(Commands.BanResetAttempts, true, true, async r =>
            {
                var req = r.Payload<IpListRequest>() ?? new IpListRequest();
                await r.Guard.Ban.ResetAttemptsAsync(req.IpAddresses, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Ban.List());
            });

            router.Register(Commands.BanSyncFirewall, true, true, async r =>
            {
                await Task.Run(() => r.Guard.Ban.SyncFirewall()).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Ban.List());
            });
        }
    }
}
