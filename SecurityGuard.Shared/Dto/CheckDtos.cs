using System.Collections.Generic;
using SecurityGuard.Shared.Enums;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Tarama türleri (Performans Önerileri, Güvenlik Önerileri).</summary>
    public static class CheckScopes
    {
        public const string Performance = "performance";
        public const string Security = "security";
    }

    /// <summary>Taramanın bir maddesi: sonuç + neden + nasıl düzeltilir (+ hazır script).</summary>
    public sealed class CheckItemDto
    {
        /// <summary>Kalıcı kimlik (görmezden gelme bununla tutulur), ör. "perf.sql.maxmemory@MSSQLSERVER".</summary>
        public string CheckId { get; set; }

        /// <summary>Nerede: "Windows", "SQL Server (MSSQLSERVER)"...</summary>
        public string Area { get; set; }

        public string Title { get; set; }
        public CheckStatus Status { get; set; }

        /// <summary>Tek satır sonuç (grid'de görünen).</summary>
        public string Summary { get; set; }

        /// <summary>Ayrıntı: ölçülen değerler, listeler, neden önemli.</summary>
        public string Detail { get; set; }

        /// <summary>Adım adım nasıl düzeltilir. SecurityGuard kendisi uygulamaz.</summary>
        public string HowToFix { get; set; }

        /// <summary>Hazır script (SQL / PowerShell); yoksa null. Kullanıcı kopyalayıp kendisi çalıştırır.</summary>
        public string Script { get; set; }

        /// <summary>"sql" veya "powershell" (script editörünün renklendirmesi için).</summary>
        public string ScriptLanguage { get; set; }

        public bool IsIgnored { get; set; }
        public string IgnoreNote { get; set; }

        /// <summary>Bir önceki taramada bu durumda değildi (yeni çıkan veya kötüleşen bulgu).</summary>
        public bool IsNew { get; set; }
    }

    /// <summary>Son tarama ve durumu.</summary>
    public sealed class CheckRunDto
    {
        public string Scope { get; set; }

        /// <summary>Hiç tarama yapılmadıysa null.</summary>
        public string RunId { get; set; }

        /// <summary>"zamanlanmış", "elle (kullanıcı)".</summary>
        public string Trigger { get; set; }

        public string StartedUtc { get; set; }
        public string FinishedUtc { get; set; }

        public List<CheckItemDto> Items { get; set; }

        /// <summary>Şu an tarama sürüyor mu (sürüyorsa Items bir önceki taramadır).</summary>
        public bool IsRunning { get; set; }

        /// <summary>Sürüyorsa hangi adımda.</summary>
        public string Progress { get; set; }

        /// <summary>Bir sonraki otomatik tarama (UTC).</summary>
        public string NextRunUtc { get; set; }
    }

    public sealed class CheckScopeRequest
    {
        public string Scope { get; set; }
    }

    public sealed class CheckIgnoreRequest
    {
        public string Scope { get; set; }
        public string CheckId { get; set; }

        /// <summary>true = görmezden gel (not ile), false = görmezden gelmeyi kaldır.</summary>
        public bool Ignore { get; set; }

        public string Note { get; set; }
    }
}
