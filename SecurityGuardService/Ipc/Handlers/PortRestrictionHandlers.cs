using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class PortRestrictionHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.PortRestrictGet, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.PortRestrictions.GetState())));

            router.Register(Commands.PortRestrictCheck, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.PortRestrictions.Check(r.Payload<PortRestrictionSaveRequest>()))));

            router.Register(Commands.PortRestrictSave, true, true, async r =>
                PipeResponse.Ok(new IdRequest { Id = await r.Guard.PortRestrictions.SaveAsync(r.Payload<PortRestrictionSaveRequest>(), r.Session.UserName).ConfigureAwait(false) }));

            router.Register(Commands.PortRestrictConfirm, true, true, async r =>
            {
                await r.Guard.PortRestrictions.ConfirmAsync(Id(r), r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(true);
            });

            router.Register(Commands.PortRestrictRevert, true, true, async r =>
            {
                await r.Guard.PortRestrictions.RevertNowAsync(Id(r), r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(true);
            });

            router.Register(Commands.PortRestrictDisable, true, true, async r =>
            {
                await r.Guard.PortRestrictions.DisableAsync(Id(r), r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(true);
            });

            router.Register(Commands.PortRestrictDelete, true, true, async r =>
            {
                await r.Guard.PortRestrictions.DeleteAsync(Id(r), r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(true);
            });
        }

        private static long Id(RequestContext r)
        {
            var req = r.Payload<IdRequest>();
            return req == null ? 0 : req.Id;
        }
    }
}
