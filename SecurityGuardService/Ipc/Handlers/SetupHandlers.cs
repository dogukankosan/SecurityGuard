using System.Linq;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Ipc.Handlers
{
    /// <summary>
    /// İlk kurulum sihirbazı: durum ve tamamlama. Zorunlu adımlar servis tarafında da denetlenir:
    /// SQL Server varsa en az bir bağlantı + bağlanan instance'larda SQL izlemesi, mail ayarı (test edilmiş) + en az bir aktif alıcı.
    /// </summary>
    internal static class SetupHandlers
    {
        private const string Module = "Kurulum";
        public const string CompletedKey = "setup.completed";

        public static void Register(CommandRouter router)
        {
            router.Register(Commands.SetupStatus, true, true, r => Task.Run(() => PipeResponse.Ok(Status(r.Guard))));

            router.Register(Commands.SetupComplete, true, true, async r =>
            {
                var status = await Task.Run(() => Status(r.Guard)).ConfigureAwait(false);
                if (!status.SqlStepDone)
                    throw new GuardException(ErrorCodes.Validation, status.SqlConnected
                        ? "SQL izlemesi kurulmadan kurulum bitirilemez."
                        : "SQL Server bağlantısı tanımlanmadan kurulum bitirilemez.");
                if (!status.MailStepDone)
                    throw new GuardException(ErrorCodes.Validation, status.MailConfigured
                        ? "En az bir aktif mail alıcısı eklenmeden kurulum bitirilemez."
                        : "Mail ayarı (test maili başarılı) yapılmadan kurulum bitirilemez.");

                await r.Guard.Settings.SetAsync(CompletedKey, true).ConfigureAwait(false);
                AuditTrail.Record(r.Guard.Database, r.Session.UserName, "İlk kurulum tamamlandı",
                    "SQL: " + (status.SqlDetected ? "bağlı, izleme kurulu" : "sunucuda yok") + ", mail alıcısı: " + status.ActiveRecipients + ", beyaz liste: " + status.WhitelistEntries);
                AppLog.Info(Module, "İlk kurulum tamamlandı (" + r.Session.UserName + "). İlk güvenlik taraması başlatılıyor.");

                // Kurulumun hemen ardından güvenlik taraması: eksikler Panel'de hemen görünsün
                r.Guard.Checks.RunNow(CheckScopes.Security, r.Session.UserName);
                status.Completed = true;
                return PipeResponse.Ok(status);
            });
        }

        internal static SetupStatusDto Status(GuardContext g)
        {
            var instances = g.Sql.List();
            var connected = instances.Where(i => i.IsConfigured && i.IsEnabled).ToList();
            var whitelist = g.Whitelist.ReadAll();
            return new SetupStatusDto
            {
                Completed = g.Settings.GetBool(CompletedKey, false),
                SqlDetected = instances.Any(i => i.Detected),
                SqlConnected = connected.Count > 0,
                SqlAuditInstalled = connected.Count > 0 && connected.All(i => g.SqlAudit.IsInstalled(i.InstanceName)),
                SqlMissingInstances = instances.Where(i => i.Detected && !(i.IsConfigured && i.IsEnabled)).Select(i => i.InstanceName).ToList(),
                MailConfigured = g.Mail.IsConfigured,
                ActiveRecipients = g.Mail.ListRecipients().Count(x => x.IsEnabled),
                WhitelistEntries = whitelist.Count(w => !w.IsSystem),
                ProtectedAutomatically = whitelist.Where(w => w.IsSystem).Select(w => w.Cidr + (string.IsNullOrEmpty(w.Note) ? "" : " (" + w.Note + ")")).ToList()
            };
        }
    }
}
