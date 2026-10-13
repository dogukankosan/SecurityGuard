using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuardService.Checks.Security;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class OpenPortsHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.OpenPortsGet, true, true,
                r => Task.Run(() => PipeResponse.Ok(OpenPorts.Build(r.Guard))));
        }
    }
}
