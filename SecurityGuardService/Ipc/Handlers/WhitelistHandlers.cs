using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class WhitelistHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.WhitelistList, true, true,
                r => Task.FromResult(PipeResponse.Ok(r.Guard.Whitelist.ReadAll())));

            router.Register(Commands.WhitelistAdd, true, true, async r =>
            {
                var req = r.Payload<WhitelistAddRequest>();
                var entry = await r.Guard.Whitelist.AddAsync(req.Cidr, req.Note, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(entry);
            });

            router.Register(Commands.WhitelistDelete, true, true, async r =>
            {
                var req = r.Payload<IdListRequest>();
                await r.Guard.Whitelist.DeleteAsync(req.Ids, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok();
            });
        }
    }
}
