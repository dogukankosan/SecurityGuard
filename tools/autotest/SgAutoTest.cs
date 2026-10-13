using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Ipc;

// SecurityGuard otomatik özellik testi (VM'de, YÖNETİCİ olarak, UI klasöründen çalıştırılır: UI\SgAutoTest.exe).
// "-tam": tüm bölümler + lisans, ban, Windows / SQL olayları, taramalar, raporlar, yedek, servis durdur-başlat (SgAutoTest.Full.cs).
// Arayüzün kullandığı pipe üzerinden servise bağlanır. Sadece test için oluşturduğu şeylere dokunur:
//  - Port kısıtı: KULLANILMAYAN TCP 65010 portunda "OtoTest" kısıtı (RDP / SQL'e dokunulmaz), sonunda silinir.
//  - Tuzak dosya: C:\SG_TuzakTest klasörü + SG_TuzakTest paylaşımı, sonunda silinir.
partial class SgAutoTest
{
    const int TestPort = 65010;
    const string TestName = "OtoTest - kullanılmayan port 65010";
    const string ShareName = "SG_TuzakTest";
    const string ShareDir = @"C:\SG_TuzakTest";

    static GuardClient client;
    static string outDir;

    /// <summary>Program kökündeki Temp klasörü (UI\ klasörünün bir üstü).</summary>
    static readonly string TempRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Temp"));

    /// <summary>14 günden eski test çıktılarını siler (geçici klasör dolmasın).</summary>
    static void CleanOldOutputs(string root)
    {
        try
        {
            foreach (string dir in Directory.GetDirectories(root))
                if (Directory.GetCreationTimeUtc(dir) < DateTime.UtcNow.AddDays(-14)) Directory.Delete(dir, true);
        }
        catch { }
    }
    static int pass, fail;
    static StreamWriter log;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Tüm çıktılar (sonuç, rapor önizlemeleri) programın kendi Temp klasöründe, bu çalıştırmaya ait tek bir klasöre yazılır
        // (C:\SecurityGuard\Temp\SgAutoTest\<tarih_saat>): Windows'un %TEMP% / AppData klasörü kullanılmaz.
        string root = Path.Combine(TempRoot, "SgAutoTest");
        outDir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(outDir);
        CleanOldOutputs(root);
        string file = Path.Combine(outDir, "sonuc.txt");
        log = new StreamWriter(file, false, new UTF8Encoding(true)) { AutoFlush = true };
        bool skipLong = args.Contains("-kisa");
        bool onlyCanary = args.Contains("-tuzak");
        try
        {
            client = new GuardClient();
            Console.Write("SecurityGuard yönetici kullanıcı adı: ");
            string user = Console.ReadLine();
            Console.Write("Şifre: ");
            string pw = ReadPassword();
            loginUser = user; loginPassword = pw;
            var login = client.LoginAsync(user, pw).Result;
            if (!login.Success) { Line("Giriş başarısız: " + login.ErrorMessage, ConsoleColor.Red); return 2; }
            Line("Giriş tamam. Sonuçlar: " + file, ConsoleColor.Gray);

            if (args.Contains("-tam"))
            {
                FullTests(args);
                return fail;
            }
            if (args.Contains("-koruma"))
            {
                ProtectionTests();
                return fail;
            }
            if (args.Contains("-servis"))
            {
                ServiceTests();
                return fail;
            }
            if (args.Contains("-oturum"))
            {
                SessionTests();
                return fail;
            }
            if (args.Contains("-disban"))
            {
                ExternalBanTests(args);
                return fail;
            }
            if (args.Contains("-yazilim"))
            {
                SoftwareTests();
                return fail;
            }
            if (args.Contains("-ag"))
            {
                NetworkTests();
                return fail;
            }
            if (!onlyCanary) PortTests(skipLong);
            CanaryTests();
            NetworkTests();
        }
        catch (Exception ex)
        {
            Fail("BEKLENMEYEN HATA: " + ex);
        }
        finally
        {
            Cleanup();
            Line("", ConsoleColor.Gray);
            Line("SONUÇ: " + pass + " başarılı, " + fail + " hatalı", fail == 0 ? ConsoleColor.Green : ConsoleColor.Red);
            log.Dispose();
            Console.WriteLine("Sonuç dosyaları: " + outDir);
            try { Process.Start("explorer.exe", "\"" + outDir + "\""); } catch { } // klasörü aç: dosyalar kolay bulunsun
        }
        return fail;
    }

    // ================================================================ Port kısıtı

    static void PortTests(bool skipLong)
    {
        Title("PORT KISITLARI (test portu TCP " + TestPort + ")");
        DeleteTestRestriction();
        var state = Send<PortRestrictionStateDto>(Commands.PortRestrictGet, null);
        var rdp = state.Templates.FirstOrDefault(t => t.Name.Contains("RDP"));
        Check(rdp != null, "RDP şablonu var: " + (rdp == null ? "-" : rdp.Protocol + " " + rdp.Ports));
        Check(state.Templates.Any(t => t.Name.StartsWith("SQL Server")), "SQL şablonu var: " + string.Join(", ", state.Templates.Where(t => t.Name.StartsWith("SQL")).Select(t => t.Name + " " + t.Ports)));
        if (state.FirewallOffWarning != null) Line("  UYARI: " + state.FirewallOffWarning, ConsoleColor.Yellow);

        // Kilitlenme koruması (KAYDETMEZ, sadece kontrol): RDP portunda kendi IP'si listede değilse ret
        if (rdp != null)
        {
            var chk = Send<PortRestrictionCheckDto>(Commands.PortRestrictCheck, new PortRestrictionSaveRequest
            {
                ClientAddress = "198.51.100.77",
                Item = new PortRestrictionDto { Name = "kontrol", Protocol = rdp.Protocol, Ports = rdp.Ports, Addresses = "203.0.113.5", AllowLan = false, Enabled = true }
            });
            Check(chk.Error != null && chk.Error.Contains("bağlanamazsınız"), "Kendi RDP IP'si listede yoksa kayıt reddediliyor (" + Short(chk.Error) + ")");
        }

        var item = new PortRestrictionDto { Name = TestName, Protocol = "TCP", Ports = TestPort.ToString(), Addresses = "203.0.113.5  # test", AllowLan = true, Enabled = true };
        var start = LastId();

        // Kaydet → onay bekliyor → hemen geri al
        long id = Send<IdRequest>(Commands.PortRestrictSave, new PortRestrictionSaveRequest { Item = item }).Id;
        var rule = FwRule(RuleName(id));
        Check(rule != null, "Kaydet: firewall kuralı oluştu (" + RuleName(id) + ")");
        if (rule != null)
        {
            Check(rule.Ports == TestPort.ToString() && rule.Action == 0, "Kural: gelen, ENGELLE, port " + rule.Ports);
            Check(!rule.Remote.Contains("203.0.113.5,") && rule.Remote.Contains("203.0.113.6-"), "İzinli 203.0.113.5 engel listesinde yok, komşusu engelli");
            Check(!rule.Remote.Contains("192.168.0.0") && !rule.Remote.Contains("10.0.0.0-"), "İç ağ engel listesinde yok");
        }
        Check(Item(id).State == "Onay bekliyor", "Durum: onay bekliyor");
        Send<object>(Commands.PortRestrictRevert, new IdRequest { Id = id });
        Check(FwRule(RuleName(id)) == null && !Item(id).Enabled, "Hemen geri al: kural silindi, kısıt kapandı");

        // Kaydet + onayla
        item.Id = id;
        Send<IdRequest>(Commands.PortRestrictSave, new PortRestrictionSaveRequest { Item = item });
        Send<object>(Commands.PortRestrictConfirm, new IdRequest { Id = id });
        Check(Item(id).State == "Uygulandı", "Onayla: durum uygulandı");
        Check(WaitEvent("Guard.PortRestrictionChanged", start, 10) != null, "'Port kısıtı uygulandı' olayı (mail)");

        // Kurcalama: kuralı dışarıdan sil → servis geri yazmalı
        var tamperStart = LastId();
        DeleteFwRule(RuleName(id));
        Line("  Kural wf.msc'den siliniyormuş gibi silindi, servisin geri yazması bekleniyor...", ConsoleColor.Gray);
        bool back = WaitUntil(() => FwRule(RuleName(id)) != null, 30);
        Check(back, "Silinen kural geri yazıldı");
        Check(WaitEvent("Guard.PortRuleTampered", tamperStart, 15) != null, "'Port kısıtı kuralı silindi/değiştirildi' kritik olayı (mail)");

        // Onaylanmayan düzenleme → 5 dk sonra önceki hale dönmeli
        if (!skipLong)
        {
            var editStart = LastId();
            var edited = Item(id);
            edited.Addresses += "\r\n198.51.100.10";
            Send<IdRequest>(Commands.PortRestrictSave, new PortRestrictionSaveRequest { Item = edited });
            Check(FwRule(RuleName(id)).Remote.Contains("198.51.100.11-"), "Düzenleme uygulandı (198.51.100.10 izinli)");
            Line("  Onaylanmadı; servisin 5 dakika sonra geri alması bekleniyor (yaklaşık 5,5 dk)...", ConsoleColor.Gray);
            bool reverted = WaitUntil(() => Item(id).PendingUntilUtc == null, 400, true);
            var after = Item(id);
            Check(reverted && after.Enabled && !after.Addresses.Contains("198.51.100.10"), "5 dk sonra önceki (onaylı) hale dönüldü");
            Check(WaitEvent("Guard.PortRestrictionReverted", editStart, 10) != null, "'Onaylanmadığı için geri alındı' olayı (mail)");
        }
        else Line("  (-kisa: 5 dakikalık geri alma testi atlandı)", ConsoleColor.Gray);

        Send<object>(Commands.PortRestrictDelete, new IdRequest { Id = id });
        Check(FwRule(RuleName(id)) == null, "Sil: kural kaldırıldı");

        // Ülke kısıtı: sadece Türkiye (test portunda)
        Line("  " + (state.GeoInfo ?? "ülke veritabanı bilgisi yok") + (state.GeoWarning != null ? " | UYARI: " + state.GeoWarning : ""), ConsoleColor.Gray);
        Check(state.Countries.Count > 200 && state.Countries[0].Code == "TR", "Ülke listesi geldi (" + state.Countries.Count + " ülke, Türkiye başta)");
        var geo = new PortRestrictionDto { Name = TestName, Protocol = "TCP", Ports = TestPort.ToString(), Addresses = "", Countries = "TR", AllowLan = true, Enabled = true };
        var sw = Stopwatch.StartNew();
        long gid = Send<IdRequest>(Commands.PortRestrictSave, new PortRestrictionSaveRequest { Item = geo }).Id;
        long ms = sw.ElapsedMilliseconds;
        var parts = Enumerable.Range(1, 12).Select(i => FwRule(RuleName(gid) + (i == 1 ? "" : "-" + i))).Where(r => r != null).ToList();
        Check(parts.Count >= 4, "Sadece Türkiye: " + parts.Count + " parça kural yazıldı (" + ms + " ms)");
        string all = string.Join("", parts.Select(r => r.Remote));
        Check(Blocks(all, "8.8.8.8") && Blocks(all, "1.1.1.1"), "8.8.8.8 / 1.1.1.1 (yurt dışı) engel listesinde");
        Check(!Blocks(all, "85.105.0.10") && !Blocks(all, "78.186.20.5") && !Blocks(all, "192.168.1.5"), "Türk Telekom IP'leri ve iç ağ engelli değil");
        Send<object>(Commands.PortRestrictRevert, new IdRequest { Id = gid });
        Check(Enumerable.Range(1, 12).All(i => FwRule(RuleName(gid) + (i == 1 ? "" : "-" + i)) == null), "Geri al: tüm parça kurallar silindi");
        Send<object>(Commands.PortRestrictDelete, new IdRequest { Id = gid });
    }

    static PortRestrictionDto Item(long id)
    {
        return Send<PortRestrictionStateDto>(Commands.PortRestrictGet, null).Items.First(i => i.Id == id);
    }

    /// <summary>Firewall uzak adres listesi (a, a-b, a/maske) IPv4 adresi kapsıyor mu.</summary>
    static bool Blocks(string remote, string ip)
    {
        uint x = V4(ip);
        foreach (var part in remote.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Contains(":")) continue;
            int dash = part.IndexOf('-'), slash = part.IndexOf('/');
            uint a, b;
            if (dash > 0) { a = V4(part.Substring(0, dash)); b = V4(part.Substring(dash + 1)); }
            else if (slash > 0)
            {
                a = V4(part.Substring(0, slash));
                string m = part.Substring(slash + 1);
                uint mask = m.Contains(".") ? V4(m) : (m == "0" ? 0u : uint.MaxValue << (32 - int.Parse(m)));
                a &= mask; b = a | ~mask;
            }
            else a = b = V4(part);
            if (x >= a && x <= b) return true;
        }
        return false;
    }

    static uint V4(string s)
    {
        var b = System.Net.IPAddress.Parse(s.Trim()).GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    static string RuleName(long id) { return "SecurityGuard-Restrict-" + id.ToString("000") + "-TCP"; }

    static void DeleteTestRestriction()
    {
        foreach (var i in Send<PortRestrictionStateDto>(Commands.PortRestrictGet, null).Items.Where(i => i.Name == TestName))
            Send<object>(Commands.PortRestrictDelete, new IdRequest { Id = i.Id });
    }

    // ================================================================ Servis koruması

    static string loginUser, loginPassword;

    static void ProtectionTests()
    {
        Title("SERVİS KORUMASI");
        var info = Send<ServiceInfoDto>(Commands.ServiceInfo, null);
        Line("  Koruma durumu: " + info.Protection, ConsoleColor.Gray);
        Check(info.Protection != null && info.Protection.Contains("Servis izinleri: korunuyor"), "Servis izinleri korunuyor");
        Check(info.Protection != null && info.Protection.Contains("Veri klasörleri: korunuyor"), "Veri klasörleri korunuyor");

        // 1) Windows'tan durdurma / ayar değiştirme
        var stop = RunOut("sc.exe", "stop " + SecurityGuard.Shared.GuardConstants.ServiceName);
        Check(stop.Item1 == 5, "sc stop reddedildi (erişim engellendi, kod " + stop.Item1 + ")");
        if (stop.Item1 != 5)
        {
            Fail("Koruma uygulanmamış görünüyor: sc config / sc delete denemeleri ATLANDI (servisi bozmamak için). Test durduruldu.");
            return;
        }
        var config = RunOut("sc.exe", "config " + SecurityGuard.Shared.GuardConstants.ServiceName + " start= demand");
        Check(config.Item1 == 5, "sc config reddedildi (kod " + config.Item1 + ")");
        var del = RunOut("sc.exe", "delete " + SecurityGuard.Shared.GuardConstants.ServiceName);
        Check(del.Item1 == 5, "sc delete reddedildi (kod " + del.Item1 + ")");

        // 2) Veri klasörüne yazma / silme
        string dbDir = Path.GetDirectoryName(info.DatabaseFile);
        Check(!CanWrite(dbDir), "Database klasörüne yönetici yazamıyor");
        Check(!CanWrite(info.LogFolder), "Logs klasörüne yönetici yazamıyor");
        Check(Directory.GetFiles(info.LogFolder).Length > 0, "Loglar okunabiliyor");

        // 3) Servis izinlerini açma (sc sdset) → geri yazılmalı + kritik olay
        long before = LastId();
        var sdset = RunOut("sc.exe", "sdset " + SecurityGuard.Shared.GuardConstants.ServiceName + " D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)");
        Check(sdset.Item1 == 0, "Yönetici izinleri açabildi (kurtarma yolu açık: sc sdset)");
        Line("  İzinler Windows varsayılanına açıldı; servisin 1 dk içinde geri yazması bekleniyor...", ConsoleColor.Gray);
        var ev = WaitEventShow("Guard.ServiceTamper", before, 90);
        Check(ev != null, "İzin değişikliği → kritik olay" + MailText(ev));
        Check(RunOut("sc.exe", "sdshow " + SecurityGuard.Shared.GuardConstants.ServiceName).Item2.Contains("CCLCSWRPLOCRRCWD;;;BA"), "Koruma geri yazıldı (sdshow)");

        // 4) Logs klasörünün sahipliğini alıp yetki açma → geri yazılmalı + kritik olay
        before = LastId();
        RunOut("takeown.exe", "/f \"" + info.LogFolder + "\"");
        var grant = RunOut("icacls.exe", "\"" + info.LogFolder + "\" /grant *S-1-5-32-544:(OI)(CI)F");
        Check(grant.Item1 == 0 && CanWrite(info.LogFolder), "Sahiplik alınıp yönetici yetkisi açıldı (saldırgan senaryosu)");
        Line("  Logs klasörü açıldı; servisin 1 dk içinde geri kilitlemesi bekleniyor...", ConsoleColor.Gray);
        ev = WaitEventShow("Guard.DataTamper", before, 90);
        Check(ev != null && ev.Summary.Contains("Logs"), "Klasör izin değişikliği → kritik olay" + MailText(ev));
        Thread.Sleep(2000);
        Check(!CanWrite(info.LogFolder), "Logs klasörü tekrar kilitlendi");

        // 5) Servis işlemini öldürme → Windows yeniden başlatır, açılışta "beklenmedik kapanma"
        before = LastId();
        Line("  SecurityGuardService.exe öldürülüyor (taskkill /F); Windows kurtarma ayarı yeniden başlatmalı...", ConsoleColor.Gray);
        RunOut("taskkill.exe", "/F /IM SecurityGuardService.exe");
        Thread.Sleep(15000);
        bool back = WaitUntil(() =>
        {
            try
            {
                client.ForgetSession();
                var login = client.LoginAsync(loginUser, loginPassword).Result;
                return login.Success;
            }
            catch { return false; }
        }, 180, true);
        Check(back, "Servis kendiliğinden yeniden başladı ve bağlanıldı");
        if (back)
        {
            ev = WaitEvent("Guard.UnexpectedStop", before, 60);
            Check(ev != null, "Açılışta 'beklenmedik kapanma' kritik olayı" + MailText(ev) + (ev == null ? "" : ": " + ev.Summary));
        }
    }

    static bool CanWrite(string folder)
    {
        string f = Path.Combine(folder, "sgautotest_" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(f, "x");
            File.Delete(f);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    /// <summary>
    /// Komutu çalıştırır. Girdi kapalıdır (soru soran komut beklemez, hemen biter) ve en fazla 2 dakika beklenir:
    /// hiçbir dış komut testi kilitleyemez. Zaman aşımında çıkış kodu -999.
    /// </summary>
    static Tuple<int, string> RunOut(string exe, string args)
    {
        using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true }))
        {
            try { p.StandardInput.Close(); } catch { }
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(120000))
            {
                try { p.Kill(); } catch { }
                Line("  (zaman aşımı, komut sonlandırıldı: " + exe + " " + Short(args) + ")", ConsoleColor.Yellow);
                return Tuple.Create(-999, "zaman aşımı");
            }
            p.WaitForExit();
            return Tuple.Create(p.ExitCode, so.Result + se.Result);
        }
    }

    // ================================================================ Servis takibi (W32Time ile: zararsız, kısa süre durdurulur)

    const string TestSvc = "W32Time";

    static void ServiceTests()
    {
        Title("SERVİS TAKİBİ (Windows Saati / W32Time ile)");
        var original = Send<ServiceWatchStateDto>(Commands.ServiceWatchGet, null).Services.Where(s => s.Watched).ToList();
        Line("  İzlenen servisler: " + string.Join(", ", original.Select(s => s.Name + (s.AutoRestart ? " (otomatik başlat)" : ""))), ConsoleColor.Gray);
        Check(original.Any(s => s.Name.Equals("EventLog", StringComparison.OrdinalIgnoreCase)) && original.Any(s => s.Name.Equals("MpsSvc", StringComparison.OrdinalIgnoreCase)),
            "Önerilenler izleniyor (Olay Günlüğü, Firewall...)");
        try
        {
            Run("net", "start " + TestSvc);
            var items = original.Where(s => !s.Name.Equals(TestSvc, StringComparison.OrdinalIgnoreCase)).ToList();
            items.Add(new WatchedServiceDto { Name = TestSvc, Watched = true, AutoRestart = false });
            Send<ServiceWatchStateDto>(Commands.ServiceWatchSave, new ServiceWatchSaveRequest { Items = items });
            Thread.Sleep(20000); // ilk kontrol "çalışıyor" görsün

            long before = LastId();
            Run("net", "stop " + TestSvc);
            Line("  " + TestSvc + " durduruldu; 'servis durdu' bekleniyor (en fazla ~30 sn)...", ConsoleColor.Gray);
            var ev = WaitEvent("Svc.Stopped", before, 45);
            Check(ev != null && ev.Summary.Contains(TestSvc), "Servis durdu → kritik olay" + MailText(ev));
            if (ev != null) Line("  Olay: " + ev.Summary + "\r\n    " + (ev.Detail ?? "").Replace("\n", "\n    "), ConsoleColor.Gray);

            before = LastId();
            Run("net", "start " + TestSvc);
            ev = WaitEvent("Svc.Recovered", before, 45);
            Check(ev != null && ev.Summary.StartsWith("Servis tekrar çalışıyor"), "Tekrar başlatılınca 'düzeldi' olayı" + MailText(ev));

            // Otomatik yeniden başlatma
            items.First(i => i.Name == TestSvc).AutoRestart = true;
            Send<ServiceWatchStateDto>(Commands.ServiceWatchSave, new ServiceWatchSaveRequest { Items = items });
            before = LastId();
            Run("net", "stop " + TestSvc);
            Line("  Otomatik yeniden başlatma açık; servis durduruldu, SecurityGuard'ın ~30-45 sn içinde başlatması bekleniyor...", ConsoleColor.Gray);
            ev = WaitEventShow("Svc.Recovered", before, 120);
            Check(ev != null && ev.Summary.Contains("SecurityGuard tarafından yeniden başlatıldı"), "SecurityGuard servisi kendisi başlattı" + MailText(ev));
            using (var sc = new System.ServiceProcess.ServiceController(TestSvc))
                Check(sc.Status == System.ServiceProcess.ServiceControllerStatus.Running, "Servis gerçekten çalışıyor");
        }
        finally
        {
            Send<ServiceWatchStateDto>(Commands.ServiceWatchSave, new ServiceWatchSaveRequest { Items = original });
            Run("net", "start " + TestSvc);
            Line("Temizlik: izleme listesi eski haline getirildi, " + TestSvc + " çalışır durumda.", ConsoleColor.Gray);
        }
    }

    // ================================================================ RDP oturumu / mesai dışı (yarı otomatik: kullanıcı kopup bağlanır)

    static void SessionTests()
    {
        Title("RDP OTURUMU VE MESAİ DIŞI GİRİŞ");
        string sessionName = Environment.GetEnvironmentVariable("SESSIONNAME") ?? "";
        string me = Environment.UserDomainName + "\\" + Environment.UserName;
        if (!sessionName.StartsWith("RDP", StringComparison.OrdinalIgnoreCase))
        {
            Fail("Bu test RDP oturumunda çalıştırılmalı (şu an: " + (sessionName.Length == 0 ? "bilinmiyor" : sessionName) + "). Kendi PC'nden VM'e RDP ile bağlanıp tekrar çalıştırın.");
            return;
        }

        var original = Send<List<GeneralSettingDto>>(Commands.GeneralSettingsList, null)
            .Where(s => s.Key.StartsWith("workhours.", StringComparison.Ordinal)).ToDictionary(s => s.Key, s => s.Value);
        var originalHours = Send<WorkHoursDto>(Commands.WorkHoursGet, null);
        try
        {
            // 1) Açık oturum listede mi
            var list = Send<RdpSessionResult>(Commands.RdpSessionsList, new RdpSessionQuery { FromUtc = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") });
            var mine = list.Rows.FirstOrDefault(r => string.Equals(r.User, me, StringComparison.OrdinalIgnoreCase) && (r.State == "Açık" || r.State == "Bağlantı kopuk"));
            Check(mine != null, "Bu RDP oturumu geçmişte 'Açık' görünüyor" + (mine == null ? " (servis bu oturumdan sonra güncellendiyse bir kez kopup bağlanın)" : ": " + mine.Ips + " / " + mine.Country + " / başlangıç " + mine.StartUtc));
            int reconnectsBefore = mine == null ? 0 : mine.Reconnects;

            // 2) Mesaiyi geçici olarak şu anı dışarıda bırakacak şekilde ayarla
            var now = DateTime.Now;
            string start = now.AddHours(2).ToString("HH:00"), end = now.AddHours(3).ToString("HH:00");
            Send<List<GeneralSettingDto>>(Commands.GeneralSettingsSave, new GeneralSettingsSaveRequest { Values = new List<SettingValueDto>
            {
                new SettingValueDto { Key = "workhours.alarm", Value = "true" },
                new SettingValueDto { Key = "workhours.days", Value = "all" },
                new SettingValueDto { Key = "workhours.start", Value = start },
                new SettingValueDto { Key = "workhours.end", Value = end }
            } });
            Send<WorkHoursDto>(Commands.WorkHoursSave, new WorkHoursDto { Exceptions = originalHours.Exceptions.Where(e => !SameUser(e.User, me)).ToList() });
            Line("  Mesai geçici olarak her gün " + start + "-" + end + " yapıldı (şu an mesai dışı). Ayarlar test sonunda geri alınacak.", ConsoleColor.Gray);

            long before = LastId();
            Prompt("ŞİMDİ: RDP penceresini kapatın (X ile, oturumu kapatmadan), 10 sn sonra tekrar bağlanın. Bu pencere beklemeye devam eder.");
            var ev = WaitEventShow("Rdp.OffHoursLogin", before, 300);
            Check(ev != null && ev.Summary.IndexOf(Environment.UserName, StringComparison.OrdinalIgnoreCase) >= 0, "Mesai dışı yeniden bağlanma → uyarı" + MailText(ev));
            if (ev != null) Line("  Olay: " + ev.Summary + "\r\n    " + (ev.Detail ?? "").Replace("\n", "\n    "), ConsoleColor.Gray);

            list = Send<RdpSessionResult>(Commands.RdpSessionsList, new RdpSessionQuery { FromUtc = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") });
            mine = list.Rows.FirstOrDefault(r => string.Equals(r.User, me, StringComparison.OrdinalIgnoreCase) && r.State == "Açık");
            Check(mine != null && mine.Disconnects >= 1 && mine.Reconnects > reconnectsBefore && mine.OffHours, "Oturum satırı: kopma / yeniden bağlanma sayıldı, mesai dışı işaretli"
                  + (mine == null ? "" : " (kopma " + mine.Disconnects + ", yeniden bağlanma " + mine.Reconnects + ")"));

            // 3) İstisna: bu kullanıcı her zaman serbest → uyarı gelmemeli
            var exceptions = originalHours.Exceptions.Where(e => !SameUser(e.User, me)).ToList();
            exceptions.Add(new WorkHoursExceptionDto { User = me, Mode = "always", Note = "SgAutoTest" });
            Send<WorkHoursDto>(Commands.WorkHoursSave, new WorkHoursDto { Exceptions = exceptions });
            int reconnects = mine == null ? 0 : mine.Reconnects;
            before = LastId();
            Prompt("İSTİSNA TESTİ: Sizi 'her zaman serbest' yaptım. RDP penceresini tekrar kapatıp 10 sn sonra bağlanın.");
            bool reconnected = WaitUntil(() =>
            {
                var l = Send<RdpSessionResult>(Commands.RdpSessionsList, new RdpSessionQuery { FromUtc = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") });
                var m = l.Rows.FirstOrDefault(r => string.Equals(r.User, me, StringComparison.OrdinalIgnoreCase) && r.State == "Açık");
                return m != null && m.Reconnects > reconnects;
            }, 300, true);
            Thread.Sleep(3000);
            Check(reconnected && Events("Rdp.OffHoursLogin", before).Count == 0, "İstisnalı kullanıcı mesai dışında bağlandı → uyarı YOK" + (reconnected ? "" : " (yeniden bağlanma görülmedi)"));
        }
        finally
        {
            Send<List<GeneralSettingDto>>(Commands.GeneralSettingsSave, new GeneralSettingsSaveRequest { Values = original.Select(o => new SettingValueDto { Key = o.Key, Value = o.Value }).ToList() });
            Send<WorkHoursDto>(Commands.WorkHoursSave, new WorkHoursDto { Exceptions = originalHours.Exceptions });
            Line("Temizlik: mesai saatleri ve istisnalar eski haline getirildi (" + string.Join(", ", original.Select(o => o.Key.Substring(10) + "=" + o.Value)) + ").", ConsoleColor.Gray);
        }
    }

    static bool SameUser(string a, string me)
    {
        string sam = me.Substring(me.IndexOf('\\') + 1);
        return string.Equals(a, me, StringComparison.OrdinalIgnoreCase) || string.Equals(a, sam, StringComparison.OrdinalIgnoreCase);
    }

    static void Prompt(string text)
    {
        Line("", ConsoleColor.Gray);
        Line(">>> " + text, ConsoleColor.Yellow);
        try { Console.Beep(); } catch { }
    }

    // ================================================================ Program kurulumu / şüpheli servis

    const string FakeUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SgAutoTest_AnyDesk";
    const string TestServiceName = "SgAutoTestSvc";

    static void SoftwareTests()
    {
        Title("PROGRAM KURULUMU VE ŞÜPHELİ SERVİS");
        try
        {
            // 1) Sahte "AnyDesk" kurulum kaydı (gerçek program kurulmaz; sadece Programlar ve Özellikler listesine satır)
            long before = LastId();
            using (var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
            using (var k = hklm.CreateSubKey(FakeUninstallKey))
            {
                k.SetValue("DisplayName", "AnyDesk (SecurityGuard testi)");
                k.SetValue("DisplayVersion", "9.9.9");
                k.SetValue("Publisher", "SgAutoTest");
                k.SetValue("InstallLocation", @"C:\SgAutoTest\AnyDesk");
            }
            Line("  Sahte AnyDesk kaydı eklendi; program listesi 2 dakikada bir karşılaştırılıyor (en fazla ~3 dk)...", ConsoleColor.Gray);
            var ev = WaitEventShow("Win.RemoteToolInstalled", before, 200);
            Check(ev != null && ev.Summary.Contains("AnyDesk (SecurityGuard testi)"), "Uzaktan erişim programı kuruldu → kritik olay" + MailText(ev));
            if (ev != null) Line("  Olay ayrıntısı:\r\n    " + (ev.Detail ?? "").Replace("\n", "\n    "), ConsoleColor.Gray);

            // 2) Kaldırma
            before = LastId();
            using (var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                hklm.DeleteSubKeyTree(FakeUninstallKey, false);
            Line("  Kayıt silindi; 'program kaldırıldı' bekleniyor (en fazla ~3 dk)...", ConsoleColor.Gray);
            ev = WaitEventShow("Win.SoftwareRemoved", before, 200);
            Check(ev != null && ev.Summary.Contains("AnyDesk (SecurityGuard testi)"), "Program kaldırıldı → olay" + MailText(ev));

            // 3) Şüpheli servis (oluşturulur, hiç çalıştırılmaz, silinir)
            before = LastId();
            Run("sc.exe", "create " + TestServiceName + " binPath= \"C:\\Users\\Public\\sgautotest.exe\" start= demand DisplayName= \"SgAutoTest servis\"");
            Line("  Test servisi oluşturuldu (C:\\Users\\Public\\sgautotest.exe, çalıştırılmadı)...", ConsoleColor.Gray);
            ev = WaitEvent("Win.ServiceInstalled", before, 30);
            Check(ev != null && ev.Summary.StartsWith("ŞÜPHELİ servis kuruldu: SgAutoTest") && ev.Detail != null && ev.Detail.Contains("olağandışı klasör"),
                "Kullanıcı klasöründen çalışan servis → 'ŞÜPHELİ servis kuruldu'" + MailText(ev) + (ev == null ? "" : " (" + ev.Summary + ")"));
        }
        finally
        {
            try
            {
                using (var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                    hklm.DeleteSubKeyTree(FakeUninstallKey, false);
            }
            catch { }
            Run("sc.exe", "delete " + TestServiceName);
            Line("Temizlik: sahte kurulum kaydı ve test servisi silindi.", ConsoleColor.Gray);
        }
    }

    static EventLogRow WaitEventShow(string type, long afterId, int seconds)
    {
        EventLogRow found = null;
        WaitUntil(() => (found = Events(type, afterId).FirstOrDefault()) != null, seconds, true);
        return found;
    }

    // ================================================================ Ağ girişi (SMB) + Açık Portlar

    static void NetworkTests()
    {
        Title("AĞ GİRİŞİ (SMB) VE AÇIK PORTLAR");
        // Localhost'tan yanlış şifreyle SMB girişi (olmayan kullanıcı: hesap kilitlenmez; localhost korunduğu için ban oluşmaz)
        long before = LastId();
        for (int i = 0; i < 3; i++) Run("net", @"use \\127.0.0.1\IPC$ /user:sgtest_smb YanlisSifre123");
        Run("net", @"use \\127.0.0.1\IPC$ /delete /y");
        Line("  3 hatalı SMB girişi yapıldı; RDP eşi beklenip SMB olarak sayılması bekleniyor (~6 sn)...", ConsoleColor.Gray);
        var smb = WaitEvent("Net.LoginFailed", before, 20);
        Thread.Sleep(2000);
        int smbCount = Events("Net.LoginFailed", before).Count, rdpCount = Events("Rdp.LoginFailed", before).Count(r => r.IpAddress == "127.0.0.1");
        Check(smb != null && smbCount >= 3, "SMB hataları 'Ağ girişi başarısız (SMB / WinRM)' olarak sayıldı (" + smbCount + ")" + (smb == null ? "" : ": " + smb.Summary));
        Check(rdpCount == 0, "RDP olarak sayılmadı (" + rdpCount + ")");

        var ports = Send<OpenPortsDto>(Commands.OpenPortsGet, null);
        Line("  " + ports.Ports.Count + " dinleyen port" + (ports.FirewallWarning != null ? " | " + ports.FirewallWarning : "")
             + (ports.WatchedLogs.Count > 0 ? " | IIS logları: " + string.Join(", ", ports.WatchedLogs) : " | IIS logu yok"), ConsoleColor.Gray);
        foreach (var p in ports.Ports.Where(x => x.Exposure != "Kapalı" && x.Exposure != "Sadece bu sunucu").OrderBy(x => x.Protocol).ThenBy(x => x.Port))
            Line(string.Format("    {0} {1,-5} {2,-26} {3,-18} {4,-7} 24s:{5,-4} {6}", p.Protocol, p.Port, Short(p.Label ?? p.Process, 26), p.Exposure, p.Risk, p.Failures24h < 0 ? "-" : p.Failures24h.ToString(), Short(p.ExposureDetail, 60)), ConsoleColor.Gray);
        var rdp = ports.Ports.FirstOrDefault(p => p.Protocol == "TCP" && p.Label != null && p.Label.StartsWith("RDP"));
        Check(rdp != null, "RDP portu listede: " + (rdp == null ? "-" : rdp.Port + " / " + rdp.Exposure + " / risk " + rdp.Risk));
        var smbPort = ports.Ports.FirstOrDefault(p => p.Protocol == "TCP" && p.Port == 445);
        Check(smbPort == null || smbPort.Failures24h >= 3, "SMB (445) satırında son 24 saat hatalı giriş sayısı görünüyor (" + (smbPort == null ? "-" : smbPort.Failures24h.ToString()) + ")");
    }

    static string Short(string s, int n) { s = s ?? ""; return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

    // ================================================================ Tuzak dosya

    static void CanaryTests()
    {
        Title("TUZAK DOSYALAR (geçici paylaşım " + ShareName + ")");
        Directory.CreateDirectory(ShareDir);
        Run("net", "share " + ShareName + "=" + ShareDir + " /GRANT:Everyone,FULL");
        var state = Send<CanaryStateDto>(Commands.CanaryCheck, null);
        var folder = state.Folders.FirstOrDefault(f => string.Equals(f.Path, ShareDir, StringComparison.OrdinalIgnoreCase));
        Check(folder != null && folder.Files.Count == 3 && folder.State.StartsWith("İzleniyor"), "Paylaşım bulundu, 3 tuzak dosya kondu" + (folder == null ? "" : ": " + string.Join(", ", folder.Files.Select(f => f.Name))));
        foreach (var f in state.Folders) Line("  Klasör: " + f.Path + " | " + f.Source + " | " + f.State, ConsoleColor.Gray);
        Line("  Dosya sistemi denetimi: " + (state.FileAuditEnabled ? "AÇIK (mailde kullanıcı / program yazar)" : "KAPALI (mailde kullanıcı / program yazmaz)"), state.FileAuditEnabled ? ConsoleColor.Gray : ConsoleColor.Yellow);
        if (folder == null) return;
        string first = folder.Files.First(f => f.Name.StartsWith("!!")).Path;
        Check((File.GetAttributes(first) & FileAttributes.Hidden) != 0, "Dosyalar gizli");

        // Test butonu
        var t0 = LastId();
        Send<string>(Commands.CanaryTest, null);
        var ev = WaitEvent("Win.CanaryTouched", t0, 30);
        Check(ev != null && ev.Summary.StartsWith("TEST:"), "Test et → 'TEST: Tuzak dosyaya dokunuldu' olayı" + MailText(ev));

        // Okuma / kopyalama → alarm yok
        var t1 = LastId();
        File.ReadAllBytes(first);
        File.Copy(first, Path.Combine(outDir, "sg_kopya.tmp"), true);
        File.Delete(Path.Combine(outDir, "sg_kopya.tmp"));
        Thread.Sleep(8000);
        var noise = Events("Win.CanaryTouched", t1);
        Check(noise.Count == 0, "Okuma / kopyalama alarm üretmedi" + (noise.Count == 0 ? "" : ": " + noise[0].Summary + " | " + noise[0].Detail));

        // Fidye gibi ad değiştir (sonraki bildirim en erken 60 sn sonra gider)
        var t2 = LastId();
        File.Move(first, first + ".locked");
        Line("  Dosya adı .locked yapıldı; bildirim bekleniyor (iki bildirim arası en az 60 sn)...", ConsoleColor.Gray);
        ev = WaitEvent("Win.CanaryTouched", t2, 90);
        Check(ev != null && ev.Detail != null && ev.Detail.Contains(".locked"), "Ad değiştirme → kritik olay, yeni ad yazıyor" + MailText(ev));
        if (ev != null) Line("  Olay ayrıntısı:\r\n    " + (ev.Detail ?? "").Replace("\n", "\n    "), ConsoleColor.Gray);
        Check(WaitUntil(() => File.Exists(first), 10), "Tuzak dosya yeniden kondu");

        // Silme
        var t3 = LastId();
        string second = folder.Files.First(f => f.Name.StartsWith("~~")).Path;
        File.SetAttributes(second, FileAttributes.Normal);
        File.Delete(second);
        ev = WaitEvent("Win.CanaryTouched", t3, 90);
        Check(ev != null && ev.Detail != null && ev.Detail.Contains("silin"), "Silme → kritik olay" + MailText(ev) + (ev == null ? "" : " (" + ev.Summary + ")"));
        if (ev != null) Line("  Olay ayrıntısı:\r\n    " + (ev.Detail ?? "").Replace("\n", "\n    "), ConsoleColor.Gray);
    }

    // ================================================================ Temizlik

    static void Cleanup()
    {
        try { if (client != null && client.IsLoggedIn) DeleteTestRestriction(); } catch { }
        try
        {
            Run("net", "share " + ShareName + " /delete /y");
            if (client != null && client.IsLoggedIn) Send<CanaryStateDto>(Commands.CanaryCheck, null); // paylaşım kalkınca tuzaklar temizlenir
            if (Directory.Exists(ShareDir))
            {
                foreach (var f in Directory.GetFiles(ShareDir)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(ShareDir, true);
            }
            Line("Temizlik: test kısıtı, paylaşım ve klasör silindi.", ConsoleColor.Gray);
        }
        catch (Exception ex) { Line("Temizlik tamamlanamadı: " + ex.Message, ConsoleColor.Yellow); }
    }

    // ================================================================ Yardımcılar

    static T Send<T>(string command, object payload) where T : class
    {
        if (typeof(T) == typeof(object))
        {
            var r0 = client.SendAsync(command, payload, TimeSpan.FromMinutes(2)).Result;
            if (!r0.Success) throw new Exception(command + ": " + r0.ErrorMessage);
            return null;
        }
        var r = client.SendAsync<T>(command, payload, TimeSpan.FromMinutes(2)).Result;
        if (!r.Success) throw new Exception(command + ": " + r.ErrorMessage);
        return r.Value;
    }

    /// <summary>Şu ana kadarki son olay numarası: sonraki aramalar bundan büyük Id'lere bakar (zamana göre arama önceki adımın olayını yakalıyordu).</summary>
    static long LastId()
    {
        var res = Send<EventLogResult>(Commands.EventLogQuery, new EventLogQuery
        {
            FromUtc = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            MinSeverity = -1,
            MaxRows = 1
        });
        return res.Rows.Count == 0 ? 0 : res.Rows.Max(r => r.Id);
    }

    static List<EventLogRow> Events(string type, long afterId)
    {
        var res = Send<EventLogResult>(Commands.EventLogQuery, new EventLogQuery
        {
            FromUtc = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            MinSeverity = -1,
            Search = type,
            MaxRows = 200
        });
        return res.Rows.Where(r => r.EventType == type && r.Id > afterId).OrderBy(r => r.Id).ToList();
    }

    static EventLogRow WaitEvent(string type, long afterId, int seconds)
    {
        EventLogRow found = null;
        WaitUntil(() => (found = Events(type, afterId).FirstOrDefault()) != null, seconds);
        return found;
    }

    static bool WaitUntil(Func<bool> condition, int seconds, bool showProgress = false)
    {
        var end = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < end)
        {
            if (condition()) { if (showProgress) Console.WriteLine(); return true; }
            if (showProgress) Console.Write("\r  bekleniyor: " + (int)(end - DateTime.Now).TotalSeconds + " sn kaldı   ");
            Thread.Sleep(1000);
        }
        if (showProgress) Console.WriteLine();
        return condition();
    }

    static string MailText(EventLogRow ev)
    {
        return ev == null ? "" : ev.MailSent ? " [mail kuyruğa alındı]" : " [MAİL YOK: kural / mail ayarı kapalı olabilir]";
    }

    sealed class FwInfo { public string Ports, Remote; public int Action; }

    static FwInfo FwRule(string name)
    {
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
        try
        {
            foreach (dynamic r in policy.Rules)
                if (r.Name == name) return new FwInfo { Ports = r.LocalPorts, Remote = r.RemoteAddresses + ",", Action = r.Action };
            return null;
        }
        finally { Marshal.ReleaseComObject(policy); }
    }

    static void DeleteFwRule(string name)
    {
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
        try { policy.Rules.Remove(name); }
        finally { Marshal.ReleaseComObject(policy); }
    }

    static void Run(string exe, string args)
    {
        RunOut(exe, args);
    }

    static string ReadPassword()
    {
        var sb = new StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }

    static string Short(string s) { return s == null ? "-" : s.Length > 90 ? s.Substring(0, 90) + "…" : s; }

    static void Title(string t) { Line("", ConsoleColor.Gray); Line("=== " + t + " ===", ConsoleColor.Cyan); }

    static void Check(bool ok, string text)
    {
        if (ok) pass++; else fail++;
        Line((ok ? "[OK]   " : "[HATA] ") + text, ok ? ConsoleColor.Green : ConsoleColor.Red);
    }

    static void Fail(string text) { fail++; Line("[HATA] " + text, ConsoleColor.Red); }

    static void Line(string text, ConsoleColor color)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = old;
        log.WriteLine(text);
    }
}
