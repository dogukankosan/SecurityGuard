using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class ReportHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.ReportList, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.Reports.List())));

            router.Register(Commands.ReportGet, true, true, r => Task.Run(() =>
            {
                var req = r.Payload<IdRequest>() ?? new IdRequest();
                return PipeResponse.Ok(r.Guard.Reports.Get(req.Id));
            }));

            // Rapor hazırlanırken bütünlük kontrolü ve SQL durumu okunur: birkaç saniye sürebilir
            router.Register(Commands.ReportPreview, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.Reports.Preview())));

            router.Register(Commands.ReportMonthlyPreview, true, true,
                r => Task.Run(() =>
                {
                    var req = r.Payload<IdRequest>();
                    return PipeResponse.Ok(r.Guard.MonthlyReports.Preview(req != null && req.Id == 1)); // Id 1 = bu ay (şimdiye kadar)
                }));

            router.Register(Commands.ReportMonthlySendNow, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.MonthlyReports.SendNow(r.Session.UserName))));

            router.Register(Commands.ReportSendNow, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.Reports.SendNow(r.Session.UserName))));
        }
    }
}
