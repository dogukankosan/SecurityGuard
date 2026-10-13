using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SecurityGuard.Shared.Contracts;
using SecurityGuardService.Checks.Performance;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Engine.Collectors;
using SecurityGuardService.Engine.Events;

namespace SecurityGuardService.Engine.Health
{
    /// <summary>Zamanlanmış görev kaydı (Baselines'ta JSON olarak saklanır; serileştirici için üst düzey public tip).</summary>
    public sealed class TaskEntry
    {
        /// <summary>Görev yolu: \Klasör\Ad.</summary>
        public string Path;

        /// <summary>Çalıştırdığı şey + hangi hesapla: değişirse "görev değiştirildi" sayılır (tetikleyici / saat değişimi sayılmaz).</summary>
        public string Sig;

        public string Command;
        public string RunAs;
        public string Author;
        public string Created;
        public string Triggers;
        public bool Elevated;
        public bool Hidden;
    }

    /// <summary>
    /// Zamanlanmış görev takibi (sadece bildirir): görev tanımları (Windows\System32\Tasks altındaki XML dosyaları) dakikada bir
    /// önceki listeyle karşılaştırılır. Windows denetim ayarı ("Diğer nesne erişimi", Security 4698) kapalı olsa da yeni görev yakalanır;
    /// saldırganlar kalıcılık için en çok bunu kullanır.
    /// - Yeni görev veya çalıştırdığı komut / hesabı değişen görev → Win.ScheduledTaskCreated (komut, hesap, tetikleyici, yazan, oturumlar).
    /// - \Microsoft\ altındaki görevleri Windows kendisi sürekli oluşturup siler (güncelleme vb.): orada sadece ŞÜPHELİ olanlar bildirilir
    ///   (Temp / AppData / kullanıcı klasöründen çalışan, cmd / powershell / rundll32 ile çalışan, uzaktan erişim aracı).
    /// - Aynı görev için denetim olayı (4698) zaten yazıldıysa tekrar bildirilmez.
    /// Önceki liste Baselines tablosunda: servis kapalıyken eklenen görev açılışta yakalanır; ilk çalıştırmada sadece kaydedilir.
    /// </summary>
    internal sealed class TaskWatch
    {
        private const string Module = "Nöbet";
        private const string Scope = "watch";
        private const string Key = "tasks";
        private const string EventType = "Win.ScheduledTaskCreated";
        private const int MaxSingleEvents = 8;
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        private readonly GuardDatabase _db;
        private readonly EventRecorder _events;
        private DateTime _last = DateTime.MinValue;
        private List<TaskEntry> _previous;

        /// <summary>Test için değiştirilebilir kaynak.</summary>
        internal Func<List<TaskEntry>> Source = ReadTasks;

        public TaskWatch(GuardDatabase db, EventRecorder events)
        {
            _db = db;
            _events = events;
        }

        /// <summary>Sistem nöbeti her turda çağırır (30 sn); liste dakikada bir karşılaştırılır.</summary>
        public void Tick()
        {
            if (DateTime.UtcNow - _last < Interval) return;
            _last = DateTime.UtcNow;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Check();
            if (sw.ElapsedMilliseconds > 3000) AppLog.Warn(Module, "Zamanlanmış görev karşılaştırması uzun sürdü: " + sw.ElapsedMilliseconds + " ms.");
        }

        internal void Check()
        {
            var now = Source();
            var old = _previous ?? Load(); // veritabanı sadece açılışta okunur
            var before = (old ?? new List<TaskEntry>()).GroupBy(o => o.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // O an okunamayan (yazılmakta olan) görev: bilinen bir görevse önceki kaydı korunur, yeniyse okunabildiği turda ele alınır.
            // Böylece geçici okuma hatası sonraki turda sahte "yeni görev" üretmez.
            now = now.Select(t => t.Sig != null ? t : (before.ContainsKey(t.Path) ? before[t.Path] : null)).Where(t => t != null).ToList();
            _previous = now;
            if (old == null || Changed(old, now)) Save(now); // değişiklik yoksa her dakika veritabanına yazılmaz
            if (old == null)
            {
                AppLog.Info(Module, "Zamanlanmış görev listesi kaydedildi (" + now.Count + " görev); bundan sonra eklenen / değiştirilenler bildirilecek.");
                return;
            }
            var found = new List<Tuple<TaskEntry, bool, string>>(); // görev, yeni mi, şüphe nedeni
            foreach (var t in now)
            {
                TaskEntry prev;
                bool isNew = !before.TryGetValue(t.Path, out prev);
                if (!isNew && string.Equals(prev.Sig, t.Sig, StringComparison.Ordinal)) continue;

                string suspicion = WindowsEventProcessor.ServiceSuspicion(t.Path, t.Command);
                // Windows'un kendi görev ağacı: güncelleme vb. sürekli görev oluşturur; sadece şüpheli olan bildirilir
                if (suspicion == null && t.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;
                found.Add(Tuple.Create(t, isNew, suspicion));
            }
            if (found.Count == 0) return;

            string sessions = SessionsText();
            // Şüpheliler önce: çok sayıda görev birden eklendiyse (program kurulumu) tek tek bildirilenler onlar olsun
            var ordered = found.OrderByDescending(f => f.Item3 != null).ToList();
            foreach (var f in ordered.Take(MaxSingleEvents))
            {
                if (AlreadyReported(f.Item1.Path)) continue; // denetim olayı (4698) kimin oluşturduğuyla birlikte zaten yazıldı
                _events.Record(ToEvent(f.Item1, f.Item2, f.Item3, sessions));
            }
            if (ordered.Count > MaxSingleEvents)
            {
                var rest = ordered.Skip(MaxSingleEvents).ToList();
                _events.Record(new GuardEvent(EventType, rest.Count + " zamanlanmış görev daha eklendi / değiştirildi (toplu)") { Source = "Görev listesi" }
                    .With("Görevler", string.Join(", ", rest.Take(30).Select(r => r.Item1.Path)))
                    .With("Sunucudaki oturumlar", sessions));
            }
        }

        private static GuardEvent ToEvent(TaskEntry t, bool isNew, string suspicion, string sessions)
        {
            string what = (suspicion != null ? "ŞÜPHELİ zamanlanmış görev " : "Zamanlanmış görev ") + (isNew ? "oluşturuldu: " : "değiştirildi (çalıştırdığı komut / hesap): ") + t.Path;
            return new GuardEvent(EventType, what) { Source = "Görev listesi" }
                .With("Görev", t.Path)
                .With("Çalıştırdığı komut", string.IsNullOrEmpty(t.Command) ? "(komut yok)" : t.Command)
                .With("Hangi hesapla", string.IsNullOrEmpty(t.RunAs) ? "Bilinmiyor" : t.RunAs + (t.Elevated ? " (en yüksek yetkiyle)" : ""))
                .With("Ne zaman çalışır", t.Triggers)
                .With("Gizli görev", t.Hidden ? "Evet" : null)
                .With("Görevi yazan (görev kaydındaki bilgi)", t.Author)
                .With("Kayıt tarihi", t.Created)
                .With("Şüphe nedeni", suspicion)
                .With("Sunucudaki oturumlar", sessions)
                .With("Neden önemli", "Saldırganlar sunucuda kalıcı olmak (yeniden başlatmada / belirli saatte tekrar çalışmak) için zamanlanmış görev ekler.")
                .With("Ne yapmalı", "Bu görevi siz / IT ekibi / kurduğunuz bir program eklemediyse Görev Zamanlayıcı'dan devre dışı bırakın, çalıştırdığı dosyayı inceleyin ve oturumları kontrol edin.")
                .With("Not", "Windows görev denetimi kapalı olduğu için kimin oluşturduğu kaydedilmedi; o an bağlı oturumlar yukarıda. Kimin oluşturduğunu görmek için Güvenlik Önerileri'ndeki 'Windows audit politikası' maddesine bakın.");
        }

        private static bool Changed(List<TaskEntry> a, List<TaskEntry> b)
        {
            if (a.Count != b.Count) return true;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in a) map[x.Path] = x.Sig;
            foreach (var y in b)
            {
                string sig;
                if (!map.TryGetValue(y.Path, out sig) || !string.Equals(sig, y.Sig, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private bool AlreadyReported(string path)
        {
            string since = DbTime.ToDb(DateTime.UtcNow.AddMinutes(-10));
            string like = "%" + path.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            _db.Writer.EnqueueAsync(c => { }).Wait(TimeSpan.FromSeconds(30)); // kuyruktaki denetim olayı yazılsın
            return _db.Read(c => c.Scalar<long>(
                "SELECT COUNT(*) FROM EventLog WHERE EventType = @t AND TimeUtc >= @s AND Summary LIKE @m ESCAPE '\\'",
                ("@t", EventType), ("@s", since), ("@m", like))) > 0;
        }

        // ================================================================ Okuma

        /// <summary>Görev tanımlarını okur (SADECE OKUR). Okunamayan dosya atlanır.</summary>
        internal static List<TaskEntry> ReadTasks()
        {
            // 32 bit işlemde System32 → SysWOW64'e yönlenir; gerçek klasör Sysnative ile okunur
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string root = Path.Combine(windows, !Environment.Is64BitProcess && Environment.Is64BitOperatingSystem ? "Sysnative" : "System32", "Tasks");
            var list = new List<TaskEntry>();
            if (!Directory.Exists(root)) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (FileCache)
            {
                Walk(root, new DirectoryInfo(root), list, seen);
                foreach (string gone in FileCache.Keys.Where(k => !seen.Contains(k)).ToList()) FileCache.Remove(gone);
            }
            return list;
        }

        /// <summary>Dosya yolu → (son yazma zamanı, boyut, ayrıştırılmış görev). Değişmeyen dosya tekrar okunmaz: tur başına maliyet klasör listelemesi kadar.</summary>
        private static readonly Dictionary<string, Tuple<long, long, TaskEntry>> FileCache = new Dictionary<string, Tuple<long, long, TaskEntry>>(StringComparer.OrdinalIgnoreCase);

        private static void Walk(string root, DirectoryInfo dir, List<TaskEntry> list, HashSet<string> seen)
        {
            FileInfo[] files;
            DirectoryInfo[] dirs;
            try
            {
                files = dir.GetFiles();
                dirs = dir.GetDirectories();
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            foreach (var file in files)
            {
                string path = file.FullName.Substring(root.Length); // "\Klasör\Ad"
                seen.Add(path);
                long stamp = file.LastWriteTimeUtc.Ticks, size = file.Length;
                Tuple<long, long, TaskEntry> cached;
                if (FileCache.TryGetValue(path, out cached) && cached.Item1 == stamp && cached.Item2 == size)
                {
                    list.Add(cached.Item3);
                    continue;
                }
                try
                {
                    var entry = Parse(path, File.ReadAllText(file.FullName));
                    FileCache[path] = Tuple.Create(stamp, size, entry);
                    list.Add(entry);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException || ex is InvalidOperationException)
                {
                    // Yazılırken okunan / bozuk dosya: Sig = null ile işaretlenir (önbelleğe alınmaz), bir sonraki turda tekrar denenir
                    list.Add(new TaskEntry { Path = path, Sig = null });
                    AppLog.Debug(Module, "Görev tanımı okunamadı: " + path + " (" + ex.Message + ")");
                }
            }
            foreach (var sub in dirs) Walk(root, sub, list, seen);
        }

        internal static TaskEntry Parse(string path, string xml)
        {
            var doc = XDocument.Parse(xml);
            var task = doc.Root;
            var actions = task == null ? null : task.Element(Ns + "Actions");
            var commands = new List<string>();
            if (actions != null)
                foreach (var a in actions.Elements())
                {
                    if (a.Name.LocalName == "Exec")
                    {
                        string cmd = Val(a, "Command"), args = Val(a, "Arguments");
                        commands.Add((cmd + (string.IsNullOrEmpty(args) ? "" : " " + args)).Trim());
                    }
                    else if (a.Name.LocalName == "ComHandler")
                        commands.Add("COM bileşeni " + Val(a, "ClassId"));
                    else
                        commands.Add(a.Name.LocalName);
                }

            var principal = task == null || task.Element(Ns + "Principals") == null ? null : task.Element(Ns + "Principals").Elements().FirstOrDefault();
            string user = principal == null ? null : (Val(principal, "UserId") ?? Val(principal, "GroupId"));
            var reg = task == null ? null : task.Element(Ns + "RegistrationInfo");
            var triggers = task == null ? null : task.Element(Ns + "Triggers");
            var settings = task == null ? null : task.Element(Ns + "Settings");
            string command = string.Join(" ; ", commands);

            return new TaskEntry
            {
                Path = path,
                Command = command.Length > 600 ? command.Substring(0, 600) + "…" : command,
                RunAs = AccountName(user),
                Elevated = principal != null && string.Equals(Val(principal, "RunLevel"), "HighestAvailable", StringComparison.OrdinalIgnoreCase),
                Author = reg == null ? null : Val(reg, "Author"),
                Created = reg == null ? null : Val(reg, "Date"),
                Triggers = triggers == null || !triggers.Elements().Any() ? "Tetikleyici yok (elle / başka görevle)" : string.Join(", ", triggers.Elements().Select(t => TriggerName(t.Name.LocalName)).Distinct()),
                Hidden = settings != null && string.Equals(Val(settings, "Hidden"), "true", StringComparison.OrdinalIgnoreCase),
                Sig = command + "|" + (user ?? "")
            };
        }

        private static string Val(XElement parent, string name)
        {
            var e = parent.Element(Ns + name);
            return e == null || string.IsNullOrWhiteSpace(e.Value) ? null : e.Value.Trim();
        }

        private static string TriggerName(string element)
        {
            switch (element)
            {
                case "BootTrigger": return "sunucu açılışında";
                case "LogonTrigger": return "kullanıcı girişinde";
                case "TimeTrigger": return "belirli bir zamanda";
                case "CalendarTrigger": return "takvime göre (günlük / haftalık)";
                case "IdleTrigger": return "sunucu boştayken";
                case "EventTrigger": return "bir Windows olayında";
                case "RegistrationTrigger": return "oluşturulunca hemen";
                case "SessionStateChangeTrigger": return "oturum açılıp kapanınca";
                default: return element;
            }
        }

        /// <summary>S-1-5-18 gibi SID'leri okunur ada çevirir; çevrilemezse olduğu gibi.</summary>
        private static string AccountName(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) return value;
            try
            {
                return new System.Security.Principal.SecurityIdentifier(value).Translate(typeof(System.Security.Principal.NTAccount)).Value;
            }
            catch (Exception)
            {
                return value;
            }
        }

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

        private List<TaskEntry> Load()
        {
            string json = _db.Read(c => c.Scalar<string>("SELECT Snapshot FROM Baselines WHERE Scope = @s AND Key = @k", ("@s", Scope), ("@k", Key)));
            return json == null ? null : PipeProtocol.FromJson<List<TaskEntry>>(json);
        }

        private void Save(List<TaskEntry> value)
        {
            string json = PipeProtocol.ToJson(value), now = DbTime.Now();
            _db.Writer.EnqueueAsync(c => c.Execute(@"INSERT INTO Baselines (Scope, Key, Snapshot, CapturedUtc) VALUES (@s, @k, @j, @t)
                                                     ON CONFLICT(Scope, Key) DO UPDATE SET Snapshot = @j, CapturedUtc = @t",
                ("@s", Scope), ("@k", Key), ("@j", json), ("@t", now))).Wait(TimeSpan.FromSeconds(30));
        }
    }
}
