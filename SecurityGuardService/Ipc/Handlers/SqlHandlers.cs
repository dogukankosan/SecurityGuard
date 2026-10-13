using System.Collections.Generic;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Sql;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class SqlHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.SqlInstances, true, true,
                r => Task.Run(() => PipeResponse.Ok(List(r.Guard))));

            // Bağlantı denemesi 10 sn'ye kadar sürebilir: pipe iş parçacığını bloklamamak için arka planda
            router.Register(Commands.SqlTest, true, true, async r =>
            {
                var req = r.Payload<SqlConnectionRequest>();
                var result = await Task.Run(() => r.Guard.Sql.Test(req, r.Session.UserName)).ConfigureAwait(false);
                return PipeResponse.Ok(result);
            });

            router.Register(Commands.SqlSave, true, true, async r =>
            {
                await r.Guard.Sql.SaveAsync(r.Payload<SqlConnectionRequest>(), r.Session.UserName).ConfigureAwait(false);
                r.Guard.SqlMonitor.CheckSoon();
                return PipeResponse.Ok(List(r.Guard));
            });

            router.Register(Commands.SqlAuditInstall, true, true, async r =>
            {
                var req = r.Payload<SqlInstanceRequest>() ?? new SqlInstanceRequest();
                await Task.Run(() => r.Guard.SqlAudit.InstallAsync(req.InstanceName, r.Session.UserName)).ConfigureAwait(false);
                return PipeResponse.Ok(List(r.Guard));
            });

            router.Register(Commands.SqlAuditUninstall, true, true, async r =>
            {
                var req = r.Payload<SqlInstanceRequest>() ?? new SqlInstanceRequest();
                await Task.Run(() => r.Guard.SqlAudit.UninstallAsync(req.InstanceName, r.Session.UserName)).ConfigureAwait(false);
                return PipeResponse.Ok(List(r.Guard));
            });

            router.Register(Commands.SqlAuditScript, true, false,
                r => Task.FromResult(PipeResponse.Ok(SqlAuditManager.InstallScript())));
        }

        /// <summary>Instance listesi + izleme (audit) durumu (son kontrolden; SQL'e gitmez).</summary>
        private static List<SqlInstanceDto> List(GuardContext guard)
        {
            var list = guard.Sql.List();
            foreach (var i in list)
            {
                var state = guard.SqlAudit.GetState(i.InstanceName);
                i.AuditInstalled = state.Installed;
                i.AuditRunning = state.Running;
                i.AuditStatus = state.Message;
            }
            return list;
        }
    }
}
