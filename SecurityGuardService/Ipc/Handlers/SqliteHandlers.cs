using System;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class SqliteHandlers
    {
        private const string Module = "SQLiteKonsol";

        public static void Register(CommandRouter router)
        {
            router.Register(Commands.SqliteSchema, true, true, async r =>
            {
                var guard = r.Guard;
                var schema = await Task.Run(() => guard.Sqlite.GetSchema(guard.Database.SchemaVersion)).ConfigureAwait(false);
                return PipeResponse.Ok(schema);
            });

            router.Register(Commands.SqliteExecute, true, true, async r =>
            {
                var req = r.Payload<SqliteExecuteRequest>();
                var guard = r.Guard;
                var result = await guard.Sqlite.ExecuteAsync(req == null ? null : req.Sql, r.Session.UserName, () => ReloadCaches(guard)).ConfigureAwait(false);
                return PipeResponse.Ok(result);
            });
        }

        /// <summary>Elle yapılan değişiklikler (ayar, beyaz liste, kural) servis yeniden başlatılmadan geçerli olsun.</summary>
        private static void ReloadCaches(GuardContext guard)
        {
            Try("ayarlar", guard.Settings.Reload);
            Try("beyaz liste", guard.Whitelist.Reload);
            Try("kurallar", guard.Rules.Reload);
        }

        private static void Try(string what, Action reload)
        {
            try
            {
                reload();
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Sorgu sonrası " + what + " yeniden okunamadı.", ex);
            }
        }
    }
}
