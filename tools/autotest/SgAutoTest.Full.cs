using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Ipc;

// "-tam": sıfırdan uçtan uca test (SADECE TEST VM'İNDE, snapshot alındıktan sonra).
// Sunucuda gerçek değişiklik yapar ve geri alır: test kullanıcısı, zamanlanmış görev, firewall kuralı / profil aç-kapa,
// audit politikası, RDP NLA ayarı, olay logu temizleme, SQL login / sysadmin / xp_cmdshell / veritabanı, servis durdur-başlat, servisi öldürme.
// Bir bölüm hata verse de diğerleri çalışır; en sonda servis logundaki hatalar ve mail kuyruğu dökülür.
partial class SgAutoTest
{
    const string KeepIp = "203.0.113.77";       // test boyunca banlı kalır: lisans kapanınca / servis yeniden başlayınca yerinde mi
    const string TestUser = "sgat_test";
    const string SqlTestLogin = "sgat_login";
    const string SqlTestDb = "SgAutoTestDb";
    const string FwTestRule = "SgAutoTestRule";
    const string TaskName = "SgAutoTestTask";

    static int warn;
    static DateTime fullStartUtc;
    static long fullStartId;
    static string adminKey;
    static readonly List<string> sectionTimes = new List<string>();
    static readonly string[] SectionKeys = { "yetki", "ayar", "ban", "lisans", "windows", "sql", "port", "tuzak", "ag", "yazilim", "servis", "tarama", "rapor", "bakim", "geriyukle", "gercekbakim", "zamanlama", "restart", "koruma" };
    static HashSet<string> only;

    static bool Want(string key) { return only == null || only.Contains(key); }
    static readonly List<string> resourceLines = new List<string>();

    static void FullTests(string[] args)
    {
        fullStartUtc = DateTime.UtcNow;
        Line("TAM TEST başladı: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + " | makine " + Environment.MachineName + " | " + Environment.OSVersion.VersionString, ConsoleColor.Cyan);
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Fail("Bu test YÖNETİCİ olarak açılmış komut satırından çalıştırılmalı.");
            return;
        }
        // "-sadece sql,port": sadece o bölümler (ORTAM her zaman çalışır). Bir hatayı düzelttikten sonra tüm testi beklememek için.
        int at = Array.IndexOf(args, "-sadece");
        only = at >= 0 && at + 1 < args.Length
            ? new HashSet<string>(args[at + 1].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLowerInvariant()))
            : null;
        if (only != null)
        {
            var unknown = only.Where(o => !SectionKeys.Contains(o)).ToList();
            if (unknown.Count > 0)
            {
                Fail("Bilinmeyen bölüm: " + string.Join(", ", unknown) + ". Geçerli bölümler: " + string.Join(", ", SectionKeys));
                return;
            }
            // Lisans ve yeniden başlatma bölümleri test banının (203.0.113.77) yerinde durduğunu kontrol eder: ban bölümü de gerekir
            if (only.Contains("lisans") || only.Contains("restart")) only.Add("ban");
            Line("Sadece şu bölümler çalışacak: " + string.Join(", ", SectionKeys.Where(only.Contains)), ConsoleColor.Yellow);
        }

        if (Want("lisans"))
        {
            Console.Write("Lisans API yönetici anahtarı (lisans iptal / geri açma testi için; boş bırakılırsa o bölüm atlanır): ");
            adminKey = ReadPassword().Trim();
        }

        if (!Section("ORTAM", Environment_)) return; // lisans yoksa / servis hazır değilse devamı anlamsız
        fullStartId = LastId();

        if (Want("yetki")) Section("YETKİ VE OTURUM", AuthTests);
        if (Want("ayar")) Section("GENEL AYARLAR", SettingsTests);
        if (Want("ban")) Section("BAN MOTORU VE BEYAZ LİSTE", BanTests);
        if (Want("lisans")) Section("LİSANS", LicenseTests);
        if (Want("windows")) Section("WINDOWS OLAYLARI", WindowsEventTests);
        if (Want("sql")) Section("SQL SERVER", SqlTests);
        if (Want("port")) Section("PORT KISITLARI", () => PortTests(!args.Contains("-uzun")));
        if (Want("tuzak")) Section("TUZAK DOSYALAR", CanaryTests);
        if (Want("ag")) Section("AĞ GİRİŞİ VE AÇIK PORTLAR", NetworkTests);
        if (Want("yazilim")) Section("PROGRAM KURULUMU", SoftwareTests);
        if (Want("servis")) Section("SERVİS TAKİBİ", ServiceTests);
        if (Want("tarama")) Section("TARAMALAR", ScanTests);
        if (Want("rapor")) Section("RAPORLAR VE PANEL", ReportTests);
        if (Want("bakim")) Section("SQL BAKIM VE YEDEK", MaintenanceBackupTests);
        if (Want("geriyukle")) Section("GERİ YÜKLEME VE YEDEK HATALARI", RestoreTests);
        if (Want("gercekbakim")) Section("GERÇEK SQL BAKIMI", RealMaintenanceTests);
        if (Want("zamanlama")) Section("ZAMANLAMA", ScheduleTests);
        if (Want("restart")) Section("SERVİSİ DURDUR / BAŞLAT", RestartTests);
        if (Want("koruma")) Section("SERVİS KORUMASI", ProtectionTests);
        Section("SON KONTROLLER", FinalChecks); // her zaman: temizlik, servis logu, mail kuyruğu

        Line("", ConsoleColor.Gray);
        Line("Bölüm süreleri: " + string.Join(" | ", sectionTimes), ConsoleColor.Gray);
        Line("Servisin kaynak kullanımı (her bölümün sonunda; PID değişirse servis yeniden başlamıştır):", ConsoleColor.Gray);
        foreach (string l in resourceLines) Line(l, ConsoleColor.Gray);
        Line("Uyarı (hata sayılmayan, bakılması gereken): " + warn, warn == 0 ? ConsoleColor.Gray : ConsoleColor.Yellow);
    }

    /// <returns>Bölüm beklenmeyen hata vermeden bittiyse true.</returns>
    static bool Section(string name, Action body)
    {
        var sw = Stopwatch.StartNew();
        int failBefore = fail;
        bool ok = true;
        try
        {
            body();
        }
        catch (AbortException ex)
        {
            Fail(name + ": " + ex.Message);
            ok = false;
        }
        catch (Exception ex)
        {
            var inner = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
            Fail(name + " bölümü yarıda kaldı: " + inner.GetType().Name + ": " + inner.Message);
            log.WriteLine(inner.ToString());
            ok = false;
            // Servis yeniden başladıysa oturum düşmüş olabilir
            try { if (!client.PingAsync().Result.Success) Relogin(60); } catch { }
        }
        sectionTimes.Add(name + " " + (int)sw.Elapsed.TotalSeconds + " sn" + (fail > failBefore ? " (" + (fail - failBefore) + " HATA)" : ""));
        // Bölüm sonunda servisin kaynak kullanımı: bellek / işlemci artışı hangi bölümde oluyor görülsün
        try
        {
            var info = client.SendAsync<ServiceInfoDto>(Commands.ServiceInfo).Result;
            if (info.Success)
            {
                var snap = ProcSnap.Take(info.Value.ProcessId);
                string line = string.Format("  {0,-28} {1,4} sn | PID {2,-6} bellek {3,4} MB (özel {4,4}) | handle {5,5} | iş parçacığı {6,3} | CPU {7,7} sn",
                    name, (int)sw.Elapsed.TotalSeconds, snap.Pid, snap.WorkingMb, snap.PrivateMb, snap.Handles, snap.Threads, snap.CpuSec.ToString("0.0", CultureInfo.InvariantCulture));
                resourceLines.Add(line);
            }
        }
        catch { }
        return ok;
    }

    sealed class AbortException : Exception { public AbortException(string m) : base(m) { } }

    static void Warn(string text) { warn++; Line("[UYARI] " + text, ConsoleColor.Yellow); }
    static void Info(string text) { Line("  " + text, ConsoleColor.Gray); }

    // ================================================================ Ortam

    static ProcSnap startSnap;

    static void Environment_()
    {
        Title("ORTAM");
        var status = client.GetStatusAsync().Result;
        if (!status.Success) throw new AbortException("Servis durumu alınamadı: " + status.ErrorMessage);
        Info("Servis sürümü " + status.Value.ServiceVersion + " | hazır: " + status.Value.Ready + " | lisanslı: " + status.Value.Licensed);

        var lic = Send<LicenseInfoDto>(Commands.LicenseGet, null);
        Info("Lisans: " + lic.StatusText + " | müşteri " + (lic.Customer ?? "-") + " | bitiş " + (lic.Expires ?? "-") + " (" + lic.DaysLeft + " gün) | son kontrol " + (lic.LastCheck ?? "-")
             + " | sunucu " + lic.ApiUrl + (lic.LastError != null ? " | SON HATA: " + lic.LastError : ""));
        Info("Donanım kimliği: " + lic.HwId + (lic.HardwareChanged ? " (donanım değişikliği algılandı)" : ""));
        Check(LicenseProtocolOk(lic.HwId), "Donanım kimliği biçimi doğru");
        if (!lic.Active)
            throw new AbortException("Lisans geçerli değil (" + lic.StatusText + "): koruma kapalıyken olay kaydedilmez, test yapılamaz. Önce bu HWID'ye lisans açın.");
        Check(true, "Lisans geçerli, koruma açık");

        var info = Send<ServiceInfoDto>(Commands.ServiceInfo, null);
        Info("PID " + info.ProcessId + " | başlangıç " + info.StartedUtc + " | şema v" + info.SchemaVersion + " | veritabanı " + (info.DatabaseSizeBytes / 1024 / 1024) + " MB (" + info.DatabaseFile + ")");
        Info("Koruma: " + info.Protection + " | bekleyen mail: " + info.MailPending);
        Check(info.IsWindowsService, "Windows servisi olarak çalışıyor");
        Check(info.SchemaVersion >= 17, "Veritabanı şeması güncel (v" + info.SchemaVersion + ")");
        Check(string.Equals(info.Version, status.Value.ServiceVersion, StringComparison.Ordinal), "Sürüm bilgisi tutarlı");

        var setup = Send<SetupStatusDto>(Commands.SetupStatus, null);
        Info("Kurulum: tamamlandı=" + setup.Completed + " | SQL bulundu=" + setup.SqlDetected + " bağlı=" + setup.SqlConnected + " audit=" + setup.SqlAuditInstalled
             + " | mail ayarlı=" + setup.MailConfigured + " alıcı=" + setup.ActiveRecipients + " | beyaz liste=" + setup.WhitelistEntries);
        Check(setup.Completed, "İlk kurulum tamamlanmış");
        if (!setup.MailConfigured || setup.ActiveRecipients == 0) Warn("Mail ayarlı değil / alıcı yok: mail gönderimi doğrulanamayacak.");
        if (!setup.SqlConnected) Warn("SQL bağlantısı yok: SQL bölümleri atlanacak.");
        if (setup.SqlMissingInstances != null && setup.SqlMissingInstances.Count > 0) Warn("Bağlantısı eksik SQL instance: " + string.Join(", ", setup.SqlMissingInstances));

        startSnap = ProcSnap.Take(info.ProcessId);
        Info("Servis işlemi: " + startSnap);
    }

    static bool LicenseProtocolOk(string hwId)
    {
        return hwId != null && Regex.IsMatch(hwId, "^SG1-[0-9A-F]{8}-[0-9A-F]{8}-[0-9A-F]{8}-[0-9A-F]{8}$");
    }

    sealed class ProcSnap
    {
        public int Pid, Handles, Threads; public long WorkingMb, PrivateMb; public double CpuSec; public DateTime At = DateTime.UtcNow;

        public static ProcSnap Take(int pid)
        {
            var s = new ProcSnap { Pid = pid };
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    s.WorkingMb = p.WorkingSet64 / 1024 / 1024;
                    s.PrivateMb = p.PrivateMemorySize64 / 1024 / 1024;
                    s.Handles = p.HandleCount;
                    s.Threads = p.Threads.Count;
                    try { s.CpuSec = p.TotalProcessorTime.TotalSeconds; } catch { s.CpuSec = -1; }
                }
            }
            catch (Exception ex) { s.Pid = -1; Console.WriteLine("  (işlem bilgisi okunamadı: " + ex.Message + ")"); }
            return s;
        }

        public override string ToString()
        {
            return "PID " + Pid + ", bellek " + WorkingMb + " MB (özel " + PrivateMb + " MB), " + Handles + " handle, " + Threads + " iş parçacığı" + (CpuSec >= 0 ? ", CPU " + CpuSec.ToString("0.0", CultureInfo.InvariantCulture) + " sn" : "");
        }
    }

    // ================================================================ Yetki / oturum

    static void AuthTests()
    {
        Title("YETKİ VE OTURUM");
        var anon = new GuardClient();
        var r = anon.SendAsync<DashboardDto>(Commands.DashboardGet).Result;
        Check(!r.Success && r.IsUnauthorized, "Giriş yapmadan komut reddediliyor (" + r.ErrorCode + ")");
        var lic = anon.SendAsync<LicenseInfoDto>(Commands.LicenseGet).Result;
        Check(!lic.Success && lic.IsUnauthorized, "Giriş yapmadan lisans bilgisi de verilmiyor");
        var st = anon.GetStatusAsync().Result;
        Check(st.Success && st.Value.Ready, "Durum bilgisi (sürüm / hazır) giriş gerektirmiyor");

        var bad = anon.LoginAsync(loginUser, "yanlis-" + Guid.NewGuid().ToString("N")).Result;
        Check(!bad.Success && bad.ErrorCode == ErrorCodes.LoginFailed, "Yanlış şifre reddedildi (" + bad.ErrorCode + ": " + Short(bad.ErrorMessage) + ")");
        var nouser = anon.LoginAsync("olmayan_" + Guid.NewGuid().ToString("N").Substring(0, 6), "x").Result;
        Check(!nouser.Success, "Olmayan kullanıcı reddedildi (" + nouser.ErrorCode + ")");
        Check(bad.ErrorMessage == nouser.ErrorMessage || nouser.ErrorCode == bad.ErrorCode, "Yanlış şifre ile olmayan kullanıcı aynı cevabı alıyor (kullanıcı adı sızdırılmıyor)");

        var good = anon.LoginAsync(loginUser, loginPassword).Result;
        Check(good.Success, "Doğru şifreyle ikinci oturum açıldı (hatalı deneme sayacı sıfırlanır)");
        Check(anon.PingAsync().Result.Success, "İkinci oturum çalışıyor");
        anon.LogoutAsync().Wait();
        Check(!anon.IsLoggedIn, "Çıkış yapıldı");

        var unknown = client.SendAsync("Yok.BoyleKomut").Result;
        Check(!unknown.Success && unknown.ErrorCode == ErrorCodes.UnknownCommand, "Bilinmeyen komut reddedildi");
        var broken = client.SendAsync(Commands.BanAdd, "bu bir nesne değil").Result;
        Check(!broken.Success, "Bozuk istek verisi reddedildi (" + broken.ErrorCode + "), servis ayakta");
        Check(client.PingAsync().Result.Success, "Ana oturum hâlâ geçerli");
    }

    // ================================================================ Genel ayarlar

    static void SettingsTests()
    {
        Title("GENEL AYARLAR");
        var list = Send<List<GeneralSettingDto>>(Commands.GeneralSettingsList, null);
        Check(list.Count >= 15, list.Count + " ayar listelendi");
        foreach (var g in list.GroupBy(s => s.Group))
            Info(g.Key + ": " + string.Join(", ", g.Select(s => s.Label + "=" + s.Value + (string.IsNullOrEmpty(s.Unit) ? "" : " " + s.Unit))));

        var start = list.FirstOrDefault(s => s.Key == "workhours.start");
        if (start == null) { Warn("workhours.start ayarı yok: doğrulama testi atlandı."); return; }
        var badTime = SaveSetting("workhours.start", "25:99");
        Check(!badTime.Success, "Geçersiz saat reddedildi (" + Short(badTime.ErrorMessage) + ")");
        var unknown = SaveSetting("yok.boyle.ayar", "1");
        Check(!unknown.Success, "Bilinmeyen ayar reddedildi");
        var intSetting = list.FirstOrDefault(s => s.Kind == "int" && s.Max > s.Min);
        if (intSetting != null)
        {
            var tooBig = SaveSetting(intSetting.Key, (intSetting.Max + 1).ToString());
            Check(!tooBig.Success, "Sınır dışı sayı reddedildi (" + intSetting.Label + " = " + (intSetting.Max + 1) + ")");
        }
        try
        {
            string value = start.Value == "08:30" ? "08:45" : "08:30";
            Check(SaveSetting("workhours.start", value).Success, "Geçerli değer kaydedildi (mesai başlangıcı " + value + ")");
            var after = Send<List<GeneralSettingDto>>(Commands.GeneralSettingsList, null).First(s => s.Key == "workhours.start");
            Check(after.Value == value, "Kaydedilen değer geri okundu");
        }
        finally
        {
            SaveSetting("workhours.start", start.Value);
        }
        Check(Send<List<GeneralSettingDto>>(Commands.GeneralSettingsList, null).First(s => s.Key == "workhours.start").Value == start.Value, "Ayar eski haline getirildi (" + start.Value + ")");
    }

    static GuardResult SaveSetting(string key, string value)
    {
        return client.SendAsync(Commands.GeneralSettingsSave, new GeneralSettingsSaveRequest { Values = new List<SettingValueDto> { new SettingValueDto { Key = key, Value = value } } }).Result;
    }

    // ================================================================ Ban + beyaz liste

    static void BanTests()
    {
        Title("BAN MOTORU VE BEYAZ LİSTE (TEST-NET adresleri: 203.0.113.x / 198.51.100.x)");
        BanCleanup();
        var first = Send<BanListResult>(Commands.BanList, null);
        Info("Başlangıç: " + first.Banned.Count + " banlı IP, " + first.Attempts.Count + " sayaç | firewall: " + (first.FirewallOk ? "tamam" : "SORUN: " + first.FirewallMessage));
        Check(first.FirewallOk, "Ban firewall kuralları tutarlı");

        // Elle ban
        long before = LastId();
        var list = Send<BanListResult>(Commands.BanAdd, new BanAddRequest { IpAddress = KeepIp, Reason = "SgAutoTest" });
        var b = list.Banned.FirstOrDefault(x => x.IpAddress == KeepIp);
        Check(b != null && b.IsManual, "Elle ban listede (" + (b == null ? "-" : b.RuleName) + ")");
        if (b == null) return;
        var rule = FwRule(b.RuleName);
        Check(rule != null && rule.Remote.Contains(KeepIp) && rule.Action == 0, "Firewall kuralında: gelen, ENGELLE, " + KeepIp);
        var banEv = WaitEvent("Ban.IpBanned", before, 8);
        if (banEv != null) Check(true, "'IP banlandı' olayı yazıldı" + MailText(banEv)); else Info("Elle ban için 'IP banlandı' olayı yazılmıyor (sadece denetim kaydı).");

        // Reddedilmesi gerekenler
        Check(!Raw(Commands.BanAdd, new BanAddRequest { IpAddress = KeepIp }).Success, "Aynı IP ikinci kez banlanamıyor");
        Check(!Raw(Commands.BanAdd, new BanAddRequest { IpAddress = "999.1.1.1" }).Success, "Geçersiz IP reddedildi");
        Check(!Raw(Commands.BanAdd, new BanAddRequest { IpAddress = "" }).Success, "Boş IP reddedildi");
        foreach (string ip in new[] { "127.0.0.1", "192.168.1.10", "10.0.2.2", "::1" })
        {
            var res = Raw(Commands.BanAdd, new BanAddRequest { IpAddress = ip });
            Check(!res.Success, "Korunan adres banlanamıyor: " + ip + (res.Success ? " !!! BANLANDI" : ""));
            if (res.Success) Raw(Commands.BanRemove, new IpListRequest { IpAddresses = new List<string> { ip }, ResetCounter = true });
        }
        foreach (var local in Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
        {
            var res = Raw(Commands.BanAdd, new BanAddRequest { IpAddress = local.ToString() });
            Check(!res.Success, "Sunucunun kendi IP'si banlanamıyor: " + local);
            if (res.Success) Raw(Commands.BanRemove, new IpListRequest { IpAddresses = new List<string> { local.ToString() }, ResetCounter = true });
        }

        // Beyaz liste
        var entry = Send<WhitelistEntry>(Commands.WhitelistAdd, new WhitelistAddRequest { Cidr = "198.51.100.0/24", Note = "SgAutoTest" });
        Check(entry != null && entry.Id > 0, "Beyaz listeye 198.51.100.0/24 eklendi");
        Check(!Raw(Commands.BanAdd, new BanAddRequest { IpAddress = "198.51.100.7" }).Success, "Beyaz listedeki IP banlanamıyor");
        Check(!Raw(Commands.WhitelistAdd, new WhitelistAddRequest { Cidr = "bu-ip-degil" }).Success, "Geçersiz beyaz liste girdisi reddedildi");
        var sys = Send<List<WhitelistEntry>>(Commands.WhitelistList, null).Where(w => w.IsSystem).ToList();
        Check(sys.Count > 0, "Sistem girdileri var (" + string.Join(", ", sys.Select(w => w.Cidr).Take(8)) + ")");
        if (sys.Count > 0)
        {
            Raw(Commands.WhitelistDelete, new IdListRequest { Ids = new List<long> { sys[0].Id } });
            Check(Send<List<WhitelistEntry>>(Commands.WhitelistList, null).Any(w => w.Id == sys[0].Id || (w.IsSystem && w.Cidr == sys[0].Cidr)), "Sistem girdisi silinemiyor (" + sys[0].Cidr + ")");
        }
        if (entry != null) Send<object>(Commands.WhitelistDelete, new IdListRequest { Ids = new List<long> { entry.Id } });
        var afterWl = Raw(Commands.BanAdd, new BanAddRequest { IpAddress = "198.51.100.7", Reason = "SgAutoTest" });
        Check(afterWl.Success, "Beyaz listeden çıkınca banlanabiliyor" + (afterWl.Success ? "" : " (" + afterWl.ErrorMessage + ")"));

        // Kurcalama: kuralı sil → geri yazılmalı
        before = LastId();
        DeleteFwRule(b.RuleName);
        Info("Ban kuralı wf.msc'den silinmiş gibi silindi; geri yazılması bekleniyor...");
        bool back = WaitUntil(() => { var x = FwRule(b.RuleName); return x != null && x.Remote.Contains(KeepIp); }, 45);
        Check(back, "Silinen ban kuralı geri yazıldı");
        var tamper = WaitEvent("Guard.BanRuleTampered", before, 20);
        Check(tamper != null, "'Ban kuralı silindi / değiştirildi' kritik olayı" + MailText(tamper));

        // Toplu: 40 ban art arda
        var ips = Enumerable.Range(100, 40).Select(i => "203.0.113." + i).ToList();
        var sw = Stopwatch.StartNew();
        foreach (string ip in ips) Raw(Commands.BanAdd, new BanAddRequest { IpAddress = ip, Reason = "SgAutoTest toplu" });
        sw.Stop();
        list = Send<BanListResult>(Commands.BanList, null);
        int found = ips.Count(ip => list.Banned.Any(x => x.IpAddress == ip));
        Check(found == ips.Count, "40 IP art arda banlandı (" + found + "/40, " + sw.ElapsedMilliseconds + " ms, IP başına " + sw.ElapsedMilliseconds / 40 + " ms)");
        if (sw.ElapsedMilliseconds > 40000) Warn("Ban başına süre yüksek: " + sw.ElapsedMilliseconds / 40 + " ms.");
        string allRemote = string.Join(",", list.Banned.Select(x => x.RuleName).Distinct().Select(n => { var f = FwRule(n); return f == null ? "" : f.Remote; }));
        Check(ips.All(ip => allRemote.Contains(ip + ",") || allRemote.Contains(ip + "/")), "40 IP'nin hepsi firewall kurallarında");

        // Ban kaldır (KeepIp kalır)
        var remove = ips.Concat(new[] { "198.51.100.7" }).ToList();
        list = Send<BanListResult>(Commands.BanRemove, new IpListRequest { IpAddresses = remove, ResetCounter = true });
        Check(!list.Banned.Any(x => remove.Contains(x.IpAddress)), "41 ban kaldırıldı");
        allRemote = string.Join(",", list.Banned.Select(x => x.RuleName).Distinct().Select(n => { var f = FwRule(n); return f == null ? "" : f.Remote; }));
        Check(!remove.Any(ip => allRemote.Contains(ip + ",") || allRemote.Contains(ip + "/")) && allRemote.Contains(KeepIp), "Firewall'dan da silindi, " + KeepIp + " yerinde");
        list = Send<BanListResult>(Commands.BanSyncFirewall, null);
        Check(list.FirewallOk, "Firewall Kontrol: tutarlı" + (list.FirewallOk ? "" : " (" + list.FirewallMessage + ")"));

        var audit = Send<AuditTrailResult>(Commands.AuditTrailQuery, new AuditTrailQuery { FromUtc = Db(fullStartUtc), Search = KeepIp, MaxRows = 50 });
        Check(audit.Rows.Count > 0, "Denetim kaydında elle ban görünüyor (" + (audit.Rows.Count > 0 ? audit.Rows[0].Action + " / " + audit.Rows[0].UserName : "-") + ")");
    }

    static void BanCleanup()
    {
        try
        {
            var list = client.SendAsync<BanListResult>(Commands.BanList).Result;
            if (list.Success)
            {
                var mine = list.Value.Banned.Where(x => x.IpAddress.StartsWith("203.0.113.") || x.IpAddress.StartsWith("198.51.100.")).Select(x => x.IpAddress).ToList();
                if (mine.Count > 0) Raw(Commands.BanRemove, new IpListRequest { IpAddresses = mine, ResetCounter = true });
            }
            var wl = client.SendAsync<List<WhitelistEntry>>(Commands.WhitelistList).Result;
            if (wl.Success)
            {
                var ids = wl.Value.Where(w => !w.IsSystem && w.Note == "SgAutoTest").Select(w => w.Id).ToList();
                if (ids.Count > 0) Raw(Commands.WhitelistDelete, new IdListRequest { Ids = ids });
            }
        }
        catch { }
    }

    // ================================================================ Lisans

    static void LicenseTests()
    {
        Title("LİSANS");
        var a = Send<LicenseInfoDto>(Commands.LicenseCheck, null);
        Check(a.Active && a.LastError == null && a.StatusText == "Geçerli", "Şimdi kontrol et: sunucuya ulaşıldı, " + a.StatusText + " (son kontrol " + a.LastCheck + ")");
        string apiUrl = a.ApiUrl;
        bool wasDefault = apiUrl == GuardConstants.LicenseDefaultApiUrl;

        Check(!Raw(Commands.LicenseSetApiUrl, new LicenseApiUrlRequest { Url = "ftp://ornek.com/" }).Success, "http/https olmayan adres reddedildi");
        Check(!Raw(Commands.LicenseSetApiUrl, new LicenseApiUrlRequest { Url = "adres degil" }).Success, "Bozuk adres reddedildi");
        try
        {
            // Ulaşılamayan sunucu: tolerans içinde çalışmaya devam etmeli
            var off = Send<LicenseInfoDto>(Commands.LicenseSetApiUrl, new LicenseApiUrlRequest { Url = "http://127.0.0.1:9/" });
            Check(off.Active && off.LastError != null && off.OfflineUntil != null, "Sunucuya ulaşılamıyor → koruma devam (" + off.StatusText + ", " + off.OfflineUntil + " tarihine kadar)");
            Check(Raw(Commands.DashboardGet, null).Success, "Çevrimdışıyken komutlar çalışıyor");

            // Sahte lisans sunucusu: imzasız "valid" cevabı kabul edilmemeli
            using (var fake = new FakeLicenseServer())
            {
                var forged = Send<LicenseInfoDto>(Commands.LicenseSetApiUrl, new LicenseApiUrlRequest { Url = fake.Url });
                Check(fake.Hits > 0, "Servis sahte sunucuya sordu (" + fake.Hits + " istek)");
                Check(forged.LastError != null && forged.LastError.Contains("imza"), "Sahte sunucunun 'geçerli, 2099' cevabı REDDEDİLDİ (" + Short(forged.LastError) + ")");
                Check(forged.Expires == a.Expires, "Bitiş tarihi sahte cevaptan etkilenmedi (" + forged.Expires + ")");
            }
        }
        finally
        {
            var back = Send<LicenseInfoDto>(Commands.LicenseSetApiUrl, new LicenseApiUrlRequest { Url = wasDefault ? "" : apiUrl });
            Check(back.Active && back.LastError == null && back.ApiUrl == apiUrl, "Adres geri alındı, sunucuya tekrar ulaşıldı (" + back.ApiUrl + ")");
        }

        if (string.IsNullOrEmpty(adminKey))
        {
            Warn("Yönetici anahtarı girilmedi: lisans iptal → koruma durur → geri aç testi ATLANDI.");
            return;
        }

        string listJson = AdminCall(apiUrl, "GET", "api/v1/admin/licenses?search=" + Uri.EscapeDataString(a.HwId) + "&top=20");
        if (listJson.StartsWith("401")) { Fail("Yönetici anahtarı kabul edilmedi (401). Anahtarın kendisi girilmeli, özeti değil."); return; }
        var active = Regex.Matches(listJson, @"\{[^{}]*\}").Cast<Match>().Select(m => m.Value).FirstOrDefault(o => o.Contains("\"Status\":1"));
        if (active == null) { Fail("Lisans sunucusunda bu makinenin aktif lisansı bulunamadı: " + Short(listJson)); return; }
        string id = Regex.Match(active, "\"LicenseId\":(\\d+)").Groups[1].Value;
        Info("Lisans no " + id + " iptal edilecek, sonra geri açılacak.");

        long before = LastId();
        try
        {
            Check(AdminCall(apiUrl, "POST", "api/v1/admin/licenses/" + id + "/revoke").StartsWith("200"), "Lisans sunucuda iptal edildi");
            var rev = Send<LicenseInfoDto>(Commands.LicenseCheck, null);
            Check(!rev.Active && rev.StatusText == "İptal edildi", "Kontrol: " + rev.StatusText + " → koruma kapalı");

            var dash = Raw(Commands.DashboardGet, null);
            Check(!dash.Success && dash.ErrorCode == ErrorCodes.Unlicensed, "Lisanssızken Panel komutu reddediliyor (" + dash.ErrorCode + ")");
            Check(!Raw(Commands.BanAdd, new BanAddRequest { IpAddress = "203.0.113.200" }).Success, "Lisanssızken elle ban yapılamıyor");
            Check(!Raw(Commands.EventLogQuery, new EventLogQuery { FromUtc = Db(DateTime.UtcNow.AddHours(-1)), MaxRows = 1 }).Success, "Lisanssızken olay logu okunamıyor");
            Check(Raw(Commands.LicenseGet, null).Success && Raw(Commands.ServiceInfo, null).Success && Raw(Commands.Ping, null).Success, "Lisans / Servis sayfası ve oturum çalışıyor");
            Check(client.GetStatusAsync().Result.Value.Licensed == false, "Durum bilgisinde 'lisanssız' (arayüz lisans sayfasını açar)");

            var keep = FindBanRule(KeepIp);
            Check(keep != null, "Mevcut ban kuralı firewall'da duruyor (" + KeepIp + ")");

            // Lisanssızken gelen saldırı kaydedilmemeli
            for (int i = 0; i < 3; i++) Run("net", @"use \\127.0.0.1\IPC$ /user:sgat_lisanssiz YanlisSifre123");
            Run("net", @"use \\127.0.0.1\IPC$ /delete /y");
            Thread.Sleep(9000);
        }
        finally
        {
            string restore = AdminCall(apiUrl, "POST", "api/v1/admin/licenses/" + id + "/restore");
            var ok = Raw(Commands.LicenseCheck, null);
            var now = client.SendAsync<LicenseInfoDto>(Commands.LicenseGet).Result;
            Check(restore.StartsWith("200") && now.Success && now.Value.Active, "Lisans geri açıldı → koruma yeniden başlatmadan devam ediyor");
            if (!restore.StartsWith("200")) Fail("!!! LİSANS GERİ AÇILAMADI (" + Short(restore) + "): Postman'den 'Geri aç' çalıştırın, lisans no " + id);
        }

        Check(Raw(Commands.DashboardGet, null).Success, "Panel tekrar çalışıyor");
        var expired = Events("Guard.LicenseExpired", before);
        Check(expired.Count == 1, "'Lisans bitti / iptal' kritik olayı bir kez yazıldı (" + expired.Count + ")" + MailText(expired.FirstOrDefault()));
        var restored = Events("Guard.LicenseRestored", before);
        Check(restored.Count == 1, "'Lisans tekrar geçerli' olayı yazıldı (" + restored.Count + ")" + MailText(restored.FirstOrDefault()));
        int leaked = Events("Net.LoginFailed", before).Count(e => (e.UserName ?? "").Contains("sgat_lisanssiz") || (e.Summary ?? "").Contains("sgat_lisanssiz") || (e.Detail ?? "").Contains("sgat_lisanssiz"));
        Check(leaked == 0, "Lisanssızken gelen hatalı girişler kaydedilmedi (" + leaked + ")");

        // Koruma gerçekten devam ediyor mu
        long b2 = LastId();
        for (int i = 0; i < 3; i++) Run("net", @"use \\127.0.0.1\IPC$ /user:sgat_lisansli YanlisSifre123");
        Run("net", @"use \\127.0.0.1\IPC$ /delete /y");
        Check(WaitEvent("Net.LoginFailed", b2, 25) != null, "Lisans geri gelince hatalı girişler yine kaydediliyor");
    }

    static FwInfo FindBanRule(string ip)
    {
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
        try
        {
            foreach (dynamic r in policy.Rules)
            {
                string name = r.Name;
                if (name == null || !name.StartsWith(GuardConstants.FirewallRulePrefix, StringComparison.Ordinal)) continue;
                string remote = r.RemoteAddresses + ",";
                if (remote.Contains(ip + ",") || remote.Contains(ip + "/")) return new FwInfo { Ports = r.LocalPorts, Remote = remote, Action = r.Action };
            }
            return null;
        }
        finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(policy); }
    }

    static string AdminCall(string apiUrl, string method, string path)
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var r = (HttpWebRequest)WebRequest.Create(apiUrl + path);
            r.Method = method;
            r.Timeout = 20000;
            r.Headers["X-Admin-Key"] = adminKey;
            if (method != "GET") r.ContentLength = 0;
            using (var resp = (HttpWebResponse)r.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
                return (int)resp.StatusCode + " " + sr.ReadToEnd();
        }
        catch (WebException ex)
        {
            var resp = ex.Response as HttpWebResponse;
            return resp == null ? "000 " + ex.Message : (int)resp.StatusCode + " " + resp.StatusDescription;
        }
    }

    /// <summary>Her isteğe imzasız "geçerli, 2099'a kadar" cevabı veren sahte lisans sunucusu (localhost).</summary>
    sealed class FakeLicenseServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        public readonly string Url;
        public int Hits;

        public FakeLicenseServer()
        {
            Url = "http://127.0.0.1:" + (new Random().Next(50100, 50900)) + "/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _listener.BeginGetContext(Handle, null);
        }

        private void Handle(IAsyncResult ar)
        {
            try
            {
                var ctx = _listener.EndGetContext(ar);
                Interlocked.Increment(ref Hits);
                string body;
                using (var sr = new StreamReader(ctx.Request.InputStream)) body = sr.ReadToEnd();
                string nonce = Regex.Match(body, "\"Nonce\":\"([^\"]*)\"").Groups[1].Value.Replace("\\/", "/");
                string hw = Regex.Match(body, "\"HwId\":\"([^\"]*)\"").Groups[1].Value;
                string json = "{\"Status\":\"valid\",\"LicenseId\":1,\"Customer\":\"Sahte\",\"ExpiresUtc\":\"2099-01-01T00:00:00Z\",\"ServerTimeUtc\":\"" + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                              + "\",\"Nonce\":\"" + nonce + "\",\"HwId\":\"" + hw + "\",\"Matched\":4,\"Signature\":\"" + Convert.ToBase64String(new byte[384]) + "\"}";
                byte[] data = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json";
                ctx.Response.OutputStream.Write(data, 0, data.Length);
                ctx.Response.Close();
                _listener.BeginGetContext(Handle, null);
            }
            catch { }
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }

    // ================================================================ Windows olayları

    static void WindowsEventTests()
    {
        Title("WINDOWS OLAYLARI (kullanıcı, grup, görev, firewall, audit, RDP ayarı, log temizleme)");
        string admins = ((NTAccount)new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Translate(typeof(NTAccount))).Value;
        admins = admins.Substring(admins.IndexOf('\\') + 1);
        string pw = "Sg!" + Guid.NewGuid().ToString("N").Substring(0, 8) + "aA1"; // 14 karakter: net user daha uzununda (Y/N) sorar
        string auditBackup = Path.Combine(outDir, "audit_yedek.csv"); // test sonunda silinir
        const string RdpKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";
        object nlaBefore = null;
        bool domainFwWasOn = RunOut("netsh.exe", "advfirewall show domainprofile state").Item2.IndexOf("ON", StringComparison.OrdinalIgnoreCase) >= 0;
        Run("net", "user " + TestUser + " /delete");
        try
        {
            // Kullanıcı
            long before = LastId();
            var add = RunOut("net.exe", "user " + TestUser + " " + pw + " /add");
            var createdAt = DateTime.UtcNow;
            if (add.Item1 != 0) Warn("Test kullanıcısı oluşturulamadı: " + Short(add.Item2));
            Expect("Win.UserCreated", before, 25, "Yeni Windows kullanıcısı (" + TestUser + ")", TestUser);
            before = LastId();
            Run("net", "localgroup \"" + admins + "\" " + TestUser + " /add");
            Expect("Win.AdminGroupAdded", before, 25, "Administrators grubuna üye eklendi", null);
            before = LastId();
            Run("net", "user " + TestUser + " /active:no");
            Expect("Win.UserEnabledDisabled", before, 25, "Kullanıcı devre dışı bırakıldı", TestUser);

            // Zamanlanmış görev
            before = LastId();
            Run("schtasks.exe", "/create /tn " + TaskName + " /tr \"cmd.exe /c echo sgautotest\" /sc once /st 23:59 /ru SYSTEM /f");
            Info("Test görevi oluşturuldu (cmd.exe /c echo, SYSTEM); görev listesi dakikada bir karşılaştırılıyor (en fazla ~100 sn)...");
            var taskEv = Expect("Win.ScheduledTaskCreated", before, 100, "Zamanlanmış görev (Windows denetimi kapalıyken de)", TaskName);
            if (taskEv != null)
            {
                Check(taskEv.Summary.StartsWith("ŞÜPHELİ") && (taskEv.Detail ?? "").Contains("cmd.exe /c echo sgautotest"), "Görev olayında çalıştırdığı komut yazıyor ve cmd ile çalıştığı için ŞÜPHELİ işaretli");
                Info("Olay ayrıntısı:\r\n    " + (taskEv.Detail ?? "").Replace("\n", "\n    "));
            }

            // Firewall kuralı
            before = LastId();
            Run("netsh.exe", "advfirewall firewall add rule name=" + FwTestRule + " dir=in action=allow protocol=TCP localport=65011");
            Expect("Win.FirewallRuleChanged", before, 25, "Firewall'da port açıldı (65011)", FwTestRule);
            before = LastId();
            Run("netsh.exe", "advfirewall firewall delete rule name=" + FwTestRule);
            Expect("Win.FirewallRuleChanged", before, 25, "Firewall kuralı silindi", FwTestRule);

            // Firewall profili (etkin olmayan Domain profili: sunucunun gerçek koruması değişmez)
            before = LastId();
            Run("netsh.exe", "advfirewall set domainprofile state " + (domainFwWasOn ? "off" : "on"));
            if (domainFwWasOn) Expect("Win.FirewallDisabled", before, 25, "Firewall profili kapatıldı (Domain)", null);
            Run("netsh.exe", "advfirewall set domainprofile state " + (domainFwWasOn ? "on" : "off"));

            // Fidye öncesi komut (olmayan Q: sürücüsü: hiçbir şey silinmez)
            before = LastId();
            RunOut("vssadmin.exe", "delete shadows /for=Q: /quiet");
            ExpectSoft("Win.RansomwarePrecursor", before, 25, "Gölge kopya silme komutu yakalandı", "vssadmin",
                "Security 4688 (işlem oluşturma) denetimi ve komut satırı kaydı açık olmalı (Güvenlik Önerileri'nde çıkıyor mu bakın).");

            // Olay logu temizleme (boş 'HardwareEvents' logu)
            before = LastId();
            RunOut("wevtutil.exe", "cl HardwareEvents");
            Expect("Win.EventLogCleared", before, 25, "Windows olay logu temizlendi (HardwareEvents)", null);

            // Audit politikası
            before = LastId();
            RunOut("auditpol.exe", "/backup /file:\"" + auditBackup + "\"");
            RunOut("auditpol.exe", "/set /subcategory:{0CCE9214-69AE-11D9-BED3-505054503030} /success:disable /failure:disable");
            RunOut("auditpol.exe", "/set /subcategory:{0CCE9214-69AE-11D9-BED3-505054503030} /success:enable /failure:enable");
            Expect("Win.AuditPolicyChanged", before, 25, "Audit politikası değişti", null);

            // RDP NLA ayarı (30 sn'de bir kontrol edilir)
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = hklm.OpenSubKey(RdpKey, true))
            {
                nlaBefore = k.GetValue("UserAuthentication");
                int current = nlaBefore == null ? 0 : Convert.ToInt32(nlaBefore);
                before = LastId();
                k.SetValue("UserAuthentication", current == 1 ? 0 : 1, RegistryValueKind.DWord);
                Info("RDP NLA ayarı " + current + " → " + (current == 1 ? 0 : 1) + " yapıldı; registry kontrolü bekleniyor (en fazla ~40 sn)...");
            }
            Expect("Win.RdpSettingsChanged", before, 60, "RDP NLA ayarı değişti", null);

            // Şifre sıfırlama: hesap oluşturulduktan sonraki ilk 60 sn "ilk şifre atama" sayılır, o yüzden en sonda
            int wait = 66 - (int)(DateTime.UtcNow - createdAt).TotalSeconds;
            if (wait > 0) { Info("Şifre sıfırlama testi için " + wait + " sn bekleniyor (yeni hesabın ilk 60 saniyesi sayılmaz)..."); Thread.Sleep(wait * 1000); }
            before = LastId();
            Run("net", "user " + TestUser + " " + pw.Substring(0, 12) + "x9");
            Expect("Win.PasswordReset", before, 25, "Şifre sıfırlandı", TestUser);
        }
        finally
        {
            Run("net", "user " + TestUser + " /delete");
            Run("schtasks.exe", "/delete /tn " + TaskName + " /f");
            Run("netsh.exe", "advfirewall firewall delete rule name=" + FwTestRule);
            Run("netsh.exe", "advfirewall set domainprofile state " + (domainFwWasOn ? "on" : "off"));
            if (File.Exists(auditBackup))
            {
                RunOut("auditpol.exe", "/restore /file:\"" + auditBackup + "\"");
                File.Delete(auditBackup);
            }
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = hklm.OpenSubKey(RdpKey, true))
                    if (nlaBefore != null) k.SetValue("UserAuthentication", Convert.ToInt32(nlaBefore), RegistryValueKind.DWord);
            }
            catch (Exception ex) { Warn("RDP NLA ayarı geri alınamadı: " + ex.Message); }
            Info("Temizlik: test kullanıcısı, görev, firewall kuralı silindi; firewall profili, audit politikası ve RDP NLA eski haline getirildi.");
        }
    }

    static EventLogRow Expect(string type, long afterId, int seconds, string what, string mustContain)
    {
        var all = new List<EventLogRow>();
        WaitUntil(() => (all = Events(type, afterId)).Any(e => Mentions(e, mustContain)), seconds);
        var ev = all.FirstOrDefault(e => Mentions(e, mustContain));
        Check(ev != null, what + " → " + type + (ev == null ? (all.Count > 0 ? " (olay var ama '" + mustContain + "' geçmiyor: " + Short(all[0].Summary) + ")" : " (olay gelmedi)") : MailText(ev) + ": " + Short(ev.Summary)));
        return ev;
    }

    /// <summary>Windows denetim ayarına bağlı olaylar: gelmezse hata değil uyarı.</summary>
    static void ExpectSoft(string type, long afterId, int seconds, string what, string mustContain, string hint)
    {
        var all = new List<EventLogRow>();
        WaitUntil(() => (all = Events(type, afterId)).Any(e => Mentions(e, mustContain)), seconds);
        var ev = all.FirstOrDefault(e => Mentions(e, mustContain));
        if (ev != null) Check(true, what + " → " + type + MailText(ev) + ": " + Short(ev.Summary));
        else Warn(what + " → " + type + " olayı GELMEDİ. " + hint);
    }

    static bool Mentions(EventLogRow e, string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        return ((e.Summary ?? "") + " " + (e.Detail ?? "") + " " + (e.UserName ?? "")).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ================================================================ SQL

    static void SqlTests()
    {
        Title("SQL SERVER (hatalı giriş, login / sysadmin / şifre, xp_cmdshell, veritabanı sil)");
        var instances = Send<List<SqlInstanceDto>>(Commands.SqlInstances, null);
        if (instances.Any(i => i.IsConfigured && i.IsEnabled && (i.AuditStatus ?? "").Contains("bekleniyor")))
        {
            Info("Servis yeni başlamış: ilk SQL izleme kontrolü bekleniyor (en fazla 45 sn)...");
            WaitUntil(() => !(instances = Send<List<SqlInstanceDto>>(Commands.SqlInstances, null)).Any(i => i.IsConfigured && i.IsEnabled && (i.AuditStatus ?? "").Contains("bekleniyor")), 45);
        }
        foreach (var i in instances)
            Info(i.InstanceName + ": " + i.ServiceState + " | " + i.ProductVersion + " " + i.Edition + " | bağlantı " + (i.IsConfigured ? i.Server + (i.Port > 0 ? "," + i.Port : "") + " (" + i.Login + ")" : "YOK")
                 + " | izleme " + (i.IsEnabled ? "açık" : "kapalı") + " | audit " + (i.AuditInstalled ? (i.AuditRunning ? "çalışıyor" : "DURMUŞ") : "kurulu değil") + " " + i.AuditStatus);
        var inst = instances.FirstOrDefault(i => i.IsConfigured && i.IsEnabled);
        if (inst == null) { Warn("Yapılandırılmış SQL instance yok: SQL testi atlandı."); return; }
        Check(inst.AuditInstalled && inst.AuditRunning, "SQL Audit kurulu ve çalışıyor (" + inst.InstanceName + ")");
        Check(string.Equals(inst.Login, GuardConstants.SqlLoginName, StringComparison.OrdinalIgnoreCase), "Bağlantı " + GuardConstants.SqlLoginName + " hesabıyla");

        string server = inst.Server + (inst.Port > 0 ? "," + inst.Port : "");
        try
        {
            RunOut("sqlcmd.exe", "-?");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Warn("sqlcmd.exe bu makinede yok (PATH'te değil): SQL hatalı giriş / değişiklik testleri atlandı.");
            return;
        }
        var who = RunOut("sqlcmd.exe", "-S " + server + " -C -E -b -h -1 -W -Q \"SET NOCOUNT ON; SELECT IS_SRVROLEMEMBER('sysadmin')\"");
        if (who.Item1 != 0 || who.Item2.Trim() != "1")
        {
            Warn("sqlcmd ile Windows kimliğiyle sysadmin bağlanılamadı (" + Short(who.Item2.Trim()) + "): SQL değişiklik testleri atlandı.");
            return;
        }

        // Hatalı giriş
        long before = LastId();
        for (int i = 0; i < 3; i++) RunOut("sqlcmd.exe", "-S " + server + " -C -U sgat_yok -P YanlisSifre123! -l 5 -Q \"select 1\"");
        var failed = WaitEvent("Sql.LoginFailed", before, 30);
        Check(failed != null, "SQL hatalı giriş kaydedildi" + (failed == null ? "" : ": " + Short(failed.Summary) + " | IP " + failed.IpAddress));

        string pw = "Sg!" + Guid.NewGuid().ToString("N").Substring(0, 14) + "aA1";
        string xp = RunOut("sqlcmd.exe", "-S " + server + " -C -E -b -h -1 -W -Q \"SET NOCOUNT ON; SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'xp_cmdshell'\"").Item2.Trim();
        bool advWasOff = RunOut("sqlcmd.exe", "-S " + server + " -C -E -b -h -1 -W -Q \"SET NOCOUNT ON; SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'show advanced options'\"").Item2.Trim() == "0";
        Sql(server, "IF SUSER_ID('" + SqlTestLogin + "') IS NOT NULL DROP LOGIN [" + SqlTestLogin + "]; IF DB_ID('" + SqlTestDb + "') IS NOT NULL DROP DATABASE [" + SqlTestDb + "];");
        Thread.Sleep(3000);
        before = LastId();
        try
        {
            Check(Sql(server, "CREATE LOGIN [" + SqlTestLogin + "] WITH PASSWORD = '" + pw + "', CHECK_POLICY = OFF;"), "SQL: login oluşturuldu");
            Sql(server, "ALTER SERVER ROLE sysadmin ADD MEMBER [" + SqlTestLogin + "];");
            Sql(server, "ALTER LOGIN [" + SqlTestLogin + "] WITH PASSWORD = '" + pw + "Z';");
            if (xp == "0")
                Sql(server, "EXEC sp_configure 'show advanced options', 1; RECONFIGURE; EXEC sp_configure 'xp_cmdshell', 1; RECONFIGURE;");
            else Info("xp_cmdshell zaten açık (" + xp + "): ayar testi atlandı.");
            Sql(server, "CREATE DATABASE [" + SqlTestDb + "];");
            Sql(server, "DROP DATABASE [" + SqlTestDb + "];");
            Info("SQL değişiklikleri yapıldı; audit olaylarının okunması bekleniyor (en fazla ~6 dk)...");

            var expected = new List<Tuple<string, string, string>>
            {
                Tuple.Create("Sql.LoginChanged", SqlTestLogin, "Login oluşturuldu"),
                Tuple.Create("Sql.ServerRoleChanged", SqlTestLogin, "sysadmin üyeliği verildi"),
                Tuple.Create("Sql.PasswordChanged", SqlTestLogin, "Login şifresi değişti"),
                Tuple.Create("Sql.DatabaseDropped", SqlTestDb, "Veritabanı silindi")
            };
            if (xp == "0") expected.Add(Tuple.Create("Sql.DangerousConfigChanged", "xp_cmdshell", "xp_cmdshell açıldı"));
            var got = new Dictionary<string, EventLogRow>();
            WaitUntil(() =>
            {
                foreach (var x in expected.Where(x => !got.ContainsKey(x.Item1)))
                {
                    var ev = Events(x.Item1, before).FirstOrDefault(e => Mentions(e, x.Item2));
                    if (ev != null) got[x.Item1] = ev;
                }
                return got.Count == expected.Count;
            }, 380, true);
            foreach (var x in expected)
            {
                EventLogRow ev;
                got.TryGetValue(x.Item1, out ev);
                Check(ev != null, x.Item3 + " → " + x.Item1 + (ev == null ? " (olay gelmedi)" : MailText(ev) + ": " + Short(ev.Summary) + " | kim: " + (ev.UserName ?? "-")));
            }
            EventLogRow cfg;
            if (got.TryGetValue("Sql.DangerousConfigChanged", out cfg))
            {
                Check(!string.IsNullOrEmpty(cfg.UserName), "xp_cmdshell değişikliğinde yapan kişi yazıyor (" + (cfg.UserName ?? "YOK") + ")");
                Info("Olay ayrıntısı:\r\n    " + (cfg.Detail ?? "").Replace("\n", "\n    "));
                if (string.IsNullOrEmpty(cfg.UserName)) DumpConfigAudit();
            }
            else DumpConfigAudit();

            if (xp == "0")
            {
                // Aynı ayar art arda değişirse her değişiklik ayrı bildirilmeli (saldırgan kapatıp tekrar açabilir)
                long b2 = LastId();
                Sql(server, "EXEC sp_configure 'xp_cmdshell', 0; RECONFIGURE;");
                var closed = Expect("Sql.DangerousConfigChanged", b2, 60, "xp_cmdshell kapatıldı", "kapatıldı");
                long b3 = LastId();
                Sql(server, "EXEC sp_configure 'xp_cmdshell', 1; RECONFIGURE;");
                var again = Expect("Sql.DangerousConfigChanged", b3, 60, "xp_cmdshell hemen ardından TEKRAR açıldı", "AÇILDI");
                if (again != null) Check(!string.IsNullOrEmpty(again.UserName), "İkinci açılışta da yapan kişi yazıyor (" + (again.UserName ?? "YOK") + ")");
            }
        }
        finally
        {
            if (xp == "0") Sql(server, "EXEC sp_configure 'xp_cmdshell', 0; RECONFIGURE;" + (advWasOff ? " EXEC sp_configure 'show advanced options', 0; RECONFIGURE;" : ""));
            Sql(server, "IF DB_ID('" + SqlTestDb + "') IS NOT NULL DROP DATABASE [" + SqlTestDb + "]; IF SUSER_ID('" + SqlTestLogin + "') IS NOT NULL DROP LOGIN [" + SqlTestLogin + "];");
            Info("Temizlik: test login'i ve veritabanı silindi, xp_cmdshell eski haline getirildi.");
        }

        // --- SecurityGuard'ın kendi SQL hesabı devre dışı bırakılırsa: "izleme kör" alarmı + geri gelince "tekrar bağlandı" ---
        string sgLogin = GuardConstants.SqlLoginName;
        var ivalSetting = Send<List<GeneralSettingDto>>(Commands.GeneralSettingsList, null).FirstOrDefault(s => s.Key == "sql.baselineIntervalMinutes");
        string prevIval = ivalSetting != null ? ivalSetting.Value : "5";
        int prevMin;
        if (!int.TryParse(prevIval, out prevMin) || prevMin < 1) prevMin = 5;
        int downTimeout = Math.Min(prevMin * 60 + 90, 360); // ilk kontrol eski aralığı bekleyebilir
        bool reenabled = false;
        SaveSetting("sql.baselineIntervalMinutes", "1"); // sonraki kontrolleri hızlandır
        try
        {
            long b = LastId();
            if (!Sql(server, "ALTER LOGIN [" + sgLogin + "] DISABLE;"))
            {
                Warn(sgLogin + " devre dışı bırakılamadı (yetki?): SQL bağlantı alarmı testi atlandı.");
            }
            else
            {
                Info(sgLogin + " devre dışı bırakıldı; 'SQL izleme kör kaldı' alarmı bekleniyor (en fazla " + downTimeout / 60 + " dk)...");
                var down = WaitEventShow("Sql.Unreachable", b, downTimeout);
                Check(down != null, "Hesap devre dışıyken 'SQL'e bağlanılamıyor' kritik olayı" + MailText(down) + (down == null ? "" : ": " + Short(down.Summary)));
                if (down != null)
                {
                    Check(Mentions(down, sgLogin) || Mentions(down, "reddedildi") || Mentions(down, "hesab"), "Alarm sebebi hesap reddi (silme / devre dışı / şifre) olarak yazıyor");
                    Info("Olay ayrıntısı:\r\n    " + (down.Detail ?? "").Replace("\n", "\n    "));
                }

                long b2 = LastId();
                reenabled = Sql(server, "ALTER LOGIN [" + sgLogin + "] ENABLE;");
                Check(reenabled, "Hesap geri açıldı");
                if (reenabled)
                {
                    Info("Hesap geri açıldı; 'SQL'e tekrar bağlanıldı' olayı bekleniyor (en fazla ~2,5 dk)...");
                    var up = WaitEventShow("Sql.Reconnected", b2, 150);
                    Check(up != null, "Hesap geri açılınca 'SQL'e tekrar bağlanıldı' olayı" + (up == null ? "" : MailText(up) + ": " + Short(up.Summary)));
                }
            }
        }
        finally
        {
            if (!reenabled) Sql(server, "ALTER LOGIN [" + sgLogin + "] ENABLE;"); // her ihtimale karşı hesabı aç
            SaveSetting("sql.baselineIntervalMinutes", prevIval);
            Info("Temizlik: " + sgLogin + " etkin, SQL kontrol aralığı " + prevIval + " dk.");
        }
    }

    /// <summary>Teşhis: SQL Audit'in ayar değişikliği için Application loguna ne yazdığını gösterir (33205).</summary>
    static void DumpConfigAudit()
    {
        try
        {
            var q = new System.Diagnostics.Eventing.Reader.EventLogQuery("Application", System.Diagnostics.Eventing.Reader.PathType.LogName,
                "*[System[(EventID=33205) and TimeCreated[timediff(@SystemTime) <= 900000]]]") { ReverseDirection = true };
            int shown = 0, total = 0;
            using (var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(q))
                for (var r = reader.ReadEvent(); r != null && shown < 6; r = reader.ReadEvent())
                    using (r)
                    {
                        total++;
                        string text = string.Join("\n", r.Properties.Select(pr => Convert.ToString(pr.Value)));
                        if (text.IndexOf("configure", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("ALST", StringComparison.Ordinal) < 0) continue;
                        shown++;
                        var keep = text.Split('\n').Where(l => l.StartsWith("action_id") || l.StartsWith("class_type") || l.StartsWith("server_principal_name") || l.StartsWith("statement") || l.StartsWith("application_name") || l.StartsWith("succeeded"));
                        Info("Audit kaydı " + r.TimeCreated + ": " + string.Join(" | ", keep.Select(k => Short(k.Trim(), 120))));
                    }
            Info("Son 15 dakikada " + total + " audit kaydı incelendi, ayar değişikliğiyle ilgili " + shown + " kayıt bulundu" + (shown == 0 ? " (SQL Audit bu ayar değişikliğini loga yazmıyor)." : "."));
        }
        catch (Exception ex) { Info("Audit kayıtları okunamadı: " + ex.Message); }
    }

    static bool Sql(string server, string sql)
    {
        var r = RunOut("sqlcmd.exe", "-S " + server + " -C -E -b -Q \"" + sql.Replace("\"", "\\\"") + "\"");
        if (r.Item1 != 0) Info("sqlcmd hata (" + r.Item1 + "): " + Short(r.Item2.Trim()) + " | " + Short(sql));
        return r.Item1 == 0;
    }

    // ================================================================ Taramalar

    static void ScanTests()
    {
        Title("TARAMALAR (Güvenlik Önerileri + Performans Önerileri)");
        foreach (string scope in new[] { CheckScopes.Security, CheckScopes.Performance })
        {
            var sw = Stopwatch.StartNew();
            var run = Send<CheckRunDto>(Commands.ChecksRun, new CheckScopeRequest { Scope = scope });
            WaitUntil(() => !(run = Send<CheckRunDto>(Commands.ChecksLatest, new CheckScopeRequest { Scope = scope })).IsRunning, 600, true);
            var items = run.Items ?? new List<CheckItemDto>();
            Check(!run.IsRunning && items.Count > 0, scope + " taraması bitti: " + items.Count + " kontrol, " + (int)sw.Elapsed.TotalSeconds + " sn"
                  + " (geçti " + items.Count(i => i.Status == SecurityGuard.Shared.Enums.CheckStatus.Gecti) + ", bilgi " + items.Count(i => i.Status == SecurityGuard.Shared.Enums.CheckStatus.Bilgi)
                  + ", uyarı " + items.Count(i => i.Status == SecurityGuard.Shared.Enums.CheckStatus.Uyari) + ", kritik " + items.Count(i => i.Status == SecurityGuard.Shared.Enums.CheckStatus.Kritik) + ")");
            foreach (var i in items.Where(i => i.Status >= SecurityGuard.Shared.Enums.CheckStatus.Uyari).OrderByDescending(i => i.Status))
                Info((i.Status == SecurityGuard.Shared.Enums.CheckStatus.Kritik ? "KRİTİK " : "uyarı  ") + "[" + i.Area + "] " + i.Title + ": " + Short(i.Summary));
            var broken = items.Where(i => ((i.Summary ?? "") + (i.Detail ?? "")).IndexOf("kontrol edilemedi", StringComparison.OrdinalIgnoreCase) >= 0
                                          || ((i.Summary ?? "") + (i.Detail ?? "")).IndexOf("Exception", StringComparison.Ordinal) >= 0).ToList();
            Check(broken.Count == 0, scope + ": çalışamayan kontrol yok" + (broken.Count == 0 ? "" : " → " + string.Join(" | ", broken.Select(i => i.Title + ": " + Short(i.Summary)))));
            Check(items.Select(i => i.CheckId).Distinct().Count() == items.Count, scope + ": kontrol kimlikleri tekil");
        }
    }

    // ================================================================ Raporlar / panel / bütünlük

    static void ReportTests()
    {
        Title("RAPORLAR, PANEL, LOG BÜTÜNLÜĞÜ");
        var sw = Stopwatch.StartNew();
        var daily = Send<ReportDto>(Commands.ReportPreview, null);
        Check(daily != null && daily.Html != null && daily.Html.Length > 3000 && daily.Html.IndexOf("<table", StringComparison.OrdinalIgnoreCase) >= 0,
            "Günlük rapor önizleme (" + (daily == null || daily.Html == null ? 0 : daily.Html.Length / 1024) + " KB, " + sw.ElapsedMilliseconds + " ms): " + (daily == null ? "-" : daily.Subject));
        SaveHtml("gunluk", daily);
        sw.Restart();
        var monthly = Send<ReportDto>(Commands.ReportMonthlyPreview, new IdRequest { Id = 1 });
        Check(monthly != null && monthly.Html != null && monthly.Html.Length > 3000,
            "Aylık rapor önizleme (bu ay; " + (monthly == null || monthly.Html == null ? 0 : monthly.Html.Length / 1024) + " KB, " + sw.ElapsedMilliseconds + " ms): " + (monthly == null ? "-" : monthly.Subject));
        SaveHtml("aylik", monthly);
        foreach (var rep in new[] { daily, monthly })
            if (rep != null && rep.Html != null)
                Check(rep.Html.IndexOf("Exception", StringComparison.Ordinal) < 0 && rep.Html.IndexOf("{0}", StringComparison.Ordinal) < 0 && rep.Html.IndexOf("NaN", StringComparison.Ordinal) < 0,
                    "Rapor içinde hata metni / doldurulmamış alan yok (" + rep.Kind + ")");

        var sent = Send<ReportDto>(Commands.ReportSendNow, null);
        Check(sent != null && sent.Id > 0, "Günlük rapor 'Şimdi gönder': " + (sent == null ? "-" : sent.Mailed ? "mail kuyruğa alındı" : "MAİL YOK (" + sent.MailNote + ")"));
        var sentMonthly = Send<ReportDto>(Commands.ReportMonthlySendNow, null);
        Check(sentMonthly != null && sentMonthly.Id > 0, "Aylık rapor 'Şimdi gönder': " + (sentMonthly == null ? "-" : sentMonthly.Mailed ? "mail kuyruğa alındı" : "MAİL YOK (" + sentMonthly.MailNote + ")"));
        var list = Send<List<ReportDto>>(Commands.ReportList, null);
        Check(list.Any(r => r.Kind == "daily") && list.Any(r => r.Kind == "monthly"), "Rapor listesinde günlük ve aylık var (" + list.Count + " rapor)");

        sw.Restart();
        var dash = Send<DashboardDto>(Commands.DashboardGet, null);
        Check(dash.Tiles != null && dash.Tiles.Count >= 4, "Panel: " + (dash.Tiles == null ? 0 : dash.Tiles.Count) + " kutucuk, " + sw.ElapsedMilliseconds + " ms");
        foreach (var t in dash.Tiles ?? new List<DashboardTileDto>()) Info("Kutucuk: " + t.Title + " = " + t.Value + " | " + Short(t.Detail) + (t.Level > 0 ? " [seviye " + t.Level + "]" : ""));
        foreach (var t in dash.Todos ?? new List<DashboardTodoDto>()) Info("Yapılacak: " + Short(t.Text));

        sw.Restart();
        var v = Send<EventLogVerifyResult>(Commands.EventLogVerify, null);
        Info("Bütünlük: zincir " + (v.ChainEnabled ? "açık" : "KAPALI") + ", " + v.CheckedRows + " satır, " + sw.ElapsedMilliseconds + " ms | değişmiş " + v.ModifiedRows + ", boşluk " + v.Gaps + ", imzasız " + v.UnsignedRows
             + ", sondan silinen " + v.TailDeletedRows + " | yönetici silmiş " + v.AdminDeletedRows + ", yönetici değiştirmiş " + v.AdminModifiedRows + ", kabul edilmiş " + v.AcceptedRows);
        Check(v.ChainEnabled && v.CheckedRows > 0, "Olay logu bütünlük zinciri açık");
        Check(v.ModifiedRows == 0 && v.Gaps == 0 && v.TailDeletedRows == 0, "Olay logunda açıklanamayan değişiklik / silme yok");
    }

    static void SaveHtml(string name, ReportDto report)
    {
        if (report == null || report.Html == null) return;
        try
        {
            string f = Path.Combine(outDir, "rapor_" + name + ".html");
            File.WriteAllText(f, report.Html, new UTF8Encoding(true));
            Info("Rapor kaydedildi: " + f);
        }
        catch (Exception ex) { Info("Rapor dosyaya yazılamadı: " + ex.Message); }
    }

    // ================================================================ SQL bakım / yedek

    static void MaintenanceBackupTests()
    {
        Title("SQL BAKIM (deneme modu) VE YEDEKLEME");
        var m = client.SendAsync<MaintenanceStateDto>(Commands.MaintenanceGet).Result;
        if (!m.Success) Warn("SQL Bakımı durumu alınamadı: " + m.ErrorMessage);
        else
        {
            var ms = m.Value;
            Info("Bakım: " + (ms.Config.Enabled ? "açık" : "kapalı") + (ms.Config.DryRun ? " (deneme modu)" : "") + " | " + ms.Databases.Count + " veritabanı (" + ms.Databases.Count(d => d.Selected) + " seçili) | eşikler: " + ms.Thresholds);
            foreach (string n in ms.Notes ?? new List<string>()) Info("Not: " + Short(n));
            foreach (string n in ms.OtherJobs ?? new List<string>()) Info("Diğer bakım işi: " + Short(n));
            if (ms.Databases.Count == 0) Warn("Bakım: veritabanı listesi boş (SQL bağlantısı yok?), deneme çalıştırması atlandı.");
            else
            {
                int runsBefore = ms.Runs == null ? 0 : ms.Runs.Count;
                string lastBefore = ms.Runs != null && ms.Runs.Count > 0 ? ms.Runs[0].RunId : null;
                var sw = Stopwatch.StartNew();
                ms = Send<MaintenanceStateDto>(Commands.MaintenanceRun, new MaintenanceRunRequest { DryRun = true });
                WaitUntil(() => !(ms = Send<MaintenanceStateDto>(Commands.MaintenanceGet, null)).IsRunning, 900, true);
                var run = ms.Runs == null ? null : ms.Runs.FirstOrDefault();
                Check(!ms.IsRunning && run != null && run.RunId != lastBefore, "Bakım deneme çalıştırması bitti (" + (int)sw.Elapsed.TotalSeconds + " sn): " + (run == null ? "-" : run.Status + " | " + Short(run.Summary)));
                if (run != null)
                {
                    Check(run.DryRun, "Çalıştırma deneme modunda (hiçbir şey değiştirilmedi)");
                    var ops = Send<List<MaintenanceOpDto>>(Commands.MaintenanceOps, new RunIdRequest { RunId = run.RunId });
                    foreach (var o in ops.Take(12)) Info("  " + o.Database + " " + o.ObjectName + " " + o.Operation + ": " + o.Result + (string.IsNullOrEmpty(o.Error) ? "" : " (" + Short(o.Error) + ")"));
                    Check(ops.All(o => o.Result != "Hata"), "Bakım işlemlerinde hata yok (" + ops.Count + " satır)");
                }
            }
        }

        var b = client.SendAsync<BackupStateDto>(Commands.BackupGet).Result;
        if (!b.Success) { Warn("SQL Yedekleme durumu alınamadı: " + b.ErrorMessage); return; }
        var bs = b.Value;
        Info("Yedek: " + (bs.Config.Enabled ? "açık" : "kapalı") + " | hedef " + (string.IsNullOrEmpty(bs.Config.TargetFolder) ? "(yok)" : bs.Config.TargetFolder) + " | " + bs.TargetInfo
             + " | WinRAR: " + (bs.RarFound ?? "BULUNAMADI") + " " + bs.RarNote + " | şifre " + (bs.HasPassword ? "var" : "YOK") + " | seçili " + bs.Databases.Count(d => d.Selected) + "/" + bs.Databases.Count);
        foreach (string n in bs.Notes ?? new List<string>()) Info("Not: " + Short(n));
        if (string.IsNullOrEmpty(bs.Config.TargetFolder) || !bs.Databases.Any(d => d.Selected))
        {
            Warn("Yedekleme yapılandırılmamış (hedef klasör / veritabanı seçilmemiş): yedek alma testi ATLANDI. SQL Yedekleme sayfasından ayarlayıp testi tekrar çalıştırın.");
            return;
        }
        string lastRun = bs.Runs != null && bs.Runs.Count > 0 ? bs.Runs[0].RunId : null;
        long before = LastId();
        var sw2 = Stopwatch.StartNew();
        var started = client.SendAsync<BackupStateDto>(Commands.BackupRun, null, TimeSpan.FromMinutes(2)).Result;
        if (!started.Success) { Fail("Yedek başlatılamadı: " + started.ErrorMessage); return; }
        WaitUntil(() => !(bs = Send<BackupStateDto>(Commands.BackupGet, null)).IsRunning, 1500, true);
        var brun = bs.Runs == null ? null : bs.Runs.FirstOrDefault();
        Check(!bs.IsRunning && brun != null && brun.RunId != lastRun, "Yedekleme bitti (" + (int)sw2.Elapsed.TotalSeconds + " sn): " + (brun == null ? "-" : brun.Status + " | " + Short(brun.Summary)));
        if (brun == null) return;
        var files = Send<List<BackupFileDto>>(Commands.BackupFiles, new RunIdRequest { RunId = brun.RunId });
        foreach (var f in files) Info(f.Database + " | " + f.Step + " | " + f.Result + " | " + f.SizeMb.ToString("0.0", CultureInfo.InvariantCulture) + " MB | " + f.DurationMs + " ms | " + Short(f.ArchivePath) + (string.IsNullOrEmpty(f.Note) ? "" : " | " + Short(f.Note)));
        // .bak geçici dosyadır (arşivlenince silinir); kalıcı olan .rar
        var archives = files.Where(f => f.Step == "ARŞİV" && !string.IsNullOrEmpty(f.ArchivePath)).Select(f => f.ArchivePath).Distinct().ToList();
        Check(archives.Count > 0 && archives.All(File.Exists), "Arşiv dosyaları diskte (" + archives.Count + ")");
        var leftovers = files.Where(f => f.Step == "YEDEK" && !string.IsNullOrEmpty(f.ArchivePath) && File.Exists(f.ArchivePath)).ToList();
        Check(leftovers.Count == 0, "Geçici .bak dosyaları silinmiş" + (leftovers.Count == 0 ? "" : " (kalan: " + leftovers[0].ArchivePath + ")"));
        Check(files.All(f => f.Result == "Tamam"), "Yedek adımlarının hepsi 'Tamam'");
        Check(Events("Guard.BackupFailed", before).Count == 0, "'Yedekleme başarısız' olayı yok");
        Check(Events("Sql.BackupDone", before).Count > 0, "'SQL yedeği alındı' olayı yazıldı");
    }

    // ================================================================ Servisi durdur / başlat

    static void RestartTests()
    {
        Title("SERVİSİ ARAYÜZDEN DURDUR, WINDOWS'TAN BAŞLAT");
        var info = Send<ServiceInfoDto>(Commands.ServiceInfo, null);
        var beforeStop = ProcSnap.Take(info.ProcessId);
        Info("Durdurmadan önce: " + beforeStop);
        Check(beforeStop.WorkingMb < 160, "Tüm testlerden sonra servis belleği makul (" + beforeStop.WorkingMb + " MB; sınır 160)");
        if (startSnap != null && startSnap.Pid == beforeStop.Pid)
            Info("Test boyunca değişim: bellek " + Delta(beforeStop.WorkingMb - startSnap.WorkingMb) + " MB, handle " + Delta(beforeStop.Handles - startSnap.Handles) + ", iş parçacığı " + Delta(beforeStop.Threads - startSnap.Threads)
                 + (beforeStop.CpuSec >= 0 && startSnap.CpuSec >= 0 ? ", CPU " + (beforeStop.CpuSec - startSnap.CpuSec).ToString("0.0", CultureInfo.InvariantCulture) + " sn / " + (int)(beforeStop.At - startSnap.At).TotalMinutes + " dk" : ""));
        long before = LastId();
        var portsBefore = Send<PortRestrictionStateDto>(Commands.PortRestrictGet, null).Items.Count;
        var bansBefore = Send<BanListResult>(Commands.BanList, null).Banned.Count;

        Check(!Raw(Commands.ServiceStop, new PasswordConfirmRequest { Password = "yanlis-" + Guid.NewGuid().ToString("N") }).Success, "Yanlış şifreyle servis durdurulamıyor");
        Check(Raw(Commands.ServiceStop, new PasswordConfirmRequest { Password = loginPassword }).Success, "Durdurma isteği kabul edildi");
        bool exited = WaitUntil(() => { try { using (Process.GetProcessById(info.ProcessId)) return false; } catch (ArgumentException) { return true; } }, 90, true);
        Check(exited, "Servis işlemi kapandı");
        Thread.Sleep(3000);
        using (var sc = new System.ServiceProcess.ServiceController(GuardConstants.ServiceName))
        {
            Check(sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped, "Windows'ta durum: durdu (kendiliğinden yeniden başlamadı)");
            Check(FindBanRule(KeepIp) != null, "Servis kapalıyken ban kuralı firewall'da duruyor");
            try
            {
                sc.Start();
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(90));
                Check(true, "Yönetici servisi Windows'tan başlatabildi");
            }
            catch (Exception ex)
            {
                Fail("Servis başlatılamadı: " + ex.Message);
                return;
            }
        }
        var sw = Stopwatch.StartNew();
        Check(Relogin(180), "Servise yeniden bağlanıldı (" + (int)sw.Elapsed.TotalSeconds + " sn)");

        var after = Send<ServiceInfoDto>(Commands.ServiceInfo, null);
        Check(after.ProcessId != info.ProcessId, "Yeni işlem (PID " + info.ProcessId + " → " + after.ProcessId + ")");
        var lic = Send<LicenseInfoDto>(Commands.LicenseGet, null);
        Check(lic.Active, "Açılışta lisans hemen geçerli (kayıtlı imzalı cevapla): " + lic.StatusText);
        var stop = WaitEvent("Guard.ServiceStopped", before, 30);
        Check(stop != null, "'Servis durduruldu' olayı" + MailText(stop) + (stop == null ? "" : ": " + Short(stop.Summary)));
        Thread.Sleep(5000);
        Check(Events("Guard.UnexpectedStop", before).Count == 0, "Düzgün kapanış 'beklenmedik kapanma' sayılmadı");
        Check(Send<PortRestrictionStateDto>(Commands.PortRestrictGet, null).Items.Count == portsBefore, "Port kısıtları yerinde (" + portsBefore + ")");
        var bans = Send<BanListResult>(Commands.BanList, null);
        Check(bans.Banned.Count == bansBefore && bans.Banned.Any(x => x.IpAddress == KeepIp) && FindBanRule(KeepIp) != null, "Banlar yerinde (" + bans.Banned.Count + "), " + KeepIp + " firewall'da");
        Check(bans.FirewallOk, "Açılış firewall kontrolü tutarlı");

        // Açılıştan sonra izleme çalışıyor mu
        long b2 = LastId();
        for (int i = 0; i < 3; i++) Run("net", @"use \\127.0.0.1\IPC$ /user:sgat_restart YanlisSifre123");
        Run("net", @"use \\127.0.0.1\IPC$ /delete /y");
        Check(WaitEvent("Net.LoginFailed", b2, 30) != null, "Yeniden başladıktan sonra olaylar yakalanıyor");
        startSnap = ProcSnap.Take(after.ProcessId);
    }

    static string Delta(long v) { return (v >= 0 ? "+" : "") + v; }

    static bool Relogin(int seconds)
    {
        return WaitUntil(() =>
        {
            try
            {
                client.ForgetSession();
                return client.LoginAsync(loginUser, loginPassword).Result.Success;
            }
            catch { return false; }
        }, seconds, true);
    }

    // ================================================================ Son kontroller

    static void FinalChecks()
    {
        Title("SON KONTROLLER (kaynak kullanımı, servis logu, mail kuyruğu, temizlik)");
        if (!client.PingAsync().Result.Success) Relogin(120);

        // Test banlarını kaldır
        BanCleanup();
        var bans = Send<BanListResult>(Commands.BanList, null);
        Check(!bans.Banned.Any(x => x.IpAddress.StartsWith("203.0.113.") || x.IpAddress.StartsWith("198.51.100.")) && FindBanRule(KeepIp) == null, "Test banları kaldırıldı, firewall'da iz yok");
        Check(bans.FirewallOk, "Firewall Kontrol: tutarlı");

        // Servis az önce öldürülüp yeniden başladı: koruma durumu açılıştan birkaç saniye sonra yazılır
        ServiceInfoDto info = null;
        WaitUntil(() => { info = Send<ServiceInfoDto>(Commands.ServiceInfo, null); return info.Protection != null && info.Protection.Contains("Servis izinleri: korunuyor"); }, 60);
        var end = ProcSnap.Take(info.ProcessId);
        Info("Servis işlemi (son): " + end + " | veritabanı " + (info.DatabaseSizeBytes / 1024 / 1024) + " MB");
        Check(end.WorkingMb < 400, "Bellek makul (" + end.WorkingMb + " MB)");
        Check(end.Handles < 5000 && end.Threads < 200, "Handle / iş parçacığı sayısı makul (" + end.Handles + " / " + end.Threads + ")");
        Check(info.Protection != null && info.Protection.Contains("Servis izinleri: korunuyor"), "Servis koruması yerinde: " + info.Protection);

        // Test boyunca yazılan olaylar
        var types = Query("SELECT EventType, COUNT(*) FROM EventLog WHERE Id > " + fullStartId + " GROUP BY EventType ORDER BY 2 DESC");
        Info("Test boyunca " + types.Sum(r => int.Parse(r[1])) + " olay: " + string.Join(", ", types.Select(r => r[0] + " " + r[1])));
        // Teşhis: son 4 günde en çok tekrarlayan "firewall kuralı değişti" olayları (günlük raporda 1000+ görüldü: kaynağı ne?)
        try
        {
            var fw = Query("SELECT Summary, COUNT(*), MAX(TimeUtc) FROM EventLog WHERE EventType = 'Win.FirewallRuleChanged' AND TimeUtc >= '" + Db(DateTime.UtcNow.AddDays(-4)) + "' GROUP BY Summary ORDER BY 2 DESC LIMIT 10");
            if (fw.Count > 0) Info("Son 4 günün firewall kuralı olayları (en çok tekrarlayan 10):");
            foreach (var r in fw) Info("  " + r[1] + "x | son " + r[2] + " | " + Short(r[0], 150));
            var hours = Query("SELECT substr(TimeUtc, 1, 13), COUNT(*) FROM EventLog WHERE EventType = 'Win.FirewallRuleChanged' AND TimeUtc >= '" + Db(DateTime.UtcNow.AddDays(-4)) + "' GROUP BY 1 ORDER BY 2 DESC LIMIT 5");
            if (hours.Count > 0) Info("  en yoğun saatler (UTC): " + string.Join(", ", hours.Select(h => h[0] + " → " + h[1])));
        }
        catch (Exception ex) { Info("Firewall olay dökümü alınamadı: " + ex.Message); }

        var moduleErrors = types.FirstOrDefault(r => r[0] == "Guard.ModuleError");
        Check(moduleErrors == null, "'Bir modül art arda hata veriyor' olayı yok" + (moduleErrors == null ? "" : " (" + moduleErrors[1] + ")"));
        var integrity = types.FirstOrDefault(r => r[0] == "Guard.LogIntegrity");
        Check(integrity == null, "'Log bütünlüğü bozuldu' olayı yok");

        // Mail kuyruğu
        Info("Mail kuyruğunun boşalması bekleniyor (en fazla 4 dk)...");
        WaitUntil(() => Send<ServiceInfoDto>(Commands.ServiceInfo, null).MailPending == 0, 240, true);
        string since = Db(fullStartUtc);
        var mail = Query("SELECT Status, COUNT(*) FROM MailQueue WHERE CreatedUtc >= '" + since + "' GROUP BY Status ORDER BY Status");
        string[] names = { "bekliyor", "gönderildi", "BAŞARISIZ", "süresi doldu" };
        int total = mail.Sum(r => int.Parse(r[1])), sentCount = mail.Where(r => r[0] == "1").Sum(r => int.Parse(r[1]));
        Info("Test boyunca " + total + " mail: " + string.Join(", ", mail.Select(r => { int s = int.Parse(r[0]); return (s >= 0 && s < names.Length ? names[s] : "durum " + s) + " " + r[1]; })));
        if (total == 0) Warn("Hiç mail kuyruğa girmedi (mail ayarı / alıcı / kurallar kapalı olabilir).");
        else Check(sentCount == total, "Kuyruğa giren maillerin hepsi gönderildi (" + sentCount + "/" + total + ")");
        foreach (var r in Query("SELECT Subject, Attempts, IFNULL(LastError, '') FROM MailQueue WHERE CreatedUtc >= '" + since + "' AND Status <> 1 ORDER BY Id DESC LIMIT 8"))
            Info("Gitmeyen mail: " + Short(r[0]) + " | deneme " + r[1] + " | " + Short(r[2]));
        foreach (var r in Query("SELECT Subject FROM MailQueue WHERE CreatedUtc >= '" + since + "' ORDER BY Id LIMIT 60"))
            log.WriteLine("  mail: " + r[0]);

        // Servis logu
        ScanServiceLog(info.LogFolder);

        var v = Send<EventLogVerifyResult>(Commands.EventLogVerify, null);
        Check(v.ModifiedRows == 0 && v.Gaps == 0 && v.TailDeletedRows == 0, "Test sonunda olay logu bütünlüğü tam (" + v.CheckedRows + " satır)");
        if (v.ModifiedRows != 0 || v.Gaps != 0 || v.TailDeletedRows != 0)
        {
            Info("Bütünlük: değişmiş " + v.ModifiedRows + ", boşluk " + v.Gaps + ", sondan silinen " + v.TailDeletedRows + ", imzasız " + v.UnsignedRows
                 + " | sorunlu Id'ler: " + string.Join(", ", (v.ProblemIds ?? new List<long>()).Take(10)));
            foreach (var r in Query("SELECT Id, TimeUtc, EventType, Summary FROM EventLog ORDER BY Id DESC LIMIT 6")) Info("  son satırlar: " + string.Join(" | ", r.Select(x => Short(x, 70))));
            Thread.Sleep(3000);
            var v2 = Send<EventLogVerifyResult>(Commands.EventLogVerify, null);
            Info("3 sn sonra tekrar: değişmiş " + v2.ModifiedRows + ", boşluk " + v2.Gaps + ", sondan silinen " + v2.TailDeletedRows + (v2.ModifiedRows + v2.Gaps + v2.TailDeletedRows == 0 ? " → geçici (doğrulama sırasında olay yazılmış)" : " → KALICI"));
        }

        // Doğrulama sürerken olay yazılırsa yanlış alarm çıkmamalı: arka planda olay üretip art arda doğrula
        var writer = new Thread(() => { var until = DateTime.UtcNow.AddSeconds(15); while (DateTime.UtcNow < until) { Run("net", @"use \\127.0.0.1\IPC$ /user:sgat_butunluk YanlisSifre123"); } Run("net", @"use \\127.0.0.1\IPC$ /delete /y"); }) { IsBackground = true };
        writer.Start();
        int falseAlarms = 0, rounds = 0;
        while (writer.IsAlive || rounds < 3)
        {
            var x = Send<EventLogVerifyResult>(Commands.EventLogVerify, null);
            if (x.ModifiedRows != 0 || x.Gaps != 0 || x.TailDeletedRows != 0) falseAlarms++;
            rounds++;
            if (rounds > 200) break;
        }
        writer.Join(20000);
        Check(falseAlarms == 0, "Olaylar yazılırken yapılan " + rounds + " doğrulamada yanlış 'log değişmiş' alarmı yok (" + falseAlarms + ")");
        var lic = Send<LicenseInfoDto>(Commands.LicenseGet, null);
        Check(lic.Active && lic.LastError == null, "Test sonunda lisans geçerli (" + lic.StatusText + ", son kontrol " + lic.LastCheck + ")");
    }

    static void ScanServiceLog(string folder)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        try
        {
            foreach (string file in Directory.GetFiles(folder, "app_*.log").Where(f => File.GetLastWriteTimeUtc(f) >= fullStartUtc.AddMinutes(-1)).OrderBy(f => f))
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    bool inWindow = false, lastWasError = false;
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        DateTimeOffset t = default(DateTimeOffset);
                        bool stamped = line.Length > 30 && DateTimeOffset.TryParseExact(line.Substring(0, 30), "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
                        if (stamped)
                        {
                            inWindow = t.UtcDateTime >= fullStartUtc;
                            lastWasError = false;
                            if (!inWindow) continue;
                            if (line.Contains("[ERROR]")) { errors.Add(line); lastWasError = true; }
                            else if (line.Contains("[WARN ]")) warnings.Add(line);
                        }
                        else if (inWindow && lastWasError && errors.Count > 0 && errors[errors.Count - 1].Length < 1500)
                            errors[errors.Count - 1] += "\r\n      " + line.Trim(); // istisna ayrıntısı
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Warn("Servis logu okunamadı: " + ex.Message);
            return;
        }
        // Testin bilerek ürettiği hata (olmayan sürücüye yedek) beklenen bir ERROR satırıdır
        int expected = errors.RemoveAll(e => e.Contains("SgYedekYok"));
        if (expected > 0) Info("Beklenen ERROR satırı (testin 'hedef sürücü yok' denemesi): " + expected);
        Check(errors.Count == 0, "Servis logunda test boyunca beklenmeyen ERROR satırı yok (" + errors.Count + ")");
        int compare = warnings.Count(w => w.Contains("karşılaştırma yapılamadı"));
        Check(compare == 0, "SQL değişiklik karşılaştırması hatasız çalışıyor (" + compare + " 'karşılaştırma yapılamadı' uyarısı)");
        foreach (string e in errors.Take(40)) Line("    " + e, ConsoleColor.Red);
        Info("Servis logunda " + warnings.Count + " WARN satırı" + (warnings.Count > 0 ? " (bir kısmı testin reddedilen isteklerinden; hepsi dosyada):" : ""));
        foreach (var g in warnings.GroupBy(w => Regex.Replace(w.Substring(Math.Min(w.Length, 31)), @"\d+", "#")).OrderByDescending(g => g.Count()).Take(40))
            Info("  " + g.Count() + "x " + Short(g.First().Substring(Math.Min(g.First().Length, 31)), 220));
    }

    // ================================================================ Yardımcılar

    static GuardResult Raw(string command, object payload)
    {
        return client.SendAsync(command, payload, TimeSpan.FromMinutes(2)).Result;
    }

    static string Db(DateTime utc) { return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture); }

    /// <summary>SQLite sorgu ekranı üzerinden SELECT (servisin kendi veritabanı).</summary>
    static List<List<string>> Query(string sql)
    {
        var r = Send<SqliteExecuteResultDto>(Commands.SqliteExecute, new SqliteExecuteRequest { Sql = sql });
        return r.ResultSets == null || r.ResultSets.Count == 0 ? new List<List<string>>() : r.ResultSets[0].Rows;
    }
}
