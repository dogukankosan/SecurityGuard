using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

// Dış IP'den gerçek saldırı ile otomatik ban (yarı otomatik: saldırı başka makineden tools\autotest\SgSaldiri.ps1 ile yapılır).
// İç ağ adresleri (10.x, 172.16.x, 192.168.x) asla banlanmadığı için saldıran makinenin iç ağ dışı bir adresi olmalı:
// VM'e host-only ağ kartı eklenir, iki tarafa 203.0.113.0/24 (TEST-NET-3, internete çıkmaz) adresi verilir.
static partial class SgAutoTest
{
    static void ExternalBanTests(string[] args)
    {
        Title("DIŞ IP'DEN GERÇEK SALDIRI VE OTOMATİK BAN");
        int at = Array.IndexOf(args, "-disban");
        string ip = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("-") ? args[at + 1] : "203.0.113.1";
        IPAddress attacker;
        if (!IPAddress.TryParse(ip, out attacker)) { Fail("Geçersiz saldırgan IP'si: " + ip); return; }

        var own = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(u.Address))
            .Select(u => u.Address.ToString()).ToList();
        Info("Bu sunucunun adresleri: " + string.Join(", ", own) + " | beklenen saldırgan: " + ip);
        // Lab kurulumunda (host-only) sunucu saldırganla aynı /24'te olur; gerçek internet testinde farklı ağdan gelir. İkisi de geçerli, sadece bilgi.
        Info(own.Any(o => SameSubnet24(o, ip)) ? "Saldırgan sunucuyla aynı /24 ağda (lab kurulumu)." : "Saldırgan sunucunun dışındaki bir ağdan (gerçek internet testi).");
        if ((Environment.GetEnvironmentVariable("SESSIONNAME") ?? "").StartsWith("RDP", StringComparison.OrdinalIgnoreCase))
            Info("UYARI: Bu pencere RDP oturumunda. Saldırgan IP tüm portlardan engellenecek; bu sunucuya " + ip + " adresinden RDP ile bağlıysanız ban anında bağlantınız KOPAR. Konsoldan / KVM'den çalıştırın.");

        // Temiz başlangıç: önceki denemeden kalan ban / sayaç
        var list = Send<BanListResult>(Commands.BanList, null);
        if (list.Banned.Any(b => b.IpAddress == ip) || list.Attempts.Any(a => a.IpAddress == ip))
        {
            Raw(Commands.BanRemove, new IpListRequest { IpAddresses = new List<string> { ip }, ResetCounter = true });
            Info("Önceki denemeden kalan ban / sayaç temizlendi.");
            list = Send<BanListResult>(Commands.BanList, null);
        }
        Check(!list.Banned.Any(b => b.IpAddress == ip), "Başlangıçta " + ip + " banlı değil");

        long before = LastId();
        var started = DateTime.UtcNow;
        Prompt("ŞİMDİ: kendi PC'nizden (" + ip + ") bu sunucuya açık servise (RDP 3389) yanlış kullanıcı adı/şifreyle " + "en az eşik kadar (~10-12) başarısız giriş yapın. Bu pencere banı bekliyor (en fazla 10 dk).");

        // 1) Denemeler sayılıyor mu, eşikte ban geliyor mu
        BannedIpDto ban = null;
        int lastShown = -1, threshold = 0;
        string protocol = null;
        var end = DateTime.Now.AddMinutes(10);
        while (DateTime.Now < end)
        {
            list = Send<BanListResult>(Commands.BanList, null);
            var mine = list.Attempts.Where(a => a.IpAddress == ip).ToList();
            int count = mine.Sum(a => a.FailCount);
            if (count != lastShown && mine.Count > 0)
            {
                lastShown = count;
                threshold = mine.Max(a => a.Threshold);
                protocol = string.Join("+", mine.Select(a => a.Protocol));
                Info("  deneme sayacı: " + string.Join(", ", mine.Select(a => a.Protocol + " " + a.FailCount + "/" + a.Threshold + (a.IsProtected ? " (KORUMALI: banlanmaz)" : ""))));
            }
            ban = list.Banned.FirstOrDefault(b => b.IpAddress == ip);
            if (ban != null) break;
            Thread.Sleep(1000);
        }

        Check(lastShown > 0, "Saldırganın hatalı girişleri sayıldı (" + (protocol ?? "-") + ", " + Math.Max(lastShown, 0) + " deneme)");
        if (lastShown <= 0)
        {
            Info("Hiç deneme görülmedi. Bakılacaklar: betik doğru adrese mi gidiyor, VM firewall'ı 445 / 1433'e izin veriyor mu, saldırgan gerçekten " + ip + " adresinden mi çıkıyor.");
            var seen = Send<BanListResult>(Commands.BanList, null).Attempts.Where(a => ParseUtc(a.LastUtc) >= started.AddSeconds(-5)).ToList();
            foreach (var a in seen) Info("  bu sürede deneme yapan: " + a.IpAddress + " " + a.Protocol + " " + a.FailCount + (a.IsProtected ? " (korumalı)" : ""));
            return;
        }
        Check(ban != null, "Eşik aşılınca IP kendiliğinden banlandı" + (ban == null ? " (10 dk içinde ban gelmedi; eşik " + threshold + ")" : ": " + Short(ban.Reason)));
        if (ban == null) return;

        Check(!ban.IsManual, "Ban otomatik (elle değil), protokol: " + (ban.Protocol ?? "-"));
        Check(threshold > 0 && ban.FailCount >= threshold, "Ban eşikte geldi (" + ban.FailCount + " deneme, eşik " + threshold + ")");
        Check(ban.FailCount <= threshold + 5, "Ban gecikmedi: eşikten sonra en fazla birkaç deneme geçti (" + ban.FailCount + " / " + threshold + ")");

        // 2) Firewall kuralı
        var rule = string.IsNullOrEmpty(ban.RuleName) ? null : FwRule(ban.RuleName);
        Check(rule != null && rule.Action == 0 && (rule.Remote ?? "").Contains(ip), "Firewall'da engelleme kuralı var (" + (ban.RuleName ?? "?") + ", tüm portlar)");
        list = Send<BanListResult>(Commands.BanSyncFirewall, null);
        Check(list.FirewallOk, "Firewall Kontrol: tutarlı" + (list.FirewallOk ? "" : " (" + list.FirewallMessage + ")"));

        // 3) Olay + mail
        var ev = WaitEvent("Ban.IpBanned", before, 20);
        Check(ev != null && ev.IpAddress == ip, "'IP banlandı' kritik olayı" + MailText(ev) + (ev == null ? "" : ": " + Short(ev.Summary)));
        var fails = FailedFrom(ip, before);
        Info("Bu IP'den kaydedilen hatalı giriş olayı: " + fails.Count + (fails.Count > 0 ? " (" + string.Join(", ", fails.GroupBy(f => f.EventType).Select(g => g.Key + " " + g.Count())) + ")" : ""));
        if (fails.Count > 0) Info("Denenen kullanıcı adları: " + string.Join(", ", fails.Select(f => f.UserName).Where(u => !string.IsNullOrEmpty(u)).Distinct().Take(8)));

        // 4) Ban gerçekten kesiyor mu: betik denemeye devam ediyor, yeni hatalı giriş gelmemeli
        Thread.Sleep(5000); // bandan hemen önce yola çıkmış denemeler
        long afterBan = LastId();
        Info("Ban sonrası 30 sn izleniyor (betik denemeye devam ediyor; sunucuya ulaşamamalı)...");
        Thread.Sleep(30000);
        int leaked = FailedFrom(ip, afterBan).Count;
        Check(leaked == 0, "Bandan sonra saldırgandan tek bir deneme bile sunucuya ulaşmadı (" + leaked + ")");
        Info("Saldıran makinede betik bu sırada 'ULAŞILAMIYOR' yazıyor olmalı (gözle bakın).");

        // 5) Ban hâlâ yerinde
        list = Send<BanListResult>(Commands.BanList, null);
        Check(list.Banned.Any(b => b.IpAddress == ip), "Ban listede duruyor");

        // 6) Ban kaldırılınca erişim geri geliyor mu
        Raw(Commands.BanRemove, new IpListRequest { IpAddresses = new List<string> { ip }, ResetCounter = true });
        rule = string.IsNullOrEmpty(ban.RuleName) ? null : FwRule(ban.RuleName);
        Check(rule == null || !(rule.Remote ?? "").Contains(ip), "Ban kaldırıldı: IP firewall kuralından çıktı");
        long afterUnban = LastId();
        Info("Ban kaldırıldı; betik hâlâ çalışıyorsa denemeleri tekrar sunucuya ulaşmalı (en fazla 60 sn bekleniyor)...");
        bool back = WaitUntil(() => FailedFrom(ip, afterUnban).Count > 0, 60, true);
        if (back) Check(true, "Ban kalkınca saldırganın denemeleri tekrar sunucuya ulaştı (engel gerçekten kalktı)");
        else Info("Ban kalktıktan sonra yeni deneme görülmedi (betik bitmiş olabilir): bu adım atlandı.");

        // Temizlik: betik sürüyorsa yeniden banlanmış olabilir
        Prompt("Saldırı betiğini durdurun (Ctrl+C). 10 sn sonra temizlik yapılacak.");
        Thread.Sleep(10000);
        Raw(Commands.BanRemove, new IpListRequest { IpAddresses = new List<string> { ip }, ResetCounter = true });
        list = Send<BanListResult>(Commands.BanList, null);
        Check(!list.Banned.Any(b => b.IpAddress == ip), "Temizlik: test banı kaldırıldı, sayaç sıfırlandı");
    }

    static List<EventLogRow> FailedFrom(string ip, long afterId)
    {
        var res = Send<EventLogResult>(Commands.EventLogQuery, new EventLogQuery
        {
            FromUtc = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            MinSeverity = -1,
            Search = ip,
            MaxRows = 1000
        });
        return res.Rows.Where(r => r.Id > afterId && r.IpAddress == ip && (r.EventType ?? "").EndsWith(".LoginFailed", StringComparison.Ordinal)).OrderBy(r => r.Id).ToList();
    }

    static bool SameSubnet24(string a, string b)
    {
        int i = a.LastIndexOf('.'), j = b.LastIndexOf('.');
        return i > 0 && j > 0 && a.Substring(0, i) == b.Substring(0, j);
    }

    static DateTime ParseUtc(string s)
    {
        DateTime d;
        return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out d) ? d : DateTime.MinValue;
    }
}
