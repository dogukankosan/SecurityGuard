using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;
using SecurityGuardService.Checks;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Events;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Engine.Health
{
    /// <summary>
    /// Periyodik sistem nöbeti (olay logu olmayan değişiklikler):
    /// - RDP ayarları (aç / kapa, port, NLA, güvenlik katmanı) 30 sn'de bir kayıt defterinden; servis kapalıyken
    ///   değişen de açılışta yakalanır (son durum ayarlarda saklanır).
    /// - Disk doluluğu 15 dk'da bir (Genel Ayarlar → Disk doluluk uyarısı); aynı disk için en fazla 24 saatte bir olay.
    /// - SecurityGuard'ın kendi modülleri: aynı modül 1 saatte 5+ hata verirse olay (6 saatte bir).
    /// - İşlemci / bellek: 30 sn'de bir ölçüm, dakikalık geçmiş, uzun süreli yüksek kullanımda olay (ResourceMonitor).
    /// </summary>
    internal sealed class SystemWatch : IDisposable
    {
        private const string Module = "Nöbet";
        private const string RdpSnapshotKey = "watch.rdpSnapshot";
        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DiskInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan DiskRepeat = TimeSpan.FromHours(24);
        private static readonly TimeSpan ErrorWindow = TimeSpan.FromHours(1);
        private static readonly TimeSpan ErrorRepeat = TimeSpan.FromHours(6);
        private const int ErrorThreshold = 5;

        // Bu modüllerin hataları sayılmaz (olay kaydının kendisi / bu sınıf: döngü olmasın)
        private static readonly HashSet<string> IgnoredModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Olay", Module };

        private readonly SettingsStore _settings;
        private readonly EventRecorder _events;
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private readonly ConcurrentQueue<Tuple<DateTime, string, string>> _errors = new ConcurrentQueue<Tuple<DateTime, string, string>>();
        private readonly Dictionary<string, List<DateTime>> _errorTimes = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _errorAlarmed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _diskAlarmed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastDisk = DateTime.MinValue;
        private Thread _thread;

        public SystemWatch(GuardDatabase db, SettingsStore settings, EventRecorder events)
        {
            _settings = settings;
            _events = events;
            Resources = new ResourceMonitor(db, settings, events);
            Software = new SoftwareWatch(db, events);
            Tasks = new TaskWatch(db, events);
        }

        /// <summary>Zamanlanmış görev takibi (Windows denetimi kapalı olsa da yeni görevleri yakalar).</summary>
        public TaskWatch Tasks { get; private set; }

        /// <summary>Kurulan / kaldırılan programlar ve USB diskler.</summary>
        public SoftwareWatch Software { get; private set; }

        /// <summary>İşlemci / bellek takibi (Panel anlık değerleri buradan okur).</summary>
        public ResourceMonitor Resources { get; private set; }

        public void Start()
        {
            AppLog.ErrorWritten += OnError;
            _thread = new Thread(Loop) { IsBackground = true, Name = "SystemWatch", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        private void OnError(string module, string message)
        {
            if (module == null || IgnoredModules.Contains(module)) return;
            if (_errors.Count < 10000) _errors.Enqueue(Tuple.Create(DateTime.UtcNow, module, message));
        }

        private void Loop()
        {
            if (_stop.Wait(TimeSpan.FromSeconds(5))) return;
            while (!_stop.IsSet)
            {
                if (!LicenseGate.Active)
                {
                    if (_stop.Wait(Tick)) return;
                    continue;
                }
                Safe("işlemci / bellek", Resources.Sample);
                Safe("RDP ayarları", CheckRdp);
                Safe("program / USB listesi", Software.Tick);
                Safe("zamanlanmış görevler", Tasks.Tick);
                if (DateTime.UtcNow - _lastDisk >= DiskInterval)
                {
                    _lastDisk = DateTime.UtcNow;
                    Safe("disk doluluğu", CheckDisks);
                }
                Safe("modül hataları", CheckErrors);
                if (_stop.Wait(Tick)) return;
            }
        }

        private static void Safe(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, what + " kontrol edilemedi: " + ex.Message);
            }
        }

        // ================================================================ RDP ayarları

        private static readonly string[] RdpKeys = { "RDP", "Port", "NLA", "Güvenlik katmanı" };

        internal static Dictionary<string, string> ReadRdp()
        {
            const string ts = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
            const string tcp = ts + @"\WinStations\RDP-Tcp";
            const string policy = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                Func<string, string, int?> get = (path, name) =>
                {
                    using (var key = hklm.OpenSubKey(path))
                    {
                        var v = key == null ? null : key.GetValue(name);
                        return v is int ? (int?)(int)v : null;
                    }
                };
                int deny = get(policy, "fDenyTSConnections") ?? get(ts, "fDenyTSConnections") ?? 1;
                int nla = get(policy, "UserAuthentication") ?? get(tcp, "UserAuthentication") ?? 1;
                int layer = get(policy, "SecurityLayer") ?? get(tcp, "SecurityLayer") ?? 1;
                return new Dictionary<string, string>
                {
                    { "RDP", deny == 1 ? "kapalı" : "açık" },
                    { "Port", (get(tcp, "PortNumber") ?? 3389).ToString() },
                    { "NLA", nla == 0 ? "kapalı" : "açık" },
                    { "Güvenlik katmanı", layer == 0 ? "RDP (TLS yok)" : layer == 1 ? "Negotiate" : "TLS" }
                };
            }
        }

        internal static string Serialize(Dictionary<string, string> map)
        {
            return string.Join(";", RdpKeys.Select(k => k + "=" + (map.ContainsKey(k) ? map[k] : "")));
        }

        internal static Dictionary<string, string> Parse(string text)
        {
            var map = new Dictionary<string, string>();
            foreach (var part in (text ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) map[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            return map;
        }

        private void CheckRdp()
        {
            var now = ReadRdp();
            string stored = _settings.Get(RdpSnapshotKey);
            string current = Serialize(now);
            if (stored == current) return;
            _settings.SetAsync(RdpSnapshotKey, current).Wait(TimeSpan.FromSeconds(30));
            if (string.IsNullOrEmpty(stored)) return; // ilk çalıştırma: sadece kaydedilir

            var before = Parse(stored);
            var changes = RdpKeys.Where(k => before.ContainsKey(k) && before[k] != now[k]).ToList();
            if (changes.Count == 0) return;

            var warnings = new List<string>();
            if (changes.Contains("NLA") && now["NLA"] == "kapalı") warnings.Add("NLA KAPATILDI: oturum ekranı kimlik doğrulamadan açılıyor (brute force ve RDP açıklarına karşı koruma kalktı).");
            if (changes.Contains("Güvenlik katmanı") && now["Güvenlik katmanı"].StartsWith("RDP")) warnings.Add("TLS kapatıldı: RDP trafiği zayıf şifrelemeyle gidiyor.");
            if (changes.Contains("RDP") && now["RDP"] == "açık") warnings.Add("Uzak masaüstü bağlantıları AÇILDI.");
            if (changes.Contains("Port")) warnings.Add("RDP portu değişti: firewall'da yeni porta izin olduğundan ve eski porta ait kuralın kapatıldığından emin olun.");

            var e = new GuardEvent("Win.RdpSettingsChanged", "RDP ayarı değişti: " + string.Join(", ", changes.Select(k => k + " " + before[k] + " → " + now[k])) + ".")
            {
                Source = "Kayıt defteri (RDP-Tcp / Terminal Server)"
            };
            foreach (var k in changes) e.With(k, before[k] + " → " + now[k]);
            foreach (var w in warnings) e.With("Dikkat", w);
            e.With("Not", "Kayıt defteri değişikliği kimin yaptığını göstermez: aynı saatteki RDP oturumlarına ve Olay Logu'na bakın. Bunu siz yapmadıysanız sunucuya yetkisiz erişim olabilir.");
            _events.Record(e);
        }

        // ================================================================ Disk

        private void CheckDisks()
        {
            int alarm = GeneralSettings.GetInt(_settings, SettingKeys.DiskAlarmPercent);
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                double used = 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize;
                string name = d.Name.TrimEnd('\\');
                if (used < alarm)
                {
                    _diskAlarmed.Remove(name); // eşik altına inince bir dahaki aşımda hemen haber verilir
                    continue;
                }
                DateTime last;
                if (_diskAlarmed.TryGetValue(name, out last) && DateTime.UtcNow - last < DiskRepeat) continue;
                _diskAlarmed[name] = DateTime.UtcNow;

                bool critical = used >= 98 || d.TotalFreeSpace < 2L * 1024 * 1024 * 1024;
                _events.Record(new GuardEvent("Guard.DiskLow", name + " diski " + Fmt.Pct(used) + " dolu (boş " + Fmt.Bytes(d.TotalFreeSpace) + ")" + (critical ? ": NEREDEYSE DOLU." : "."))
                    { Source = "Disk kontrolü" }
                    .With("Disk", name + " (" + d.VolumeLabel + ")")
                    .With("Boş / toplam", Fmt.Bytes(d.TotalFreeSpace) + " / " + Fmt.Bytes(d.TotalSize))
                    .With("Uyarı eşiği", "%" + alarm + " (Genel Ayarlar)")
                    .With("Etkisi", "Disk dolarsa SQL Server veritabanlarını büyütemez ve durur, Windows ve kullanıcı oturumları bozulur.")
                    .With("Ne yapmalı", "Performans Önerileri'nde Geçici dosyalar, Windows Update önbelleği, Eski profiller, Log dosyaları ve En büyük tablolar maddeleri yer açma yollarını gösterir."));
            }
        }

        // ================================================================ Modül hataları

        private void CheckErrors()
        {
            var now = DateTime.UtcNow;
            var last = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Tuple<DateTime, string, string> item;
            while (_errors.TryDequeue(out item))
            {
                List<DateTime> times;
                if (!_errorTimes.TryGetValue(item.Item2, out times)) _errorTimes[item.Item2] = times = new List<DateTime>();
                times.Add(item.Item1);
                last[item.Item2] = item.Item3;
            }
            foreach (var pair in _errorTimes.ToList())
            {
                pair.Value.RemoveAll(t => now - t > ErrorWindow);
                if (pair.Value.Count == 0)
                {
                    _errorTimes.Remove(pair.Key);
                    continue;
                }
                if (pair.Value.Count < ErrorThreshold) continue;
                DateTime alarmed;
                if (_errorAlarmed.TryGetValue(pair.Key, out alarmed) && now - alarmed < ErrorRepeat) continue;
                _errorAlarmed[pair.Key] = now;
                string message;
                last.TryGetValue(pair.Key, out message);
                _events.Record(new GuardEvent("Guard.ModuleError", "SecurityGuard'ın \"" + pair.Key + "\" bölümü son 1 saatte " + pair.Value.Count + " kez hata verdi.")
                    { Source = "SecurityGuard servisi" }
                    .With("Son hata", message)
                    .With("Etkisi", "Bu bölümün işi (izleme, mail, yedek vb.) aksıyor olabilir; diğer bölümler çalışmaya devam eder.")
                    .With("Ne yapmalı", "Servis klasöründeki Logs\\ altındaki günün log dosyasında \"" + pair.Key + "\" satırlarına bakın; çözülemezse logu destek ekibine gönderin."));
            }
        }

        public void Dispose()
        {
            AppLog.ErrorWritten -= OnError;
            _stop.Set();
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
