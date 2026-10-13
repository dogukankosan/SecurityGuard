using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core.Settings;

namespace SecurityGuardService.Checks.Performance
{
    /// <summary>
    /// Windows / RDP sunucusu performans önerileri. Sadece OKUR (registry, olay logu, dosya boyutları, sayaçlar);
    /// hiçbir dosyayı silmez, ayarı değiştirmez. Yazıcı / Print Spooler ile ilgili öneri verilmez (müşteriler yazıcı kullanıyor).
    /// </summary>
    internal sealed class WindowsPerformanceChecks
    {
        private const string Area = "Windows";
        private static readonly Regex PrinterWords = new Regex(@"print|spool|yazıcı|yazici|printer|scan|tarayıcı|canon|epson|brother|kyocera|ricoh|xerox|lexmark|konica|sharp|oki|zebra|\bhp\b|hewlett",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly CheckContext _ctx;

        private WindowsPerformanceChecks(CheckContext ctx)
        {
            _ctx = ctx;
        }

        public static void Run(CheckContext ctx)
        {
            new WindowsPerformanceChecks(ctx).RunAll();
        }

        private static string Id(string name)
        {
            return "perf.win." + name;
        }

        private void RunAll()
        {
            _ctx.Run(Id("disks"), Area, "Disk doluluğu", CheckDisks);
            _ctx.Run(Id("disklatency"), Area, "Disk gecikmesi", CheckDiskLatency);
            _ctx.Run(Id("diskoptimize"), Area, "Disk iyileştirme (SSD TRIM / defrag)", CheckDiskOptimize);
            _ctx.Run(Id("memory"), Area, "Bellek kullanımı", CheckMemory);
            _ctx.Run(Id("cpu"), Area, "İşlemci kullanımı", CheckCpu);
            _ctx.Run(Id("rdpsessions"), Area, "Kopmuş RDP oturumları", CheckDisconnectedSessions);
            _ctx.Run(Id("events"), Area, "Son 7 günün kritik Windows hataları", CheckCriticalEvents);
            _ctx.Run(Id("power"), Area, "Güç planı", CheckPowerPlan);
            _ctx.Run(Id("pagefile"), Area, "Sayfa dosyası (page file)", CheckPageFile);
            _ctx.Run(Id("antivirus"), Area, "Antivirüste SQL Server hariç tutmaları", CheckAntivirusExclusions);
            _ctx.Run(Id("temp"), Area, "Geçici dosyalar (temp)", CheckTempFolders);
            _ctx.Run(Id("wucache"), Area, "Windows Update önbelleği", CheckUpdateCache);
            _ctx.Run(Id("winsxs"), Area, "Windows bileşen deposu (WinSxS)", CheckComponentStore);
            _ctx.Run(Id("profiles"), Area, "Eski kullanıcı profilleri", CheckOldProfiles);
            _ctx.Run(Id("startup"), Area, "Tüm kullanıcılarda açılan başlangıç uygulamaları", CheckStartupApps);
            _ctx.Run(Id("services"), Area, "Sunucuda gereksiz servisler", CheckServices);
            _ctx.Run(Id("apps"), Area, "Sunucuya uygun olmayan uygulamalar", CheckSuspiciousApps);
            _ctx.Run(Id("internet"), Area, "İnternet gecikmesi", CheckInternet);
        }

        private static RegistryKey Hklm()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        // ================================================================ Disk

        private CheckItemDto CheckDisks()
        {
            int alarm = GeneralSettings.GetInt(_ctx.Settings, SettingKeys.DiskAlarmPercent);
            var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToList();
            var detail = new StringBuilder();
            var warn = new List<string>();
            var crit = new List<string>();
            foreach (var d in drives)
            {
                double used = 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize;
                string line = d.Name.TrimEnd('\\') + " " + Fmt.Pct(used) + " dolu (boş " + Fmt.Bytes(d.TotalFreeSpace) + " / " + Fmt.Bytes(d.TotalSize) + ")";
                detail.AppendLine("  " + line);
                if (used >= 98 || d.TotalFreeSpace < 2L * 1024 * 1024 * 1024) crit.Add(line);
                else if (used >= alarm) warn.Add(line);
            }

            if (crit.Count == 0 && warn.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Tüm diskler %" + alarm + " sınırının altında.", Detail = detail.ToString() };

            return new CheckItemDto
            {
                Status = crit.Count > 0 ? CheckStatus.Kritik : CheckStatus.Uyari,
                Summary = string.Join(", ", crit.Concat(warn)),
                Detail = "Disk dolarsa SQL Server veri / log dosyalarını büyütemez ve DB yazmayı durdurur; Windows güncellemeleri ve kullanıcı oturumları da bozulur.\r\n"
                         + "Uyarı sınırı Genel Ayarlar → Disk doluluk uyarısı (%" + alarm + ").\r\n\r\n" + detail,
                HowToFix = "Bu sayfadaki Geçici dosyalar, Windows Update önbelleği, WinSxS, Eski kullanıcı profilleri ve En büyük tablolar maddeleri yer açma yollarını gösterir. "
                           + "SQL log dosyası şişmişse \"Log dosyaları\" maddesine bakın. Kalıcı çözüm için disk büyütülmeli."
            };
        }

        private CheckItemDto CheckDiskLatency()
        {
            var category = new PerformanceCounterCategory("PhysicalDisk");
            var instances = category.GetInstanceNames().Where(n => n != "_Total").OrderBy(n => n).ToList();
            var counters = instances.Select(n => new
            {
                Name = n,
                Read = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Read", n, true),
                Write = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Write", n, true),
                Queue = new PerformanceCounter("PhysicalDisk", "Avg. Disk Queue Length", n, true)
            }).ToList();
            try
            {
                foreach (var c in counters) { c.Read.NextValue(); c.Write.NextValue(); c.Queue.NextValue(); }
                var sums = counters.ToDictionary(c => c.Name, c => new double[3]);
                const int samples = 5;
                for (int i = 0; i < samples; i++)
                {
                    if (_ctx.Token.WaitHandle.WaitOne(1000)) _ctx.Token.ThrowIfCancellationRequested();
                    foreach (var c in counters)
                    {
                        sums[c.Name][0] += c.Read.NextValue() * 1000;
                        sums[c.Name][1] += c.Write.NextValue() * 1000;
                        sums[c.Name][2] += c.Queue.NextValue();
                    }
                }

                var detail = new StringBuilder("Okuma | Yazma | Kuyruk | Disk  (" + samples + " sn ölçüm ortalaması)\r\n");
                double worst = 0;
                string worstName = null;
                foreach (var c in counters)
                {
                    double r = sums[c.Name][0] / samples, w = sums[c.Name][1] / samples, q = sums[c.Name][2] / samples;
                    detail.AppendLine(r.ToString("0.0", Fmt.Tr) + " ms | " + w.ToString("0.0", Fmt.Tr) + " ms | " + q.ToString("0.0", Fmt.Tr) + " | " + c.Name);
                    double m = Math.Max(r, w);
                    if (m > worst) { worst = m; worstName = c.Name; }
                }

                var status = worst >= 100 ? CheckStatus.Kritik : worst >= 25 ? CheckStatus.Uyari : CheckStatus.Gecti;
                return new CheckItemDto
                {
                    Status = status,
                    Summary = status == CheckStatus.Gecti
                        ? "Diskler hızlı cevap veriyor (en yüksek " + worst.ToString("0.0", Fmt.Tr) + " ms)."
                        : worstName + " yavaş: " + worst.ToString("0", Fmt.Tr) + " ms (iyi: 10 ms altı, kabul edilebilir: 25 ms altı).",
                    Detail = "Anlık ölçümdür: o sırada yedek / bakım / antivirüs taraması çalışıyorsa yüksek çıkabilir. Sürekli yüksekse disk yetersiz demektir.\r\n\r\n" + detail,
                    HowToFix = status == CheckStatus.Gecti ? null
                        : "Disk tek başına yavaşsa: SSD'ye geçiş, RAID / sanal disk ayarları (sanal sunucuda host tarafı) kontrol edilmeli. "
                          + "SQL veri ve log dosyalarının ayrı disklerde olması yükü dağıtır. Antivirüs SQL dosyalarını tarıyorsa hariç tutun (ilgili madde)."
                };
            }
            finally
            {
                foreach (var c in counters) { c.Read.Dispose(); c.Write.Dispose(); c.Queue.Dispose(); }
            }
        }

        // Disk iyileştirme: SecurityGuard defrag ÇALIŞTIRMAZ (SSD'yi yıpratır, sanal diskte faydasız).
        // Sadece Windows'un haftalık "Sürücüleri İyileştir" görevinin (SSD'de TRIM, HDD'de defrag) açık olup olmadığını OKUR.
        private CheckItemDto CheckDiskOptimize()
        {
            string media = DiskMediaSummary();
            bool enabled, lastOk;
            DateTime? lastRun;
            long lastResult;
            bool known = TryReadOptimizeTask(out enabled, out lastRun, out lastResult);
            lastOk = lastResult == 0;

            var detail = new StringBuilder();
            detail.AppendLine("Depolama türü: " + (media ?? "okunamadı (sanal disk olabilir)"));
            if (known)
            {
                detail.AppendLine("Windows 'Sürücüleri İyileştir' görevi (\\Microsoft\\Windows\\Defrag\\ScheduledDefrag): " + (enabled ? "açık" : "KAPALI"));
                detail.AppendLine("Son çalışma: " + (lastRun == null ? "hiç / bilinmiyor" : Fmt.Date(lastRun))
                                  + (lastRun == null ? "" : lastOk ? " (başarılı)" : " (sonuç 0x" + lastResult.ToString("X") + ")"));
            }
            else
            {
                detail.AppendLine("Windows iyileştirme görevinin durumu okunamadı.");
            }
            detail.AppendLine().Append("Not: SecurityGuard disk birleştirme (defrag) çalıştırmaz. Windows bunu haftalık görevle zaten doğru yapar: "
                                       + "SSD'de TRIM, sabit diskte (HDD) defrag. SSD'de klasik defrag diski boşuna yıpratır; sanal diskte faydasızdır.");

            if (known && !enabled)
                return new CheckItemDto
                {
                    Status = CheckStatus.Uyari,
                    Summary = "Windows otomatik disk iyileştirme (SSD TRIM / HDD defrag) görevi KAPALI.",
                    Detail = detail.ToString(),
                    HowToFix = "Görevi tekrar açın; Windows SSD / HDD ayrımını kendisi yapar: dfrgui.exe → Ayarları değiştir → \"Zamanlanmış iyileştirmeyi aç\". Veya script.",
                    Script = "Enable-ScheduledTask -TaskName 'ScheduledDefrag' -TaskPath '\\Microsoft\\Windows\\Defrag\\'",
                    ScriptLanguage = "powershell"
                };

            return new CheckItemDto
            {
                Status = known ? CheckStatus.Gecti : CheckStatus.Bilgi,
                Summary = known ? "Windows otomatik disk iyileştirme (SSD TRIM / HDD defrag) açık." : "Disk iyileştirme durumu okunamadı" + (media == null ? "." : " (" + media + ")."),
                Detail = detail.ToString()
            };
        }

        /// <summary>Fiziksel disklerin ortam türü özeti (ör. "1 SSD, 1 HDD"); okunamazsa null.</summary>
        private static string DiskMediaSummary()
        {
            try
            {
                var counts = new Dictionary<string, int>();
                var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
                scope.Connect();
                using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT MediaType FROM MSFT_PhysicalDisk")))
                using (var results = searcher.Get())
                    foreach (ManagementObject mo in results)
                        using (mo)
                        {
                            int mt = 0;
                            var v = mo["MediaType"];
                            if (v != null) mt = Convert.ToInt32(v);
                            string name = mt == 3 ? "HDD (sabit disk)" : mt == 4 ? "SSD" : mt == 5 ? "SCM" : "sanal / bilinmiyor";
                            int c;
                            counts[name] = counts.TryGetValue(name, out c) ? c + 1 : 1;
                        }
                return counts.Count == 0 ? null : string.Join(", ", counts.Select(kv => kv.Value + " " + kv.Key));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Windows'un ScheduledDefrag görevini Task Scheduler COM'uyla okur (tek nesneler, her biri bırakılır).</summary>
        private static bool TryReadOptimizeTask(out bool enabled, out DateTime? lastRun, out long lastResult)
        {
            enabled = false;
            lastRun = null;
            lastResult = 0;
            object svc = null, folder = null, task = null;
            try
            {
                Type t = Type.GetTypeFromProgID("Schedule.Service");
                if (t == null) return false;
                svc = Activator.CreateInstance(t);
                ComCall(svc, "Connect");
                folder = ComCall(svc, "GetFolder", @"\Microsoft\Windows\Defrag");
                task = ComCall(folder, "GetTask", "ScheduledDefrag");
                if (task == null) return false;

                var en = ComGet(task, "Enabled");
                enabled = en != null && Convert.ToBoolean(en);
                var lr = ComGet(task, "LastRunTime");
                if (lr is DateTime)
                {
                    var d = (DateTime)lr;
                    if (d.Year > 1900) lastRun = d; // hiç çalışmadıysa 1899-12-30 döner
                }
                var res = ComGet(task, "LastTaskResult");
                if (res != null) lastResult = Convert.ToInt64(res);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                foreach (var o in new[] { task, folder, svc })
                    if (o != null) try { Marshal.FinalReleaseComObject(o); } catch { }
            }
        }

        private static object ComCall(object obj, string method, params object[] args)
        {
            return obj.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, obj, args);
        }

        private static object ComGet(object obj, string prop)
        {
            return obj.GetType().InvokeMember(prop, BindingFlags.GetProperty, null, obj, null);
        }

        // ================================================================ Bellek / işlemci

        private List<ProcessUsage> _processes;

        /// <summary>İşlem başına CPU (3 sn ölçüm) ve bellek; iki madde de kullanır, bir kez ölçülür.</summary>
        private List<ProcessUsage> Processes(out double totalCpu)
        {
            var before = new Dictionary<int, TimeSpan>();
            var cpuBefore = WindowsNative.CpuTimes();
            foreach (var p in Process.GetProcesses())
            {
                try { before[p.Id] = p.TotalProcessorTime; } catch { }
                p.Dispose();
            }
            var sw = Stopwatch.StartNew();
            if (_ctx.Token.WaitHandle.WaitOne(3000)) _ctx.Token.ThrowIfCancellationRequested();
            var cpuAfter = WindowsNative.CpuTimes();
            double elapsedMs = sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount;

            long totalDelta = cpuAfter.Total - cpuBefore.Total;
            totalCpu = totalDelta <= 0 ? 0 : 100.0 * (totalDelta - (cpuAfter.Idle - cpuBefore.Idle)) / totalDelta;

            var list = new List<ProcessUsage>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var u = new ProcessUsage { Name = p.ProcessName, Id = p.Id, SessionId = p.SessionId, WorkingSet = p.WorkingSet64 };
                    TimeSpan t0;
                    if (before.TryGetValue(p.Id, out t0)) u.Cpu = 100.0 * (p.TotalProcessorTime - t0).TotalMilliseconds / elapsedMs;
                    list.Add(u);
                }
                catch
                {
                    // işlem kapanmış / erişilemiyor
                }
                finally
                {
                    p.Dispose();
                }
            }
            return list;
        }

        private double _totalCpu;

        private void EnsureProcesses()
        {
            if (_processes == null) _processes = Processes(out _totalCpu);
        }

        private static string ProcessTable(IEnumerable<ProcessUsage> list, bool byCpu)
        {
            // Aynı adlı işlemler (her RDP kullanıcısının ayrı kopyası) toplanır
            var grouped = list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Name = g.Key, Count = g.Count(), Cpu = g.Sum(p => p.Cpu), Ram = g.Sum(p => p.WorkingSet), Sessions = g.Select(p => p.SessionId).Distinct().Count() })
                .OrderByDescending(g => byCpu ? g.Cpu : g.Ram).Take(10);
            var sb = new StringBuilder(byCpu ? "CPU | Bellek | Adet | İşlem\r\n" : "Bellek | CPU | Adet | İşlem\r\n");
            foreach (var g in grouped)
            {
                string cpu = Fmt.Pct(g.Cpu), ram = Fmt.Bytes(g.Ram);
                sb.AppendLine((byCpu ? cpu + " | " + ram : ram + " | " + cpu) + " | " + g.Count + (g.Sessions > 1 ? " (" + g.Sessions + " oturumda)" : "") + " | " + g.Name);
            }
            return sb.ToString();
        }

        private CheckItemDto CheckMemory()
        {
            EnsureProcesses();
            var mem = WindowsNative.Memory();
            double commitUsed = mem.CommitLimit == 0 ? 0 : 100.0 * (mem.CommitLimit - mem.CommitAvailable) / mem.CommitLimit;
            var status = mem.LoadPercent >= 97 || commitUsed >= 95 ? CheckStatus.Kritik : mem.LoadPercent >= 90 || commitUsed >= 90 ? CheckStatus.Uyari : CheckStatus.Gecti;
            bool sqlTop = _processes.OrderByDescending(p => p.WorkingSet).Take(1).Any(p => p.Name.Equals("sqlservr", StringComparison.OrdinalIgnoreCase));

            return new CheckItemDto
            {
                Status = status,
                Summary = Fmt.Pct(mem.LoadPercent) + " dolu (boş " + Fmt.Bytes((long)mem.Available) + " / " + Fmt.Bytes((long)mem.Total) + ")"
                          + (commitUsed >= 90 ? ", sanal bellek " + Fmt.Pct(commitUsed) + " dolu" : "") + ".",
                Detail = "En çok bellek kullanan işlemler (aynı adlılar toplandı):\r\n" + ProcessTable(_processes, false)
                         + (commitUsed >= 90
                             ? "\r\nSanal bellek (fiziksel bellek + sayfa dosyası) " + Fmt.Pct(commitUsed) + " dolu: dolarsa programlar \"yetersiz bellek\" hatasıyla kapanır. "
                               + "Sayfa dosyası sabit ve küçükse \"Sayfa dosyası\" maddesine bakın.\r\n"
                             : "")
                         + (sqlTop ? "\r\nSQL Server'ın (sqlservr) çok bellek kullanması normaldir: veriyi bellekte tutar. Sınırı \"Bellek sınırı (max server memory)\" maddesinden ayarlanır." : ""),
                HowToFix = status == CheckStatus.Gecti ? null
                    : "SQL Server sınırsızsa önce max server memory ayarlayın. Kopmuş RDP oturumları bellekte program tutuyorsa oturum süresi sınırı koyun. "
                      + "Sürekli doluysa sunucuya bellek eklenmeli."
            };
        }

        private CheckItemDto CheckCpu()
        {
            EnsureProcesses();
            var status = _totalCpu >= 95 ? CheckStatus.Uyari : CheckStatus.Gecti;
            return new CheckItemDto
            {
                Status = status,
                Summary = Fmt.Pct(_totalCpu) + " (3 sn anlık ölçüm), " + Environment.ProcessorCount + " mantıksal işlemci.",
                Detail = "En çok işlemci kullanan işlemler (aynı adlılar toplandı; %100 = bir çekirdek değil, tüm işlemciler):\r\n" + ProcessTable(_processes, true)
                         + "\r\nAnlık ölçümdür; tarama sırasında yoğun bir iş çalışıyor olabilir.",
                HowToFix = status == CheckStatus.Gecti ? null
                    : "Sürekli yüksekse: SQL Server ise \"En çok süre harcayan sorgular\" ve \"Eksik index\" maddelerine bakın; bir kullanıcının programıysa o oturumu kontrol edin."
            };
        }

        // ================================================================ RDP oturumları

        private CheckItemDto CheckDisconnectedSessions()
        {
            var sessions = WindowsNative.Sessions();
            var disconnected = sessions.Where(s => s.Disconnected).OrderBy(s => s.DisconnectTime ?? DateTime.MaxValue).ToList();
            int limitMinutes = DisconnectLimitMinutes();
            EnsureProcesses();

            var detail = new StringBuilder("Açık oturum: " + sessions.Count + ", kopmuş: " + disconnected.Count + "\r\n");
            detail.AppendLine("Kopmuş oturum süre sınırı: " + (limitMinutes > 0 ? FormatMinutes(limitMinutes) : "yok (oturumlar sonsuza kadar açık kalır)")).AppendLine();
            foreach (var s in disconnected)
            {
                long ram = _processes.Where(p => p.SessionId == s.Id).Sum(p => p.WorkingSet);
                detail.AppendLine("  " + s.User + ": " + (s.DisconnectTime == null ? "kopma zamanı bilinmiyor" : Fmt.Ago(s.DisconnectTime) + " koptu")
                                  + ", bellekte " + Fmt.Bytes(ram));
            }

            var old = disconnected.Where(s => s.DisconnectTime != null && s.DisconnectTime < DateTime.Now.AddDays(-1)).ToList();
            CheckStatus status;
            string summary;
            if (limitMinutes > 0)
            {
                status = CheckStatus.Gecti;
                summary = "Kopmuş oturumlar " + FormatMinutes(limitMinutes) + " sonra kapanıyor. Şu an kopmuş: " + disconnected.Count + ".";
            }
            else if (old.Count > 0)
            {
                status = CheckStatus.Uyari;
                summary = old.Count + " oturum 1 günden uzun süredir kopuk ama açık; süre sınırı yok.";
            }
            else
            {
                status = CheckStatus.Bilgi;
                summary = "Kopmuş oturum süre sınırı yok" + (disconnected.Count > 0 ? " (şu an " + disconnected.Count + " kopmuş oturum)" : "") + ".";
            }

            return new CheckItemDto
            {
                Status = status,
                Summary = summary,
                Detail = "Kullanıcı RDP penceresini kapatıp çıkış yapmazsa oturumu ve açık programları (ERP, Excel...) sunucuda çalışmaya devam eder: "
                         + "bellek tüketir, kayıtları kilitli tutabilir.\r\n\r\n" + detail,
                HowToFix = status == CheckStatus.Gecti ? null
                    : "gpedit.msc → Bilgisayar Yapılandırması → Yönetim Şablonları → Windows Bileşenleri → Uzak Masaüstü Hizmetleri → "
                      + "Uzak Masaüstü Oturumu Ana Bilgisayarı → Oturum Zaman Sınırları → \"Bağlantısı kesilen oturumlar için zaman sınırı belirle\" (ör. 8 saat).\r\n"
                      + "Dikkat: süre dolunca kaydedilmemiş işler kaybolur, kullanıcıları önceden bilgilendirin. Aşağıdaki script aynı ilkeyi registry ile koyar.",
                Script = status == CheckStatus.Gecti ? null
                    : "# 8 saat (milisaniye). Kopmuş oturumlar bu süre sonunda kapanır; kaydedilmemiş işler kaybolur.\r\n"
                      + "reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services\" /v MaxDisconnectionTime /t REG_DWORD /d 28800000 /f\r\ngpupdate /force",
                ScriptLanguage = "powershell"
            };
        }

        private static int DisconnectLimitMinutes()
        {
            using (var hklm = Hklm())
            {
                using (var policy = hklm.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services"))
                {
                    var v = policy == null ? null : policy.GetValue("MaxDisconnectionTime");
                    if (v is int) return (int)v / 60000;
                }
                using (var rdp = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp"))
                {
                    var v = rdp == null ? null : rdp.GetValue("MaxDisconnectionTime");
                    var inherit = rdp == null ? null : rdp.GetValue("fInheritMaxDisconnectionTime");
                    if (v is int && (int)v > 0 && !(inherit is int && (int)inherit == 1)) return (int)v / 60000;
                }
            }
            return 0;
        }

        private static string FormatMinutes(int minutes)
        {
            return minutes >= 60 && minutes % 60 == 0 ? minutes / 60 + " saat" : minutes + " dakika";
        }

        // ================================================================ Olay logu

        private sealed class EventKind
        {
            public string Log, Text;

            /// <summary>Kaynak adı tam eşleşmeli: aynı olay numarasını başka kaynaklar da kullanıyor (ör. Hyper-V 129, Kernel-Boot 153).</summary>
            public Regex Provider;
            public int[] Ids;
            public CheckStatus Status;
        }

        private static Regex P(string pattern)
        {
            return new Regex("^(" + pattern + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static readonly EventKind[] CriticalEvents =
        {
            new EventKind { Log = "System", Provider = P("Microsoft-Windows-Kernel-Power"), Ids = new[] { 41 }, Status = CheckStatus.Kritik, Text = "Beklenmedik kapanma / elektrik kesintisi" },
            new EventKind { Log = "System", Provider = P("EventLog"), Ids = new[] { 6008 }, Status = CheckStatus.Kritik, Text = "Önceki kapanma beklenmedikti" },
            new EventKind { Log = "System", Provider = P("Microsoft-Windows-WER-SystemErrorReporting"), Ids = new[] { 1001 }, Status = CheckStatus.Kritik, Text = "Mavi ekran (sistem çöktü)" },
            new EventKind { Log = "System", Provider = P("Microsoft-Windows-WHEA-Logger"), Ids = new[] { 18, 46 }, Status = CheckStatus.Kritik, Text = "Ölümcül donanım hatası (işlemci / bellek)" },
            new EventKind { Log = "System", Provider = P("Microsoft-Windows-WHEA-Logger"), Ids = new[] { 17, 19, 47 }, Status = CheckStatus.Uyari, Text = "Düzeltilmiş donanım hatası (işlemci / bellek / PCIe; tekrarlıyorsa arıza habercisi)" },
            new EventKind { Log = "System", Provider = P("disk"), Ids = new[] { 7, 11, 51, 153 }, Status = CheckStatus.Uyari, Text = "Disk okuma / yazma hatası" },
            new EventKind { Log = "System", Provider = P(@"stor\w*|iaStor\w*|LSI_\w+|megasas\w*|percsas\w*|vmscsi|pvscsi|nvme\w*|vhdmp|HpSAMD|arcsas|SmartPqi"), Ids = new[] { 129 }, Status = CheckStatus.Uyari, Text = "Disk denetleyicisi sıfırlandı (disk cevap vermedi)" },
            new EventKind { Log = "System", Provider = P("Ntfs|Microsoft-Windows-Ntfs"), Ids = new[] { 55, 137, 140 }, Status = CheckStatus.Kritik, Text = "Dosya sistemi bozulması (NTFS)" },
            new EventKind { Log = "System", Provider = P("Microsoft-Windows-Resource-Exhaustion-Detector"), Ids = new[] { 2004 }, Status = CheckStatus.Uyari, Text = "Sanal bellek tükendi" },
            new EventKind { Log = "Application", Provider = P(@"MSSQLSERVER|MSSQL\$.+"), Ids = new[] { 823, 824 }, Status = CheckStatus.Kritik, Text = "SQL Server disk G/Ç hatası / sayfa bozulması (823 / 824)" },
            new EventKind { Log = "Application", Provider = P(@"MSSQLSERVER|MSSQL\$.+"), Ids = new[] { 825 }, Status = CheckStatus.Kritik, Text = "SQL Server okumayı tekrar denemek zorunda kaldı (825: disk arızalanıyor olabilir)" },
            new EventKind { Log = "Application", Provider = P(@"MSSQLSERVER|MSSQL\$.+"), Ids = new[] { 833 }, Status = CheckStatus.Uyari, Text = "SQL Server disk isteği 15 saniyeden uzun sürdü (833)" },
        };

        private CheckItemDto CheckCriticalEvents()
        {
            var found = new List<Tuple<EventKind, DateTime, string>>();
            foreach (var log in CriticalEvents.GroupBy(k => k.Log))
            {
                var ids = log.SelectMany(k => k.Ids).Distinct();
                string xpath = "*[System[(" + string.Join(" or ", ids.Select(i => "EventID=" + i)) + ") and TimeCreated[timediff(@SystemTime) <= 604800000]]]";
                using (var reader = new EventLogReader(new System.Diagnostics.Eventing.Reader.EventLogQuery(log.Key, PathType.LogName, xpath)))
                {
                    for (var e = reader.ReadEvent(); e != null; e = reader.ReadEvent())
                    {
                        using (e)
                        {
                            _ctx.Token.ThrowIfCancellationRequested();
                            string provider = e.ProviderName ?? "";
                            var kind = log.FirstOrDefault(k => k.Ids.Contains(e.Id) && k.Provider.IsMatch(provider));
                            if (kind != null) found.Add(Tuple.Create(kind, e.TimeCreated ?? DateTime.Now, provider));
                        }
                        if (found.Count > 2000) break;
                    }
                }
            }

            if (found.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Son 7 günde beklenmedik kapanma, mavi ekran, disk veya donanım hatası yok." };

            var detail = new StringBuilder("Adet | Son görülme | Olay\r\n");
            foreach (var g in found.GroupBy(f => f.Item1).OrderByDescending(g => g.Key.Status).ThenByDescending(g => g.Count()))
                detail.AppendLine(g.Count() + " | " + Fmt.Date(g.Max(x => x.Item2)) + " | " + g.Key.Text + " (" + g.Key.Log + " " + string.Join("/", g.Key.Ids) + ")");

            var status = found.Any(f => f.Item1.Status == CheckStatus.Kritik) ? CheckStatus.Kritik : CheckStatus.Uyari;
            return new CheckItemDto
            {
                Status = status,
                Summary = string.Join(", ", found.GroupBy(f => f.Item1).OrderByDescending(g => g.Key.Status).Select(g => g.Key.Text + " (" + g.Count() + ")").Take(3)) + ".",
                Detail = detail + "\r\nAyrıntı: Olay Görüntüleyicisi (eventvwr.msc) → Windows Günlükleri → Sistem / Uygulama, olay numarasıyla filtreleyin.",
                HowToFix = "Beklenmedik kapanma: UPS / elektrik ve sunucunun elle kapatılıp kapatılmadığını kontrol edin.\r\n"
                           + "Disk / NTFS / SQL 823-825 hataları: disk arızalanıyor olabilir. Hemen yedeğin sağlam olduğunu doğrulayın, "
                           + "SQL'de DBCC CHECKDB çalıştırın, diski değiştirmeyi planlayın.\r\n"
                           + "Donanım (WHEA) / mavi ekran: donanım servisine (sunucu üreticisi) danışın."
            };
        }

        // ================================================================ Ayarlar

        private CheckItemDto CheckPowerPlan()
        {
            string active = null;
            bool policy = false;
            using (var hklm = Hklm())
            {
                using (var p = hklm.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Power\PowerSettings"))
                {
                    active = p == null ? null : p.GetValue("ActivePowerScheme") as string;
                    policy = active != null;
                }
                if (active == null)
                    using (var p = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes"))
                        active = p == null ? null : p.GetValue("ActivePowerScheme") as string;
            }
            if (active == null) return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "Etkin güç planı okunamadı." };

            string id = active.Trim().ToLowerInvariant();
            string name;
            CheckStatus status;
            switch (id)
            {
                case "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c": name = "Yüksek performans"; status = CheckStatus.Gecti; break;
                case "e9a42b02-d5df-448d-aa00-03f14749eb61": name = "Nihai performans"; status = CheckStatus.Gecti; break;
                case "381b4222-f694-41f0-9685-ff5bb260df2e": name = "Dengeli"; status = CheckStatus.Uyari; break;
                case "a1841308-3541-4fab-bc81-f71556f20b4a": name = "Güç tasarrufu"; status = CheckStatus.Uyari; break;
                default: name = "Özel plan (" + id + ")"; status = CheckStatus.Bilgi; break;
            }
            if (status == CheckStatus.Gecti)
                return new CheckItemDto { Status = status, Summary = name + "." };

            return new CheckItemDto
            {
                Status = status,
                Summary = name + (policy ? " (grup ilkesiyle ayarlı)" : "") + ". Önerilen: Yüksek performans.",
                Detail = "\"Dengeli\" planda işlemci boşta yavaşlatılır ve yük gelince hızlanması gecikir; SQL Server sorguları ve RDP oturumları %10-30 yavaş çalışabilir.\r\n"
                         + "Sanal sunucuda host'un (VMware / Hyper-V) güç ayarı da yüksek performans olmalı; fiziksel sunucuda BIOS güç ayarı da kontrol edilmeli.",
                HowToFix = policy
                    ? "Plan grup ilkesiyle ayarlanmış: etki alanı yöneticisi ilkesini değiştirmeli (Bilgisayar Yapılandırması → Yönetim Şablonları → Sistem → Güç Yönetimi)."
                    : "Denetim Masası → Güç Seçenekleri → Yüksek performans. Veya script (hemen geçerli olur).",
                Script = policy ? null : "powercfg /setactive SCHEME_MIN   # SCHEME_MIN = Yüksek performans",
                ScriptLanguage = "powershell"
            };
        }

        private CheckItemDto CheckPageFile()
        {
            string[] files;
            using (var hklm = Hklm())
            using (var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"))
                files = key == null ? null : key.GetValue("PagingFiles") as string[];

            var entries = (files ?? new string[0]).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            var mem = WindowsNative.Memory();
            if (entries.Count == 0)
                return new CheckItemDto
                {
                    Status = CheckStatus.Uyari,
                    Summary = "Sayfa dosyası kapalı.",
                    Detail = "Sayfa dosyası yokken bellek dolarsa programlar \"yetersiz bellek\" hatasıyla kapanır ve mavi ekranda döküm (crash dump) alınamaz.",
                    HowToFix = "Sistem Özellikleri → Gelişmiş → Performans Ayarları → Gelişmiş → Sanal bellek → Değiştir → "
                               + "\"Tüm sürücüler için disk belleği dosyası boyutunu otomatik yönet\". Yeniden başlatma gerekir."
                };

            bool auto = entries.Any(e => e.StartsWith("?:"));
            double commitUsed = mem.CommitLimit == 0 ? 0 : 100.0 * (mem.CommitLimit - mem.CommitAvailable) / mem.CommitLimit;
            if (!auto && commitUsed >= 90)
                return new CheckItemDto
                {
                    Status = CheckStatus.Uyari,
                    Summary = "Sabit boyutlu (" + string.Join(", ", entries) + ") ve sanal bellek " + Fmt.Pct(commitUsed) + " dolu: sayfa dosyası küçük kalıyor.",
                    Detail = "Sanal bellek (commit) sınırı = fiziksel bellek + sayfa dosyası. Sınır dolunca programlar \"yetersiz bellek\" hatasıyla kapanır.\r\n"
                             + "Sınır: " + Fmt.Bytes((long)mem.CommitLimit) + ", boş: " + Fmt.Bytes((long)mem.CommitAvailable) + ", fiziksel bellek: " + Fmt.Bytes((long)mem.Total),
                    HowToFix = "Sistem Özellikleri → Gelişmiş → Performans Ayarları → Gelişmiş → Sanal bellek → Değiştir → "
                               + "\"Tüm sürücüler için disk belleği dosyası boyutunu otomatik yönet\" (veya daha büyük sabit boyut). Yeniden başlatma gerekebilir. "
                               + "Asıl çözüm: belleği çok kullanan programları azaltmak ya da bellek eklemek."
                };
            return new CheckItemDto
            {
                Status = CheckStatus.Gecti,
                Summary = auto ? "Windows tarafından otomatik yönetiliyor." : "Ayarlı: " + string.Join(", ", entries) + ".",
                Detail = "Ayar: " + string.Join(", ", entries) + "\r\nSanal bellek (commit) sınırı: " + Fmt.Bytes((long)mem.CommitLimit) + ", fiziksel bellek: " + Fmt.Bytes((long)mem.Total)
            };
        }

        private CheckItemDto CheckAntivirusExclusions()
        {
            bool defender;
            try
            {
                using (var sc = new ServiceController("WinDefend"))
                    defender = sc.Status == ServiceControllerStatus.Running;
            }
            catch (InvalidOperationException)
            {
                defender = false;
            }

            object pathsObj;
            var sqlPaths = _ctx.Shared.TryGetValue("sql.paths", out pathsObj) ? ((HashSet<string>)pathsObj).OrderBy(p => p).ToList() : new List<string>();
            string script = "# SQL Server dosyalarını Defender taramasından hariç tut (SQL performansı ve dosya kilitleri için Microsoft önerisi)\r\n"
                            + "Add-MpPreference -ExclusionExtension 'mdf','ldf','ndf','bak','trn'\r\n"
                            + (sqlPaths.Count > 0 ? "Add-MpPreference -ExclusionPath " + string.Join(",", sqlPaths.Select(p => "'" + p.Replace("'", "''") + "'")) + "\r\n" : "")
                            + "# İsteğe bağlı (güvenlik / performans dengesi): Add-MpPreference -ExclusionProcess 'sqlservr.exe'";

            if (!defender)
                return new CheckItemDto
                {
                    Status = CheckStatus.Bilgi,
                    Summary = "Windows Defender çalışmıyor; başka bir antivirüs kullanılıyorsa hariç tutmaları orada kontrol edin.",
                    Detail = "Antivirüs SQL Server veri / log / yedek dosyalarını tararsa SQL Server yavaşlar, nadiren de dosya bozulmasına yol açar.",
                    HowToFix = "Kullanılan antivirüste şunları hariç tutun: .mdf, .ldf, .ndf, .bak, .trn uzantıları"
                               + (sqlPaths.Count > 0 ? " ve şu klasörler: " + string.Join(", ", sqlPaths) : "") + "."
                };

            var excludedPaths = new List<string>();
            var excludedExt = new List<string>();
            var excludedProc = new List<string>();
            using (var hklm = Hklm())
            {
                excludedPaths.AddRange(ValueNames(hklm, @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths"));
                excludedExt.AddRange(ValueNames(hklm, @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Extensions"));
                excludedProc.AddRange(ValueNames(hklm, @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Processes"));
                excludedPaths.AddRange(ValueNames(hklm, @"SOFTWARE\Policies\Microsoft\Windows Defender\Exclusions\Paths"));
                excludedExt.AddRange(ValueNames(hklm, @"SOFTWARE\Policies\Microsoft\Windows Defender\Exclusions\Extensions"));
                excludedProc.AddRange(ValueNames(hklm, @"SOFTWARE\Policies\Microsoft\Windows Defender\Exclusions\Processes"));
            }

            bool procExcluded = excludedProc.Any(p => p.EndsWith("sqlservr.exe", StringComparison.OrdinalIgnoreCase));
            var missingExt = new[] { "mdf", "ldf", "ndf" }.Where(x => !excludedExt.Any(e => e.TrimStart('.', '*').Equals(x, StringComparison.OrdinalIgnoreCase))).ToList();
            var missingPaths = sqlPaths.Where(p => !excludedPaths.Any(e => p.StartsWith(e.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))).ToList();
            bool covered = procExcluded || missingExt.Count == 0 || (sqlPaths.Count > 0 && missingPaths.Count == 0);

            if (covered)
                return new CheckItemDto
                {
                    Status = CheckStatus.Gecti,
                    Summary = "SQL Server dosyaları Defender taramasından hariç tutulmuş.",
                    Detail = "Hariç tutulan uzantılar: " + (excludedExt.Count == 0 ? "-" : string.Join(", ", excludedExt))
                             + "\r\nHariç tutulan klasörler: " + (excludedPaths.Count == 0 ? "-" : string.Join(", ", excludedPaths))
                             + "\r\nHariç tutulan işlemler: " + (excludedProc.Count == 0 ? "-" : string.Join(", ", excludedProc))
                };

            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = "Defender SQL Server dosyalarını tarıyor (hariç tutma yok).",
                Detail = "Defender her okuma / yazmada SQL veri ve log dosyalarını tarar: disk gecikmesi artar, yedekler yavaşlar.\r\n\r\n"
                         + "Eksik uzantılar: " + (missingExt.Count == 0 ? "-" : string.Join(", ", missingExt))
                         + "\r\nHariç tutulmamış SQL klasörleri: " + (missingPaths.Count == 0 ? "-" : string.Join(", ", missingPaths)),
                HowToFix = "Windows Güvenliği → Virüs ve tehdit koruması → Ayarları yönet → Dışlamalar. Veya yönetici PowerShell'de script.",
                Script = script,
                ScriptLanguage = "powershell"
            };
        }

        private static IEnumerable<string> ValueNames(RegistryKey root, string path)
        {
            try
            {
                using (var key = root.OpenSubKey(path))
                    return key == null ? new string[0] : key.GetValueNames();
            }
            catch (System.Security.SecurityException)
            {
                return new string[0];
            }
            catch (UnauthorizedAccessException)
            {
                return new string[0];
            }
        }

        // ================================================================ Yer açma

        /// <summary>Klasör boyutu (erişilemeyenler atlanır; çok büyük klasörde süre sınırı var).</summary>
        private long FolderSize(string path, Stopwatch budget, TimeSpan limit, ref bool partial)
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                _ctx.Token.ThrowIfCancellationRequested();
                if (budget.Elapsed > limit) { partial = true; break; }
                string dir = stack.Pop();
                try
                {
                    var info = new DirectoryInfo(dir);
                    foreach (var f in info.EnumerateFiles())
                    {
                        try { total += f.Length; } catch { }
                    }
                    foreach (var d in info.EnumerateDirectories())
                    {
                        if ((d.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(d.FullName);
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
                catch (System.Security.SecurityException) { }
            }
            return total;
        }

        private static string WindowsDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }

        private List<Tuple<string, string, DateTime?>> _profiles;

        /// <summary>(SID, klasör, son kullanım) - yerel ve etki alanı kullanıcı profilleri.</summary>
        private List<Tuple<string, string, DateTime?>> Profiles()
        {
            if (_profiles != null) return _profiles;
            _profiles = new List<Tuple<string, string, DateTime?>>();
            using (var hklm = Hklm())
            using (var list = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
            {
                if (list == null) return _profiles;
                foreach (var sid in list.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-") && !s.EndsWith(".bak")))
                {
                    using (var key = list.OpenSubKey(sid))
                    {
                        string path = key == null ? null : key.GetValue("ProfileImagePath") as string;
                        if (string.IsNullOrEmpty(path)) continue;
                        path = Environment.ExpandEnvironmentVariables(path);
                        DateTime? last = null;
                        foreach (var pair in new[] { Tuple.Create("LocalProfileLoadTimeHigh", "LocalProfileLoadTimeLow"), Tuple.Create("LocalProfileUnloadTimeHigh", "LocalProfileUnloadTimeLow") })
                        {
                            var hi = key.GetValue(pair.Item1);
                            var lo = key.GetValue(pair.Item2);
                            if (hi is int && lo is int)
                            {
                                long ft = ((long)(uint)(int)hi << 32) | (uint)(int)lo;
                                try
                                {
                                    var t = DateTime.FromFileTime(ft);
                                    if (last == null || t > last) last = t;
                                }
                                catch (ArgumentOutOfRangeException) { }
                            }
                        }
                        try
                        {
                            string ntuser = Path.Combine(path, "NTUSER.DAT");
                            if (File.Exists(ntuser))
                            {
                                var t = File.GetLastWriteTime(ntuser);
                                if (last == null || t > last) last = t;
                            }
                        }
                        catch { }
                        _profiles.Add(Tuple.Create(sid, path, last));
                    }
                }
            }
            return _profiles;
        }

        private CheckItemDto CheckTempFolders()
        {
            var budget = Stopwatch.StartNew();
            bool partial = false;
            var sizes = new List<Tuple<string, long>>();
            sizes.Add(Tuple.Create(Path.Combine(WindowsDir, "Temp"), FolderSize(Path.Combine(WindowsDir, "Temp"), budget, TimeSpan.FromSeconds(90), ref partial)));
            foreach (var p in Profiles())
            {
                string temp = Path.Combine(p.Item2, @"AppData\Local\Temp");
                sizes.Add(Tuple.Create(temp, FolderSize(temp, budget, TimeSpan.FromSeconds(90), ref partial)));
            }

            long total = sizes.Sum(s => s.Item2);
            var detail = new StringBuilder();
            foreach (var s in sizes.Where(s => s.Item2 >= 50L * 1024 * 1024).OrderByDescending(s => s.Item2).Take(30))
                detail.AppendLine("  " + Fmt.Bytes(s.Item2) + "  " + s.Item1);
            if (partial) detail.AppendLine().Append("Ölçüm süre sınırına takıldı; gerçek toplam daha büyük olabilir.");

            var status = total >= 10L * 1024 * 1024 * 1024 ? CheckStatus.Uyari : total >= 2L * 1024 * 1024 * 1024 ? CheckStatus.Bilgi : CheckStatus.Gecti;
            return new CheckItemDto
            {
                Status = status,
                Summary = "Geçici dosyalar toplam " + Fmt.Bytes(total) + (partial ? "+" : "") + " (" + sizes.Count + " klasör).",
                Detail = detail.Length == 0 ? null : detail.ToString(),
                HowToFix = status == CheckStatus.Gecti ? null
                    : "Script 7 günden eski geçici dosyaları siler (kullanımdaki dosyalar atlanır). Disk Temizleme (cleanmgr) de kullanılabilir.",
                Script = status == CheckStatus.Gecti ? null
                    : "# 7 günden eski geçici dosyaları siler (yönetici PowerShell). Açık dosyalar silinemez ve atlanır.\r\n"
                      + "$limit = (Get-Date).AddDays(-7)\r\n"
                      + "Get-ChildItem \"$env:windir\\Temp\", 'C:\\Users\\*\\AppData\\Local\\Temp' -Recurse -Force -File -ErrorAction SilentlyContinue |\r\n"
                      + "    Where-Object { $_.LastWriteTime -lt $limit } | Remove-Item -Force -ErrorAction SilentlyContinue",
                ScriptLanguage = "powershell"
            };
        }

        private CheckItemDto CheckUpdateCache()
        {
            var budget = Stopwatch.StartNew();
            bool partial = false;
            string folder = Path.Combine(WindowsDir, @"SoftwareDistribution\Download");
            long size = FolderSize(folder, budget, TimeSpan.FromSeconds(60), ref partial);
            var status = size >= 10L * 1024 * 1024 * 1024 ? CheckStatus.Uyari : size >= 2L * 1024 * 1024 * 1024 ? CheckStatus.Bilgi : CheckStatus.Gecti;
            if (status == CheckStatus.Gecti)
                return new CheckItemDto { Status = status, Summary = Fmt.Bytes(size) + "." };

            return new CheckItemDto
            {
                Status = status,
                Summary = "İndirilmiş güncelleme dosyaları " + Fmt.Bytes(size) + (partial ? "+" : "") + " yer kaplıyor.",
                Detail = folder + "\r\nKurulmuş güncellemelerin indirme dosyaları burada kalır; silinmesi güvenlidir (gerekirse tekrar indirilir).",
                HowToFix = "Güncelleme kurulmuyorken script'i yönetici PowerShell'de çalıştırın.",
                Script = "# Güncelleme kurulumu sürerken çalıştırmayın\r\n"
                         + "Stop-Service wuauserv, bits -Force\r\n"
                         + "Remove-Item \"$env:windir\\SoftwareDistribution\\Download\\*\" -Recurse -Force -ErrorAction SilentlyContinue\r\n"
                         + "Start-Service bits, wuauserv",
                ScriptLanguage = "powershell"
            };
        }

        private CheckItemDto CheckComponentStore()
        {
            var system = new DriveInfo(Path.GetPathRoot(WindowsDir));
            double used = 100.0 * (system.TotalSize - system.TotalFreeSpace) / system.TotalSize;
            if (used < 80)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Sistem diskinde yer sıkıntısı yok (" + Fmt.Pct(used) + " dolu), temizlik gerekmiyor." };

            return new CheckItemDto
            {
                Status = CheckStatus.Bilgi,
                Summary = "Sistem diski " + Fmt.Pct(used) + " dolu: Windows bileşen deposu temizliği yer açabilir.",
                Detail = "WinSxS klasörü eski güncellemelerin yedeklerini tutar; Explorer'da gösterdiği boyut yanıltıcıdır (dosyaların çoğu Windows ile ortaktır). "
                         + "Gerçek kazancı DISM analizi gösterir.",
                HowToFix = "Önce analiz (birkaç dakika), \"Component Store Cleanup Recommended: Yes\" derse temizlik. Temizlik 10-30 dakika sürebilir, mesai dışında çalıştırın.",
                Script = "Dism.exe /Online /Cleanup-Image /AnalyzeComponentStore\r\n# Önerilirse:\r\nDism.exe /Online /Cleanup-Image /StartComponentCleanup",
                ScriptLanguage = "powershell"
            };
        }

        private CheckItemDto CheckOldProfiles()
        {
            var limit = DateTime.Now.AddDays(-90);
            var old = Profiles().Where(p => p.Item3 != null && p.Item3 < limit).OrderBy(p => p.Item3).ToList();
            if (old.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = Profiles().Count + " kullanıcı profili var, 90 günden uzun süredir kullanılmayan yok." };

            var budget = Stopwatch.StartNew();
            bool partial = false;
            var detail = new StringBuilder("Son kullanım | Boyut | Klasör\r\n");
            long total = 0;
            foreach (var p in old.Take(30))
            {
                long size = FolderSize(p.Item2, budget, TimeSpan.FromSeconds(60), ref partial);
                total += size;
                detail.AppendLine(Fmt.Date(p.Item3) + " | " + Fmt.Bytes(size) + " | " + p.Item2);
            }
            if (partial) detail.AppendLine().Append("Boyut ölçümü süre sınırına takıldı; gerçek boyut daha büyük olabilir.");

            return new CheckItemDto
            {
                Status = CheckStatus.Bilgi,
                Summary = old.Count + " kullanıcı profili 90 günden uzun süredir kullanılmıyor (" + Fmt.Bytes(total) + (partial ? "+" : "") + ").",
                Detail = "Ayrılmış çalışanların / eski kullanıcıların profilleri diskte yer kaplar.\r\n\r\n" + detail,
                HowToFix = "Kullanıcıya ait belgeler gerekiyorsa önce yedekleyin. Sonra: Sistem Özellikleri (sysdm.cpl) → Gelişmiş → Kullanıcı Profilleri → Ayarlar → "
                           + "profili seçip Sil. Klasörü Explorer'dan elle silmeyin (registry kaydı kalır, kullanıcı tekrar girerse geçici profil açılır)."
            };
        }

        // ================================================================ Gereksiz yükler

        private CheckItemDto CheckStartupApps()
        {
            var entries = new List<string>();
            using (var hklm = Hklm())
            {
                foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" })
                    using (var key = hklm.OpenSubKey(path))
                    {
                        if (key == null) continue;
                        foreach (var name in key.GetValueNames())
                            entries.Add(name + "  →  " + key.GetValue(name));
                    }
            }
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            if (Directory.Exists(startup))
                entries.AddRange(Directory.GetFiles(startup).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    .Select(f => Path.GetFileName(f) + "  →  " + startup));

            // Yazıcı sürücüleri ve Windows'un kendi bileşenleri (system32, sürücü deposu) listeye alınmaz
            string windir = WindowsDir.TrimEnd('\\') + "\\";
            entries = entries.Where(e => !PrinterWords.IsMatch(e)
                                         && e.IndexOf("%windir%", StringComparison.OrdinalIgnoreCase) < 0
                                         && e.IndexOf("%SystemRoot%", StringComparison.OrdinalIgnoreCase) < 0
                                         && e.IndexOf(windir, StringComparison.OrdinalIgnoreCase) < 0).ToList();
            if (entries.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Tüm kullanıcılar için açılan başlangıç uygulaması yok." };

            return new CheckItemDto
            {
                Status = entries.Count >= 5 ? CheckStatus.Bilgi : CheckStatus.Gecti,
                Summary = entries.Count + " uygulama her kullanıcı oturumunda otomatik açılıyor.",
                Detail = "RDP sunucusunda bu uygulamalar HER kullanıcı için ayrı ayrı çalışır: 20 kullanıcıda 20 kopya bellek ve işlemci tüketir. "
                         + "Windows bileşenleri ve yazıcı / tarayıcı sürücüleri listeye alınmadı.\r\n\r\n  " + string.Join("\r\n  ", entries),
                HowToFix = "Her kullanıcıda gerçekten gerekmeyenleri kaldırın: Görev Yöneticisi → Başlangıç sekmesi (veya Autoruns aracı). "
                           + "Ne olduğunu bilmediğiniz bir uygulamayı kapatmadan önce sorun."
            };
        }

        private static readonly Dictionary<string, string> UnneededServices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "XblAuthManager", "Xbox Live kimlik doğrulama" },
            { "XblGameSave", "Xbox oyun kayıtları" },
            { "XboxNetApiSvc", "Xbox Live ağ hizmeti" },
            { "XboxGipSvc", "Xbox aksesuar yönetimi" },
            { "MapsBroker", "İndirilmiş haritalar yöneticisi" },
            { "lfsvc", "Konum (geolocation) hizmeti" },
            { "WMPNetworkSvc", "Windows Media Player paylaşımı" },
            { "icssvc", "Mobil etkin nokta (hotspot)" },
            { "RetailDemo", "Mağaza tanıtım modu" },
            { "wisvc", "Windows Insider hizmeti" },
            { "SharedAccess", "İnternet bağlantısı paylaşımı (ICS)" },
            { "Fax", "Faks" },
            { "WalletService", "Cüzdan hizmeti" },
            { "AJRouter", "AllJoyn yönlendirici (IoT)" },
            { "PhoneSvc", "Telefon hizmeti" },
            { "SEMgrSvc", "Ödeme ve NFC yönetimi" },
            { "WpcMonSvc", "Ebeveyn denetimleri" },
        };

        private CheckItemDto CheckServices()
        {
            var found = new List<string>();
            foreach (var sc in ServiceController.GetServices())
            {
                using (sc)
                {
                    string desc;
                    if (!UnneededServices.TryGetValue(sc.ServiceName, out desc)) continue;
                    bool running = sc.Status == ServiceControllerStatus.Running;
                    bool auto = false;
                    try { auto = sc.StartType == ServiceStartMode.Automatic; } catch { }
                    if (running || auto) found.Add(sc.DisplayName + " (" + sc.ServiceName + "): " + desc + (running ? ", çalışıyor" : ", otomatik başlıyor"));
                }
            }
            if (found.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Sunucuda gereksiz bilinen bir servis çalışmıyor." };

            return new CheckItemDto
            {
                Status = CheckStatus.Bilgi,
                Summary = found.Count + " servis sunucu için gereksiz görünüyor.",
                Detail = "Bu servisler masaüstü / ev kullanımı içindir; sunucuda boşa kaynak tüketir. Yazıcı ile ilgili servisler incelenmedi.\r\n\r\n  " + string.Join("\r\n  ", found),
                HowToFix = "services.msc → servis → Başlangıç türü: Devre dışı, sonra Durdur. Emin olmadığınız servise dokunmayın."
            };
        }

        private static readonly Regex SuspiciousApp = new Regex(
            @"\b(steam|epic games|origin|battle\.net|riot|minecraft|roblox|league of legends|valorant|ubisoft connect|ea app|" +
            @"utorrent|bittorrent|qbittorrent|vuze|frostwire|limewire|toolbar|web companion|driver booster|advanced systemcare|reimage|pc cleaner|" +
            @"mypc ?booster|wise care|segurazo|bytefence|opera gx|spotify|whatsapp|telegram desktop|discord)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private CheckItemDto CheckSuspiciousApps()
        {
            var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var hklm = Hklm())
            {
                foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                    using (var root = hklm.OpenSubKey(path))
                    {
                        if (root == null) continue;
                        foreach (var sub in root.GetSubKeyNames())
                            using (var key = root.OpenSubKey(sub))
                            {
                                string name = key == null ? null : key.GetValue("DisplayName") as string;
                                if (!string.IsNullOrEmpty(name) && SuspiciousApp.IsMatch(name)) found.Add(name);
                            }
                    }
            }
            if (found.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Oyun, torrent, araç çubuğu gibi sunucuya uygun olmayan uygulama yok." };

            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = found.Count + " uygulama sunucu için uygun değil: " + string.Join(", ", found.Take(4)) + (found.Count > 4 ? "..." : ""),
                Detail = "Oyunlar, torrent programları, \"PC hızlandırıcı\" / araç çubuğu türü uygulamalar sunucuda kaynak tüketir ve güvenlik riskidir "
                         + "(çoğu reklam / istenmeyen yazılım taşır).\r\n\r\n  " + string.Join("\r\n  ", found),
                HowToFix = "Denetim Masası → Programlar ve Özellikler'den kaldırın. Kimin kurduğunu öğrenin; sunucuya program kurma yetkisini sınırlayın."
            };
        }

        // ================================================================ İnternet

        private CheckItemDto CheckInternet()
        {
            var detail = new StringBuilder();
            var rtts = new List<long>();
            int sent = 0, lost = 0;
            using (var ping = new Ping())
            {
                foreach (var host in new[] { "1.1.1.1", "8.8.8.8" })
                {
                    var times = new List<long>();
                    for (int i = 0; i < 4; i++)
                    {
                        _ctx.Token.ThrowIfCancellationRequested();
                        sent++;
                        try
                        {
                            var reply = ping.Send(host, 2000);
                            if (reply != null && reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
                            else lost++;
                        }
                        catch (PingException)
                        {
                            lost++;
                        }
                    }
                    rtts.AddRange(times);
                    detail.AppendLine("  Ping " + host + ": " + (times.Count == 0 ? "cevap yok" : "ortalama " + (long)times.Average() + " ms, kayıp " + (4 - times.Count) + "/4"));
                }
            }

            long dnsMs = -1;
            string dnsError = null;
            var sw = Stopwatch.StartNew();
            try
            {
                var task = Dns.GetHostAddressesAsync("www.microsoft.com");
                if (task.Wait(5000)) dnsMs = sw.ElapsedMilliseconds;
                else dnsError = "5 sn içinde cevap gelmedi";
            }
            catch (AggregateException ex)
            {
                dnsError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            }
            detail.AppendLine("  DNS çözümleme (www.microsoft.com): " + (dnsMs >= 0 ? dnsMs + " ms" : "başarısız: " + dnsError));

            if (rtts.Count == 0 && dnsMs < 0)
                return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "İnternete erişilemedi (sunucu internete kapalı olabilir; bu durumda sorun değil).", Detail = detail.ToString() };

            double avg = rtts.Count == 0 ? 0 : rtts.Average();
            double loss = 100.0 * lost / sent;
            var problems = new List<string>();
            if (rtts.Count == 0) problems.Add("ping cevabı yok (ICMP kapalı olabilir)");
            else if (avg > 150) problems.Add("gecikme yüksek (" + (long)avg + " ms)");
            if (rtts.Count > 0 && loss >= 25) problems.Add("paket kaybı " + Fmt.Pct(loss));
            if (dnsMs < 0) problems.Add("DNS çözümleme başarısız");
            else if (dnsMs > 1000) problems.Add("DNS yavaş (" + dnsMs + " ms)");

            bool real = problems.Any(p => !p.StartsWith("ping cevabı yok"));
            return new CheckItemDto
            {
                Status = real ? CheckStatus.Uyari : CheckStatus.Gecti,
                Summary = real ? string.Join(", ", problems) + "." : "Gecikme " + (rtts.Count == 0 ? "ölçülemedi" : (long)avg + " ms") + ", DNS " + dnsMs + " ms.",
                Detail = detail + "\r\nAnlık ölçümdür. Gerçek hız testi bant genişliği tükettiği için otomatik yapılmaz.",
                HowToFix = real ? "Gecikme / kayıp: modem, hat veya güvenlik duvarı yükü. DNS yavaşsa ağ kartındaki DNS sunucularını kontrol edin." : null
            };
        }

        private sealed class ProcessUsage
        {
            public string Name;
            public int Id;
            public int SessionId;
            public long WorkingSet;
            public double Cpu;
        }
    }
}
