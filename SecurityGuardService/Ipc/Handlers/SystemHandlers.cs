using System;
using System.Reflection;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Settings;

namespace SecurityGuardService.Ipc.Handlers
{
    /// <summary>Servis durumu ve yönetici girişi komutları.</summary>
    internal static class SystemHandlers
    {
        private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public static void Register(CommandRouter router)
        {
            router.Register(Commands.GetStatus, false, false, GetStatus);
            router.Register(Commands.CreateAdmin, false, true, CreateAdmin);
            router.Register(Commands.Login, false, true, Login);
            router.Register(Commands.Logout, true, true, Logout);
            router.Register(Commands.Ping, true, true, r => Task.FromResult(PipeResponse.Ok()));
            router.Register(Commands.SetTheme, true, true, SetTheme);
        }

        private static Task<PipeResponse> GetStatus(RequestContext r)
        {
            var status = new StatusDto
            {
                ServiceVersion = Version,
                Ready = r.Guard != null,
                MachineName = Environment.MachineName
            };

            if (r.Guard != null)
            {
                status.AdminExists = r.Guard.Admin.Exists();
                status.Licensed = Licensing.LicenseGate.Active;
                string skin = r.Guard.Settings.Get(SettingKeys.UiThemeSkin);
                if (skin != null)
                    status.Theme = new ThemeDto { SkinName = skin, PaletteName = r.Guard.Settings.Get(SettingKeys.UiThemePalette, "") };
            }
            return Task.FromResult(PipeResponse.Ok(status));
        }

        private static async Task<PipeResponse> SetTheme(RequestContext r)
        {
            var theme = r.Payload<ThemeDto>();
            string skin = (theme.SkinName ?? "").Trim();
            string palette = (theme.PaletteName ?? "").Trim();
            if (skin.Length == 0 || skin.Length > 100 || palette.Length > 100)
                throw new GuardException(ErrorCodes.Validation, "Geçersiz tema.");

            var settings = r.Guard.Settings;
            if (settings.Get(SettingKeys.UiThemeSkin) == skin && settings.Get(SettingKeys.UiThemePalette, "") == palette)
                return PipeResponse.Ok();

            await settings.SetAsync(SettingKeys.UiThemeSkin, skin).ConfigureAwait(false);
            await settings.SetAsync(SettingKeys.UiThemePalette, palette).ConfigureAwait(false);
            return PipeResponse.Ok();
        }

        private static async Task<PipeResponse> CreateAdmin(RequestContext r)
        {
            var req = r.Payload<CreateAdminRequest>();
            await r.Guard.Admin.CreateAsync(req.UserName, req.Password).ConfigureAwait(false);
            return PipeResponse.Ok();
        }

        private static async Task<PipeResponse> Login(RequestContext r)
        {
            var req = r.Payload<LoginRequest>();
            string userName = await r.Guard.Admin.LoginAsync(req.UserName, req.Password).ConfigureAwait(false);
            int idleMinutes = GeneralSettings.GetInt(r.Guard.Settings, SettingKeys.AuthSessionIdleMinutes);
            var session = r.Sessions.Create(userName, TimeSpan.FromMinutes(idleMinutes));

            return PipeResponse.Ok(new LoginResult
            {
                Token = session.Token,
                UserName = session.UserName,
                IdleTimeoutMinutes = (int)session.IdleTimeout.TotalMinutes
            });
        }

        private static Task<PipeResponse> Logout(RequestContext r)
        {
            r.Sessions.Remove(r.Session.Token);
            AuditTrail.Record(r.Guard.Database, r.Session.UserName, "Çıkış yapıldı");
            return Task.FromResult(PipeResponse.Ok());
        }
    }
}
