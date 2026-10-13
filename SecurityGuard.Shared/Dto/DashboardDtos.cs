using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Panel (ana sayfa) sayfası: tek bakışta durum.</summary>
    public sealed class DashboardDto
    {
        public string MachineName { get; set; }
        public string Version { get; set; }
        public string ServiceStartedUtc { get; set; }
        public List<DashboardTileDto> Tiles { get; set; }
        public List<DashboardEventDto> RecentEvents { get; set; }
        public List<DashboardTodoDto> Todos { get; set; }
    }

    /// <summary>Durum kutucuğu. Level: 0 iyi, 1 bilgi, 2 uyarı, 3 kritik.</summary>
    public sealed class DashboardTileDto
    {
        public string Title { get; set; }
        public string Value { get; set; }
        public string Detail { get; set; }
        public int Level { get; set; }

        /// <summary>Tıklanınca açılacak sayfa (<see cref="DashboardPages"/>).</summary>
        public string Page { get; set; }

        /// <summary>Küçük eğilim grafiği (ör. son 24 saatin işlemci yüzdesi); yoksa null.</summary>
        public List<double> Trend { get; set; }
    }

    public sealed class DashboardEventDto
    {
        public string TimeUtc { get; set; }
        public int Severity { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>Yapılması önerilen iş. Level: 2 uyarı, 3 kritik.</summary>
    public sealed class DashboardTodoDto
    {
        public int Level { get; set; }
        public string Text { get; set; }
        public string Page { get; set; }
    }

    /// <summary>Panelden açılabilen sayfalar.</summary>
    public static class DashboardPages
    {
        public const string EventLog = "eventlog";
        public const string BanList = "banlist";
        public const string Security = "security";
        public const string Performance = "performance";
        public const string Backup = "backup";
        public const string Maintenance = "maintenance";
        public const string Mail = "mail";
        public const string SqlConnection = "sql";
        public const string Reports = "reports";
        public const string Settings = "settings";
        public const string PortRestrictions = "portrestrictions";
        public const string License = "license";
    }
}
