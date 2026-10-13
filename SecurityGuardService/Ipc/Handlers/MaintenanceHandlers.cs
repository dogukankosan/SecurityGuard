using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class MaintenanceHandlers
    {
        public static void Register(CommandRouter router)
        {
            // DB listesi SQL'den canlı okunur (birkaç saniye sürebilir): pipe iş parçacığı bloklanmasın
            router.Register(Commands.MaintenanceGet, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.Maintenance.GetState())));

            router.Register(Commands.MaintenanceSave, true, true, async r =>
            {
                await r.Guard.Maintenance.SaveConfigAsync(r.Payload<MaintenanceConfigDto>(), r.Session.UserName).ConfigureAwait(false);
                return await Task.Run(() => PipeResponse.Ok(r.Guard.Maintenance.GetState())).ConfigureAwait(false);
            });

            router.Register(Commands.MaintenanceRun, true, true, r => Task.Run(() =>
            {
                var req = r.Payload<MaintenanceRunRequest>() ?? new MaintenanceRunRequest { DryRun = true };
                r.Guard.Maintenance.RunNow(req.DryRun, r.Session.UserName); // sürüyorsa yenisi başlamaz, durum yine döner
                return PipeResponse.Ok(r.Guard.Maintenance.GetState());
            }));

            router.Register(Commands.MaintenanceOps, true, true, r => Task.Run(() =>
            {
                var req = r.Payload<RunIdRequest>() ?? new RunIdRequest();
                return PipeResponse.Ok(r.Guard.Maintenance.LoadOps(req.RunId));
            }));
        }
    }
}
