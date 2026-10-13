using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Ipc.Handlers
{
    /// <summary>
    /// Servis Yönetimi: bilgi, durdurma, kaldırma.
    /// Durdurma ve kaldırma yönetici şifresinin tekrar girilmesini ister (yanlış şifre giriş kilidi sayacına işler).
    /// Cevap UI'ye yazıldıktan sonra servis kendini durdurur.
    /// </summary>
    internal static class ServiceHandlers
    {
        private const string Module = "Servis";

        public static void Register(CommandRouter router, GuardHost host)
        {
            router.Register(Commands.ServiceInfo, true, true, r => Task.FromResult(PipeResponse.Ok(Info(r, host))));

            router.Register(Commands.ServiceStop, true, true, async r =>
            {
                await ConfirmPasswordAsync(r).ConfigureAwait(false);
                AuditTrail.Record(r.Guard.Database, r.Session.UserName, "Servis durduruldu", "Arayüzden");
                host.RequestStop("Arayüzden durduruldu (" + r.Session.UserName + ")");
                return PipeResponse.Ok();
            });

            router.Register(Commands.ServiceUninstall, true, true, async r =>
            {
                if (!host.IsWindowsService)
                    throw new GuardException(ErrorCodes.Validation, "Servis Windows'a kurulu değil (geliştirme / konsol modu). Kaldırılacak bir şey yok.");

                await ConfirmPasswordAsync(r).ConfigureAwait(false);
                try
                {
                    ServiceSetup.MarkForDeletion(); // kayıt, servis durunca silinir
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Servis kaydı silinemedi.", ex);
                    throw new GuardException(ErrorCodes.Internal, "Servis kaydı silinemedi: " + ex.Message);
                }
                AuditTrail.Record(r.Guard.Database, r.Session.UserName, "Servis kaldırıldı", "Veritabanı ve loglar korundu");
                host.RequestStop("Arayüzden kaldırıldı (" + r.Session.UserName + ")");
                return PipeResponse.Ok();
            });
        }

        private static ServiceInfoDto Info(RequestContext r, GuardHost host)
        {
            long size = 0;
            try
            {
                var file = new FileInfo(AppPaths.DatabaseFile);
                size = file.Exists ? file.Length : 0;
                var wal = new FileInfo(AppPaths.DatabaseFile + "-wal");
                if (wal.Exists) size += wal.Length;
            }
            catch (IOException)
            {
                // boyut bilgisi olmadan devam
            }

            return new ServiceInfoDto
            {
                Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                StartedUtc = UtcTime.ToText(host.StartedUtc),
                ProcessId = Process.GetCurrentProcess().Id,
                IsWindowsService = host.IsWindowsService,
                DatabaseFile = AppPaths.DatabaseFile,
                DatabaseSizeBytes = size,
                SchemaVersion = r.Guard.Database.SchemaVersion,
                LogFolder = AppPaths.LogFolder,
                MailPending = r.Guard.Mail.PendingCount(),
                Protection = r.Guard.Protection == null ? null : r.Guard.Protection.Status
            };
        }

        /// <summary>Oturumdaki yöneticinin şifresini doğrular (giriş kuralları ve kilit aynen geçerli).</summary>
        private static async Task ConfirmPasswordAsync(RequestContext r)
        {
            var req = r.Payload<PasswordConfirmRequest>();
            if (string.IsNullOrEmpty(req.Password))
                throw new GuardException(ErrorCodes.Validation, "Onay için yönetici şifresini girin.");
            await r.Guard.Admin.LoginAsync(r.Session.UserName, req.Password).ConfigureAwait(false);
        }
    }
}
