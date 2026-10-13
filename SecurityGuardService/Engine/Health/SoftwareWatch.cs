using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SecurityGuard.Shared.Contracts;
using SecurityGuardService.Checks.Performance;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Engine.Events;

namespace SecurityGuardService.Engine.Health
{
    /// <summary>Uzaktan erişim programları (fidye saldırılarında sık kullanılır): ad / yayıncı / dosya yolu eşleşmesi.</summary>
    internal static class RemoteTools
    {
        private static readonly Regex Pattern = new Regex(
            @"anydesk|teamviewer|rustdesk|splashtop|screenconnect|connectwise\s*control|ultraviewer|ultra\s*vnc|tightvnc|realvnc|tigervnc|vnc\s*(server|connect)|"
            + @"chrome\s*remote\s*desktop|remoting_host|atera|logmein|gotoassist|goto\s*resolve|remotepc|zoho\s*assist|dameware|radmin|ammyy|supremo|aeroadmin|"
            + @"netsupport|remote\s*utilities|meshagent|meshcentral|action1|ninjarmm|ninjaone|simplehelp|dwservice|dwagent|getscreen|parsec|nomachine|bomgar|"
            + @"beyondtrust\s*remote|kaseya|pulseway|level\.io|tactical\s*rmm|syncro|n-able|take\s*control|remote\s*desktop\s*manager\s*agent",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Match(params string[] texts)
        {
            foreach (var t in texts)
            {
                if (string.IsNullOrEmpty(t)) continue;
                var m = Pattern.Match(t);
                if (m.Success) return m.Value;
            }
            return null;
        }
    }

    /// <summary>Kurulu program kaydı (Baselines'ta JSON olarak saklanır; serileştirici için üst düzey public tip).</summary>
    public sealed class SoftwareEntry
    {
        public string Key;
        public string Name;
        public string Version;
        public string Publisher;
        public string Location;
        public string User;
    }

    public sealed class UsbDisk
    {
        public string Id;
        public string Model;
        public string Serial;
        public long SizeBytes;
        public string Letters;
    }

    /// <summary>
    /// Kurulan / kaldırılan programlar ve takılan USB diskler (sadece bildirir, hiçbir şey engellenmez):
    /// - Program listesi ("Programlar ve Özellikler": HKLM 64 / 32 bit + yüklü kullanıcı hive'ları) 2 dakikada bir önceki listeyle karşılaştırılır.
    ///   MSI olmayan exe kurulumlar (AnyDesk, TeamViewer...) da yakalanır. Sürüm yükseltmesi (aynı ad, yeni kayıt) bildirilmez.
    ///   Uzaktan erişim programı → Kritik (Win.RemoteToolInstalled), diğerleri → Win.SoftwareInstalled / Win.SoftwareRemoved.
    ///   Kuran kullanıcı MSI kaydından (Application, MsiInstaller) bulunur; yoksa o an bağlı oturumlar yazılır.
    /// - USB diskler (WMI Win32_DiskDrive, arayüz USB): yeni takılan → Win.UsbStorage (model, seri no, boyut, sürücü harfi, oturumlar).
    /// Önceki durumlar Baselines tablosunda: servis kapalıyken olan değişiklik açılışta yakalanır; ilk çalıştırmada sadece kaydedilir.
    /// </summary>
    internal sealed class SoftwareWatch
    {
        private const string Module = "Nöbet";
        private const string Scope = "watch";
        private const string SoftwareKey = "software";
        private const string UsbKey = "usb";
        public static readonly TimeSpan SoftwareInterval = TimeSpan.FromMinutes(2);
        private const int MaxSingleEvents = 5;

        private static readonly Regex UpdateName = new Regex(@"\(KB\d{6,}\)|\bKB\d{6,}\b|^Security Update|^Update for|^Hotfix for", RegexOptions.IgnoreCase);
        private static readonly Regex VersionPart = new Regex(@"[\s\-_]*(v|version|sürüm)?\s*\d+([\.\-]\d+)*[a-z]?\b|\(x64\)|\(x86\)|\(64-bit\)|\(32-bit\)|\s+", RegexOptions.IgnoreCase);

        private readonly GuardDatabase _db;
        private readonly EventRecorder _events;
        private DateTime _lastSoftware = DateTime.MinValue;

        /// <summary>Test için değiştirilebilir kaynaklar.</summary>
        internal Func<List<SoftwareEntry>> SoftwareSource = ReadInstalled;
        internal Func<List<UsbDisk>> UsbSource = ReadUsbDisks;

        public SoftwareWatch(GuardDatabase db, EventRecorder events)
        {
            _db = db;
            _events = events;
        }

        /// <summary>Sistem nöbeti her turda çağırır (30 sn): USB her seferinde, program listesi 2 dk'da bir.</summary>
        public void Tick()
        {
            CheckUsb();
            if (DateTime.UtcNow - _lastSoftware >= SoftwareInterval)
            {
                _lastSoftware = DateTime.UtcNow;
                CheckSoftware();
            }
        }

        // ================================================================ Programlar

        internal void CheckSoftware()
        {
            var now = SoftwareSource();
            var old = Load<List<SoftwareEntry>>(SoftwareKey);
            Save(SoftwareKey, now);
            if (old == null)
            {
                AppLog.Info(Module, "Program listesi kaydedildi (" + now.Count + " program); bundan sonra kurulan / kaldırılanlar bildirilecek.");
                return;
            }

            var oldKeys = new HashSet<string>(old.Select(o => o.Key), StringComparer.OrdinalIgnoreCase);
            var newKeys = new HashSet<string>(now.Select(o => o.Key), StringComparer.OrdinalIgnoreCase);
            var added = now.Where(n => !oldKeys.Contains(n.Key)).ToList();
            var removed = old.Where(o => !newKeys.Contains(o.Key)).ToList();

            // Sürüm yükseltmesi: aynı (sürümsüz) ad hem kalkmış hem eklenmiş → bildirilmez
            var upgraded = new HashSet<string>(added.Select(a => Normalize(a.Name)).Intersect(removed.Select(r => Normalize(r.Name))), StringComparer.OrdinalIgnoreCase);
            added = added.Where(a => !upgraded.Contains(Normalize(a.Name))).ToList();
            removed = removed.Where(r => !upgraded.Contains(Normalize(r.Name))).ToList();
            if (added.Count == 0 && removed.Count == 0) return;

            string sessions = SessionsText();
            var msi = MsiEvents(TimeSpan.FromMinutes(SoftwareInterval.TotalMinutes + 15));

            var remote = added.Where(a => RemoteTools.Match(a.Name, a.Publisher, a.Location) != null).ToList();
            foreach (var a in remote)
                _events.Record(Entry(new GuardEvent("Win.RemoteToolInstalled", "Uzaktan erişim programı kuruldu: " + a.Name + (string.IsNullOrEmpty(a.Version) ? "" : " " + a.Version)), a, msi, sessions, true)
                    .With("Neden önemli", "Saldırganlar sunucuya kalıcı erişim için AnyDesk / TeamViewer gibi araçları kurar (fidye saldırılarının çoğunda görülür).")
                    .With("Ne yapmalı", "Bu kurulumu siz / IT ekibi yapmadıysanız programı hemen kaldırın, kuran hesabın şifresini değiştirin ve RDP oturumlarını kontrol edin."));

            var normal = added.Except(remote).ToList();
            if (normal.Count <= MaxSingleEvents)
                foreach (var a in normal)
                    _events.Record(Entry(new GuardEvent("Win.SoftwareInstalled", "Yeni program kuruldu: " + a.Name + (string.IsNullOrEmpty(a.Version) ? "" : " " + a.Version)), a, msi, sessions, true));
            else
            {
                var ev = new GuardEvent("Win.SoftwareInstalled", normal.Count + " yeni program kuruldu") { Source = "Program listesi" };
                foreach (var a in normal.Take(30)) ev.With("Program", a.Name + " " + a.Version + (string.IsNullOrEmpty(a.Publisher) ? "" : " (" + a.Publisher + ")"));
                ev.With("Sunucudaki oturumlar", sessions);
                _events.Record(ev);
            }

            if (removed.Count <= MaxSingleEvents)
                foreach (var r in removed)
                    _events.Record(Entry(new GuardEvent("Win.SoftwareRemoved", "Program kaldırıldı: " + r.Name + (string.IsNullOrEmpty(r.Version) ? "" : " " + r.Version)), r, msi, sessions, false));
            else
            {
                var ev = new GuardEvent("Win.SoftwareRemoved", removed.Count + " program kaldırıldı") { Source = "Program listesi" };
                foreach (var r in removed.Take(30)) ev.With("Program", r.Name + " " + r.Version);
                ev.With("Sunucudaki oturumlar", sessions);
                _events.Record(ev);
            }
        }

        private static GuardEvent Entry(GuardEvent ev, SoftwareEntry e, List<Tuple<string, string, bool>> msi, string sessions, bool install)
        {
            ev.Source = "Program listesi";
            var who = msi.FirstOrDefault(m => m.Item3 == install && !string.IsNullOrEmpty(m.Item1) && m.Item1.IndexOf(e.Name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (who != null) ev.UserName = who.Item2;
            return ev.With("Program", e.Name)
                .With("Sürüm", Empty(e.Version))
                .With("Yayıncı", Empty(e.Publisher))
                .With("Kurulum yeri", Empty(e.Location))
                .With("Kapsam", e.User == null ? "Tüm kullanıcılar" : "Sadece kullanıcı: " + e.User)
                .With(install ? "Kuran" : "Kaldıran", who != null ? who.Item2 + " (Windows Installer kaydı)" : "Bilinmiyor (MSI kaydı yok)")
                .With("Sunucudaki oturumlar", sessions);
        }

        private static string Empty(string s)
        {
            return string.IsNullOrEmpty(s) ? null : s;
        }

        internal static string Normalize(string name)
        {
            return VersionPart.Replace(name ?? "", "").ToLowerInvariant();
        }

        /// <summary>"Programlar ve Özellikler" listesi (güncellemeler ve sistem bileşenleri hariç).</summary>
        internal static List<SoftwareEntry> ReadInstalled()
        {
            var list = new List<SoftwareEntry>();
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = hklm.OpenSubKey(path))
                    Read(key, (view == RegistryView.Registry64 ? "HKLM64" : "HKLM32"), null, list);
            using (var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64))
                foreach (var sid in users.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-", StringComparison.Ordinal) && !s.EndsWith("_Classes", StringComparison.Ordinal)))
                    using (var key = users.OpenSubKey(sid + @"\" + path))
                        Read(key, "HKU\\" + sid, SidName(sid), list);
            // 64 / 32 bit görünümü aynı anahtarı gösterebilir (yönlendirilmeyen anahtarlar): ad + sürüm + yer ile tekilleştir
            return list.GroupBy(e => e.Name + "|" + e.Version + "|" + e.User, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }

        private static void Read(RegistryKey root, string prefix, string user, List<SoftwareEntry> list)
        {
            if (root == null) return;
            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using (var k = root.OpenSubKey(name))
                    {
                        if (k == null) continue;
                        string display = k.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(display)) continue;
                        if (Convert.ToInt32(k.GetValue("SystemComponent") ?? 0, CultureInfo.InvariantCulture) == 1) continue;
                        if (k.GetValue("ParentKeyName") != null) continue;
                        string release = k.GetValue("ReleaseType") as string;
                        if (release != null && Regex.IsMatch(release, "update|hotfix|service pack", RegexOptions.IgnoreCase)) continue;
                        if (UpdateName.IsMatch(display)) continue;
                        list.Add(new SoftwareEntry
                        {
                            Key = prefix + "\\" + name,
                            Name = display.Trim(),
                            Version = (k.GetValue("DisplayVersion") as string ?? "").Trim(),
                            Publisher = (k.GetValue("Publisher") as string ?? "").Trim(),
                            Location = (k.GetValue("InstallLocation") as string ?? "").Trim(),
                            User = user
                        });
                    }
                }
                catch (Exception)
                {
                    // tek bir bozuk kayıt listeyi durdurmaz
                }
            }
        }

        /// <summary>Son MSI kurulum / kaldırma kayıtları: (ürün metni, kullanıcı, kurulum mu).</summary>
        private static List<Tuple<string, string, bool>> MsiEvents(TimeSpan window)
        {
            var list = new List<Tuple<string, string, bool>>();
            try
            {
                string xpath = "*[System[Provider[@Name='MsiInstaller'] and (EventID=11707 or EventID=1033 or EventID=11724 or EventID=1034) and TimeCreated[timediff(@SystemTime) <= " + (long)window.TotalMilliseconds + "]]]";
                using (var reader = new EventLogReader(new System.Diagnostics.Eventing.Reader.EventLogQuery("Application", PathType.LogName, xpath)))
                    for (EventRecord r = reader.ReadEvent(); r != null; r = reader.ReadEvent())
                        using (r)
                        {
                            string text = string.Join(" ", r.Properties.Select(p => p.Value == null ? "" : p.Value.ToString()));
                            string user = r.UserId == null ? null : SidName(r.UserId.Value);
                            list.Add(Tuple.Create(text, user, r.Id == 11707 || r.Id == 1033));
                        }
            }
            catch (Exception ex)
            {
                AppLog.Debug(Module, "MSI kayıtları okunamadı: " + ex.Message);
            }
            return list;
        }

        // ================================================================ USB diskler

        internal void CheckUsb()
        {
            var now = UsbSource();
            var old = Load<List<UsbDisk>>(UsbKey);
            var oldIds = old == null ? null : new HashSet<string>(old.Select(o => o.Id), StringComparer.OrdinalIgnoreCase);
            bool changed = old == null || old.Count != now.Count || now.Any(n => !oldIds.Contains(n.Id));
            if (changed) Save(UsbKey, now);
            if (old == null) return; // ilk çalıştırma: takılı olanlar bildirilmez

            var added = now.Where(n => !oldIds.Contains(n.Id)).ToList();
            if (added.Count == 0) return;
            string sessions = SessionsText();
            foreach (var d in added)
                _events.Record(new GuardEvent("Win.UsbStorage", "USB disk takıldı: " + d.Model + (string.IsNullOrEmpty(d.Letters) ? "" : " (" + d.Letters + ")"))
                    {
                        Source = "USB izleme"
                    }
                    .With("Aygıt", d.Model)
                    .With("Seri no", Empty(d.Serial))
                    .With("Boyut", d.SizeBytes > 0 ? (d.SizeBytes / 1e9).ToString("0.#", CultureInfo.GetCultureInfo("tr-TR")) + " GB" : null)
                    .With("Sürücü harfi", Empty(d.Letters))
                    .With("Aygıt kimliği", d.Id)
                    .With("Sunucudaki oturumlar", sessions)
                    .With("Not", "Sunucuya USB disk takılması nadirdir: veri kaçırma veya zararlı yazılım bulaştırma olabilir. Bunu siz takmadıysanız kimin taktığını kontrol edin."));
        }

        /// <summary>USB arayüzlü diskler (bellek, harici disk) ve sürücü harfleri.</summary>
        internal static List<UsbDisk> ReadUsbDisks()
        {
            var list = new List<UsbDisk>();
            using (var searcher = new ManagementObjectSearcher("SELECT DeviceID, PNPDeviceID, Model, SerialNumber, Size FROM Win32_DiskDrive WHERE InterfaceType = 'USB'"))
                foreach (ManagementObject disk in searcher.Get())
                    using (disk)
                    {
                        var letters = new List<string>();
                        try
                        {
                            string id = ((string)disk["DeviceID"]).Replace("\\", "\\\\");
                            using (var parts = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + id + "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                                foreach (ManagementObject part in parts.Get())
                                    using (part)
                                    using (var logical = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + part["DeviceID"] + "'} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                                        foreach (ManagementObject l in logical.Get())
                                            using (l) letters.Add((string)l["DeviceID"]);
                        }
                        catch (Exception)
                        {
                            // harf bulunamazsa disk yine bildirilir
                        }
                        list.Add(new UsbDisk
                        {
                            Id = (string)disk["PNPDeviceID"] ?? (string)disk["DeviceID"],
                            Model = ((string)disk["Model"] ?? "USB disk").Trim(),
                            Serial = ((string)disk["SerialNumber"] ?? "").Trim(),
                            SizeBytes = disk["Size"] == null ? 0 : Convert.ToInt64(disk["Size"], CultureInfo.InvariantCulture),
                            Letters = string.Join(", ", letters)
                        });
                    }
            return list;
        }

        // ================================================================ Yardımcılar

        private static string SessionsText()
        {
            try
            {
                var s = WindowsNative.Sessions();
                return s.Count == 0 ? "Yok" : string.Join(", ", s.Select(x => x.User + (x.ClientAddress != null ? " (" + x.ClientAddress + ")" : " (konsol)") + (x.Disconnected ? " bağlantısı kopuk" : "")));
            }
            catch (Exception ex)
            {
                return "Okunamadı: " + ex.Message;
            }
        }

        private static string SidName(string sid)
        {
            try
            {
                return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
            }
            catch (Exception)
            {
                return sid;
            }
        }

        private T Load<T>(string key) where T : class
        {
            string json = _db.Read(c => c.Scalar<string>("SELECT Snapshot FROM Baselines WHERE Scope = @s AND Key = @k", ("@s", Scope), ("@k", key)));
            return json == null ? null : PipeProtocol.FromJson<T>(json);
        }

        private void Save(string key, object value)
        {
            string json = PipeProtocol.ToJson(value), now = DbTime.Now();
            _db.Writer.EnqueueAsync(c => c.Execute(@"INSERT INTO Baselines (Scope, Key, Snapshot, CapturedUtc) VALUES (@s, @k, @j, @t)
                                                     ON CONFLICT(Scope, Key) DO UPDATE SET Snapshot = @j, CapturedUtc = @t",
                ("@s", Scope), ("@k", key), ("@j", json), ("@t", now))).Wait(TimeSpan.FromSeconds(30));
        }
    }
}
