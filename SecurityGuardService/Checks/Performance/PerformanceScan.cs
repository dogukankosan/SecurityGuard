using System.Linq;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Sql;

namespace SecurityGuardService.Checks.Performance
{
    /// <summary>Performans Önerileri taraması: kayıtlı (izlemesi açık) her SQL Server instance'ı + Windows.</summary>
    internal static class PerformanceScan
    {
        public const string DisplayName = "Performans Önerileri";

        public static void Run(CheckContext ctx, SqlConnectionStore sql)
        {
            var instances = sql.List().Where(i => i.IsConfigured && i.IsEnabled).Select(i => i.InstanceName).ToList();
            if (instances.Count == 0)
                ctx.Add(CheckContext.Item("perf.sql.none", "SQL Server", "SQL Server bağlantısı", CheckStatus.Bilgi,
                    "SQL Server bağlantısı tanımlı değil (veya izleme kapalı): SQL önerileri yapılamadı. SQL Bağlantısı sayfasından tanımlayın."));

            // SQL önce: veri klasörleri antivirüs kontrolünde kullanılıyor
            foreach (var instance in instances)
            {
                ctx.Token.ThrowIfCancellationRequested();
                string cs = sql.GetConnectionString(instance);
                if (cs != null) SqlPerformanceChecks.Run(ctx, instance, cs);
            }

            WindowsPerformanceChecks.Run(ctx);
        }
    }
}
