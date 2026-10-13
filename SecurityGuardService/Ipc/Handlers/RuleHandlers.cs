using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class RuleHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.RulesList, true, true,
                r => Task.FromResult(PipeResponse.Ok(r.Guard.Rules.ReadAll())));

            router.Register(Commands.RulesSave, true, true, async r =>
            {
                var req = r.Payload<RuleSaveRequest>();
                await r.Guard.Rules.SaveAsync(req.Rules, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Rules.ReadAll());
            });

            router.Register(Commands.RulesReset, true, true, async r =>
            {
                await r.Guard.Rules.ResetDefaultsAsync(r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Rules.ReadAll());
            });
        }
    }
}
