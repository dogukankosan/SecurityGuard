using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class RdpSessionHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.RdpSessionsList, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.RdpSessions.List(r.Payload<RdpSessionQuery>()))));

            router.Register(Commands.WorkHoursGet, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.RdpSessions.GetWorkHours())));

            router.Register(Commands.WorkHoursSave, true, true, async r =>
                PipeResponse.Ok(await r.Guard.RdpSessions.SaveWorkHoursAsync(r.Payload<WorkHoursDto>(), r.Session.UserName).ConfigureAwait(false)));
        }
    }
}
