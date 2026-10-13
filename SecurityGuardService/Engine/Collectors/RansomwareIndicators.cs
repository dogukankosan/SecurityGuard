using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SecurityGuardService.Engine.Collectors
{
    /// <summary>
    /// Fidye yazılımlarının şifrelemeden hemen önce çalıştırdığı komutlar (Security 4688, işlem oluşturma).
    /// Gölge kopyaları / yedek kataloğunu silme, Windows kurtarmayı kapatma, logları temizleme, Defender'ı kapatma.
    /// Bunlar normal işletimde neredeyse hiç çalıştırılmaz: görülünce şifreleme dakikalar içinde başlayabilir.
    /// </summary>
    internal static class RansomwareIndicators
    {
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>Komut tehlikeliyse açıklaması, değilse null.</summary>
        /// <param name="processPath">NewProcessName (tam yol).</param>
        /// <param name="commandLine">CommandLine (komut satırı kaydı kapalıysa boş gelir).</param>
        public static string Match(string processPath, string commandLine)
        {
            string exe = (Path.GetFileName(processPath ?? "") ?? "").ToLowerInvariant();
            string cmd = Spaces.Replace((commandLine ?? "").ToLowerInvariant().Replace("\"", " "), " ");
            Func<string, bool> has = s => cmd.IndexOf(s, StringComparison.Ordinal) >= 0;

            switch (exe)
            {
                case "vssadmin.exe":
                    if (has("delete shadows") || has("resize shadowstorage")) return "Gölge kopyalar (Volume Shadow Copy) siliniyor";
                    break;
                case "diskshadow.exe":
                    if (has("delete shadows")) return "Gölge kopyalar (Volume Shadow Copy) siliniyor";
                    break;
                case "wmic.exe":
                    if (has("shadowcopy") && has("delete")) return "Gölge kopyalar (Volume Shadow Copy) siliniyor";
                    break;
                case "wbadmin.exe":
                    if (has("delete catalog") || has("delete systemstatebackup") || has("delete backup")) return "Windows yedekleri / yedek kataloğu siliniyor";
                    break;
                case "bcdedit.exe":
                    if (has("recoveryenabled no") || has("ignoreallfailures")) return "Windows kurtarma (otomatik onarım) kapatılıyor";
                    break;
                case "wevtutil.exe":
                    if (has(" cl ") || has(" clear-log") || cmd.EndsWith(" cl")) return "Windows olay logu temizleniyor";
                    break;
                case "cipher.exe":
                    if (has(" /w")) return "Boş disk alanı siliniyor (silinen dosyaların kurtarılması engelleniyor)";
                    break;
                case "fsutil.exe":
                    if (has("usn deletejournal")) return "Dosya değişiklik günlüğü (USN) siliniyor";
                    break;
                case "powershell.exe":
                case "pwsh.exe":
                    if (has("win32_shadowcopy") && (has("delete") || has("remove-"))) return "Gölge kopyalar PowerShell ile siliniyor";
                    if (has("set-mppreference") && (has("disablerealtimemonitoring") || has("disableioavprotection") || has("disablebehaviormonitoring")))
                        return "Defender koruması PowerShell ile kapatılıyor";
                    if (has("add-mppreference") && has("exclusion")) return "Defender'a PowerShell ile dışlama ekleniyor";
                    break;
            }
            return null;
        }
    }
}
