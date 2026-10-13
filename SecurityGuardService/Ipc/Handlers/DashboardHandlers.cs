using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuardService.Engine.Reports;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class DashboardHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.DashboardGet, true, true,
                r => Task.Run(() => PipeResponse.Ok(Dashboard.Build(r.Guard))));
        }
    }
}
