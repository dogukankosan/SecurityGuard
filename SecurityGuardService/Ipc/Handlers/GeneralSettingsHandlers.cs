using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core.Settings;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class GeneralSettingsHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.GeneralSettingsList, true, true,
                r => Task.FromResult(PipeResponse.Ok(GeneralSettings.List(r.Guard.Settings))));

            router.Register(Commands.GeneralSettingsSave, true, true, async r =>
            {
                var req = r.Payload<GeneralSettingsSaveRequest>();
                await GeneralSettings.SaveAsync(r.Guard.Settings, r.Guard.Database, req == null ? null : req.Values, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(GeneralSettings.List(r.Guard.Settings));
            });
        }
    }
}
