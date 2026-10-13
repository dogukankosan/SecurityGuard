using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class BackupHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.BackupGet, true, true,
                r => Task.Run(() => PipeResponse.Ok(r.Guard.Backup.GetState())));

            router.Register(Commands.BackupSave, true, true, async r =>
            {
                await r.Guard.Backup.SaveConfigAsync(r.Payload<BackupConfigDto>(), r.Session.UserName).ConfigureAwait(false);
                return await Task.Run(() => PipeResponse.Ok(r.Guard.Backup.GetState())).ConfigureAwait(false);
            });

            router.Register(Commands.BackupRun, true, true, r => Task.Run(() =>
            {
                r.Guard.Backup.RunNow(r.Session.UserName); // sürüyorsa yenisi başlamaz, durum yine döner
                return PipeResponse.Ok(r.Guard.Backup.GetState());
            }));

            router.Register(Commands.BackupFiles, true, true, r => Task.Run(() =>
            {
                var req = r.Payload<RunIdRequest>() ?? new RunIdRequest();
                return PipeResponse.Ok(r.Guard.Backup.LoadFiles(req.RunId));
            }));

            // Şifre işlemleri yönetici şifresini tekrar ister
            router.Register(Commands.BackupSetPassword, true, true, async r =>
            {
                var req = r.Payload<BackupPasswordRequest>() ?? new BackupPasswordRequest();
                if (string.IsNullOrEmpty(req.AdminPassword)) throw new GuardException(ErrorCodes.Validation, "Onay için yönetici şifresini girin.");
                await r.Guard.Admin.LoginAsync(r.Session.UserName, req.AdminPassword).ConfigureAwait(false);
                return PipeResponse.Ok(await r.Guard.Backup.SetPasswordAsync(req.NewPassword, r.Session.UserName).ConfigureAwait(false));
            });

            router.Register(Commands.BackupGetPassword, true, true, async r =>
            {
                var req = r.Payload<PasswordConfirmRequest>() ?? new PasswordConfirmRequest();
                if (string.IsNullOrEmpty(req.Password)) throw new GuardException(ErrorCodes.Validation, "Onay için yönetici şifresini girin.");
                await r.Guard.Admin.LoginAsync(r.Session.UserName, req.Password).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Backup.GetPassword(r.Session.UserName));
            });
        }
    }
}
