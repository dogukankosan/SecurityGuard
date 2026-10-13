using System;
using System.Linq;
using System.ServiceProcess;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Sql;

namespace SecurityGuardService.Checks.Security
{
    /// <summary>Güvenlik Önerileri taraması: kayıtlı (izlemesi açık) her SQL Server instance'ı + Windows / RDP. Sadece okur.</summary>
    internal static class SecurityScan
    {
        public const string DisplayName = "Güvenlik Önerileri";

        public static void Run(CheckContext ctx, SqlConnectionStore sql, SqlAuditManager audit)
        {
            var all = sql.List();
            var instances = all.Where(i => i.IsConfigured && i.IsEnabled).Select(i => i.InstanceName).ToList();
            if (instances.Count == 0)
                ctx.Add(CheckContext.Item("sec.sql.none", "SQL Server", "SQL Server bağlantısı", all.Any(i => i.Detected) ? CheckStatus.Uyari : CheckStatus.Bilgi,
                    all.Any(i => i.Detected)
                        ? "Sunucuda SQL Server var ama SecurityGuard'a bağlantısı tanımlı değil: SQL güvenliği izlenmiyor ve kontrol edilemedi. SQL Bağlantısı sayfasından tanımlayın."
                        : "Sunucuda SQL Server bulunamadı."));

            foreach (var instance in instances)
            {
                ctx.Token.ThrowIfCancellationRequested();
                string cs = sql.GetConnectionString(instance);
                if (cs == null) continue;
                SqlSecurityChecks.Run(ctx, instance, cs, name =>
                {
                    var state = audit.GetState(name);
                    return state.Running ? null : (state.Message ?? "SQL izlemesi kurulu değil.");
                });
            }

            if (all.Any(i => i.Detected))
                ctx.Run("sec.sql.browser", "SQL Server", "SQL Server Browser servisi", CheckBrowser);

            WindowsSecurityChecks.Run(ctx);
        }

        private static CheckItemDto CheckBrowser()
        {
            try
            {
                using (var sc = new ServiceController("SQLBrowser"))
                {
                    if (sc.Status != ServiceControllerStatus.Running) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Çalışmıyor." };
                    return new CheckItemDto
                    {
                        Status = CheckStatus.Uyari,
                        Summary = "SQL Server Browser çalışıyor (UDP 1434).",
                        Detail = "Browser servisi ağdaki herkese sunucudaki SQL instance'larını ve portlarını söyler; saldırganın keşfini kolaylaştırır ve "
                                 + "eski DDoS yansıtma saldırılarında kullanılır. İstemciler instance adı yerine port ile bağlanıyorsa gerekmez.",
                        HowToFix = "İsimli instance'a (SUNUCU\\INSTANCE) port belirtmeden bağlanan istemci varsa önce bağlantılarına portu ekleyin. Sonra servisi durdurup devre dışı bırakın.",
                        Script = "Stop-Service SQLBrowser\r\nSet-Service SQLBrowser -StartupType Disabled",
                        ScriptLanguage = "powershell"
                    };
                }
            }
            catch (InvalidOperationException)
            {
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Kurulu değil." };
            }
        }
    }
}
