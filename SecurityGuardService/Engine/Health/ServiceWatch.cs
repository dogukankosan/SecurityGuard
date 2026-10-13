using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Engine.Events;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Engine.Health
{
    /// <summary>
    /// Servis takibi: seçilen Windows servisleri 15 sn'de bir kontrol edilir.
    /// - Çalışırken durursa "Svc.Stopped" (Kritik + mail): durma nedeni System logundan (7034 çöktü, 7031 / 7024 / 7023 hata ile durdu,
    ///   7036 normal durdurma), başlangıç türü (devre dışı bırakılmış mı). Tekrar çalışınca "Svc.Recovered".
    /// - Servis bazında "otomatik yeniden başlat" (varsayılan kapalı): 30 sn, 2 dk, 5 dk sonra dener; devre dışı bırakılmış servise dokunmaz.
    /// - İlk açılışta önerilenler (otomatik başlayan SQL Server / Agent, LOGO, IIS, Firewall, Defender, Olay Günlüğü, Uzak Masaüstü) izlemeye alınır.
    /// - Son bilinen durum Baselines'ta: servis kapalıyken duran servis açılışta bildirilir.
    /// </summary>
    internal sealed class ServiceWatch : IDisposable
    {
        private const string Module = "ServisTakibi";
        private const string Scope = "watch";
        private const string StateKey = "services";
        private const string InitializedKey = "services.initialized";
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan[] RestartDelays = { TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5) };

        private static readonly Regex RecommendedName = new Regex(@"^(MSSQLSERVER|MSSQL\$.+|SQLSERVERAGENT|SQLAgent\$.+|W3SVC|MpsSvc|WinDefend|EventLog|TermService)$", RegexOptions.IgnoreCase);
        private static readonly Regex RecommendedDisplay = new Regex(@"\bLOGO\b|Netsis|Mikro|Nebim|Eta\s+SQL", RegexOptions.IgnoreCase);

        private readonly GuardDatabase _db;
        private readonly EventRecorder _events;
        private readonly object _lock = new object();
        private readonly Dictionary<string, Restart> _restarts = new Dictionary<string, Restart>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _last; // ad → "Running" / "Stopped" / "Missing"
        private Timer _timer;
        private int _busy;

        /// <summary>Servis listesi / durumu (test için değiştirilebilir).</summary>
        internal Func<List<Svc>> ServiceSource = ReadServices;
        internal Func<string, bool> Starter = StartService;
        internal Func<DateTime> Clock = () => DateTime.UtcNow;

        internal sealed class Svc
        {
            public string Name, DisplayName, Status, StartType;
        }

        private sealed class Restart
        {
            public int Attempt;
            public DateTime NextUtc;
        }

        private sealed class Row
        {
            public string Name, DisplayName;
            public bool AutoRestart;
        }

        public ServiceWatch(GuardDatabase db, EventRecorder events)
        {
            _db = db;
            _events = events;
        }

        public void Start()
        {
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(20), Interval);
        }

        public void Dispose()
        {
            var t = _timer;
            _timer = null;
            if (t != null) t.Dispose();
        }

        private void Tick()
        {
            if (!LicenseGate.Active) return; // lisans yok: takip / yeniden başlatma yapılmaz
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                Check();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Servisler kontrol edilemedi.", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        // ================================================================ Kontrol

        internal void Check()
        {
            var all = ServiceSource();
            EnsureInitialized(all);
            var watched = LoadWatched();
            var byName = all.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                if (_last == null) _last = LoadState() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool changed = false;
                foreach (var w in watched)
                {
                    Svc s;
                    string now = byName.TryGetValue(w.Name, out s) ? Simple(s.Status) : "Missing";
                    if (now == null) continue; // başlıyor / duruyor: kesin durumu bekle
                    string before;
                    bool known = _last.TryGetValue(w.Name, out before);
                    if (!known || before != now)
                    {
                        _last[w.Name] = now;
                        changed = true;
                        if (known) Transition(w, s, before, now);
                        else if (now != "Running") Transition(w, s, "Running", now, true); // izlemeye yeni alındı / servis kapalıyken durmuş
                    }
                    if (now != "Running" && w.AutoRestart && s != null) TryRestart(w, s);
                }
                foreach (var gone in _last.Keys.Where(k => !watched.Any(w => string.Equals(w.Name, k, StringComparison.OrdinalIgnoreCase))).ToList())
                {
                    _last.Remove(gone);
                    changed = true;
                }
                if (changed) SaveState(_last);
            }
        }

        private void Transition(Row w, Svc s, string before, string now, bool atStart = false)
        {
            string name = (w.DisplayName ?? w.Name) + " (" + w.Name + ")";
            if (now == "Running")
            {
                _restarts.Remove(w.Name);
                if (before == "Stopped" || before == "Missing")
                    _events.Record(new GuardEvent("Svc.Recovered", "Servis tekrar çalışıyor: " + name) { Source = "Servis takibi" }
                        .With("Servis", name).With("Başlangıç türü", s == null ? null : s.StartType));
                return;
            }

            string reason = now == "Missing" ? "Servis bulunamadı (kaldırılmış olabilir)." : Reason(w.DisplayName ?? w.Name);
            var ev = new GuardEvent("Svc.Stopped", (now == "Missing" ? "İzlenen servis kaldırıldı: " : "İzlenen servis durdu: ") + name) { Source = "Servis takibi" }
                .With("Servis", name)
                .With("Neden", reason)
                .With("Başlangıç türü", s == null ? null : s.StartType + (s.StartType == "Devre dışı" ? " — biri servisi DEVRE DIŞI bırakmış" : ""))
                .With("Fark edilme", atStart ? "Kontrol sırasında durmuş bulundu (SecurityGuard kapalıyken veya izlemeye alınmadan önce durmuş olabilir)" : null)
                .With("Otomatik yeniden başlatma", w.AutoRestart ? (s != null && s.StartType == "Devre dışı" ? "açık ama servis devre dışı olduğu için yapılmayacak" : "açık: 30 sn sonra denenecek") : "kapalı (Servis Takibi sayfasından açılabilir)");
            string advice = Advice(w.Name);
            if (advice != null) ev.With("Neden önemli", advice);
            _events.Record(ev);
        }

        private void TryRestart(Row w, Svc s)
        {
            if (s.StartType == "Devre dışı") return;
            var now = Clock();
            Restart r;
            if (!_restarts.TryGetValue(w.Name, out r))
            {
                _restarts[w.Name] = new Restart { Attempt = 0, NextUtc = now + RestartDelays[0] };
                return;
            }
            if (r.Attempt >= RestartDelays.Length || now < r.NextUtc) return;
            r.Attempt++;
            bool ok = false;
            string error = null;
            try
            {
                ok = Starter(w.Name);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            string name = (w.DisplayName ?? w.Name) + " (" + w.Name + ")";
            if (ok)
            {
                AppLog.Info(Module, name + " yeniden başlatıldı (deneme " + r.Attempt + ").");
                _events.Record(new GuardEvent("Svc.Recovered", "Servis SecurityGuard tarafından yeniden başlatıldı: " + name) { Source = "Servis takibi" }
                    .With("Servis", name).With("Deneme", r.Attempt));
                _last[w.Name] = "Running";
                _restarts.Remove(w.Name);
                SaveState(_last);
                return;
            }
            AppLog.Warn(Module, name + " başlatılamadı (deneme " + r.Attempt + "): " + error);
            if (r.Attempt < RestartDelays.Length) r.NextUtc = now + RestartDelays[r.Attempt];
            else
                _events.Record(new GuardEvent("Svc.Stopped", "Servis " + RestartDelays.Length + " denemede yeniden başlatılamadı: " + name) { Source = "Servis takibi" }
                    .With("Servis", name).With("Son hata", error ?? "servis başlamadı (zaman aşımı)").With("Ne yapmalı", "Servis loglarını ve Windows System logunu kontrol edin; sorun düzelince servis takibi tekrar dener."));
        }

        /// <summary>Durma nedeni: son 10 dakikanın System logundan (Service Control Manager).</summary>
        private static string Reason(string displayName)
        {
            try
            {
                string xpath = "*[System[Provider[@Name='Service Control Manager'] and (EventID=7034 or EventID=7031 or EventID=7024 or EventID=7023 or EventID=7036) and TimeCreated[timediff(@SystemTime) <= 600000]]]";
                using (var reader = new EventLogReader(new System.Diagnostics.Eventing.Reader.EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true }))
                    for (EventRecord r = reader.ReadEvent(); r != null; r = reader.ReadEvent())
                        using (r)
                        {
                            var props = r.Properties.Select(p => p.Value == null ? "" : p.Value.ToString()).ToList();
                            if (props.Count == 0 || !string.Equals(props[0], displayName, StringComparison.OrdinalIgnoreCase)) continue;
                            string when = r.TimeCreated.HasValue ? r.TimeCreated.Value.ToString("HH:mm:ss") + " " : "";
                            switch (r.Id)
                            {
                                case 7034: return when + "Beklenmedik şekilde sonlandı (çöktü) — " + (props.Count > 1 ? props[1] + ". kez" : "");
                                case 7031: return when + "Beklenmedik şekilde sonlandı (çöktü); Windows kurtarma eylemi uygulanacak.";
                                case 7024: return when + "Hata ile durdu: " + (props.Count > 1 ? props[1] : "");
                                case 7023: return when + "Hata ile durdu: " + (props.Count > 1 ? props[1] : "");
                                case 7036:
                                    if (props.Count > 1 && Regex.IsMatch(props[1], "stopped|durduruldu|durdu", RegexOptions.IgnoreCase))
                                        return when + "Normal şekilde durduruldu (bir kullanıcı veya program durdurdu; Windows durduranı kaydetmez).";
                                    break;
                            }
                        }
            }
            catch (Exception ex)
            {
                return "Okunamadı: " + ex.Message;
            }
            return "Bilinmiyor (System logunda kayıt yok).";
        }

        private static string Advice(string name)
        {
            switch (name.ToUpperInvariant())
            {
                case "MPSSVC": return "Windows Firewall servisi durdu: SecurityGuard ban kuralları ve port kısıtları ÇALIŞMAZ. Saldırganlar önce bunu kapatır.";
                case "WINDEFEND": return "Windows Defender durdu: virüs / fidye yazılımı koruması yok. Saldırganlar önce bunu kapatır.";
                case "EVENTLOG": return "Windows Olay Günlüğü durdu: giriş denemeleri ve olaylar kaydedilmez, SecurityGuard da göremez. İz silme belirtisi olabilir.";
                case "TERMSERVICE": return "Uzak Masaüstü servisi durdu: kullanıcılar RDP ile bağlanamaz.";
            }
            if (name.StartsWith("MSSQL", StringComparison.OrdinalIgnoreCase)) return "SQL Server durdu: uygulamalar (LOGO vb.) veritabanına bağlanamaz.";
            if (name.StartsWith("SQLAgent", StringComparison.OrdinalIgnoreCase) || name.Equals("SQLSERVERAGENT", StringComparison.OrdinalIgnoreCase))
                return "SQL Agent durdu: zamanlanmış SQL işleri (yedek, bakım) çalışmaz.";
            return null;
        }

        private static string Simple(string status)
        {
            switch (status)
            {
                case "Çalışıyor": return "Running";
                case "Durdu": return "Stopped";
                case "Duraklatıldı": return "Stopped";
                default: return null;
            }
        }

        // ================================================================ Arayüz

        public ServiceWatchStateDto GetState()
        {
            var all = ServiceSource();
            EnsureInitialized(all);
            var watched = LoadWatched().ToDictionary(w => w.Name, StringComparer.OrdinalIgnoreCase);
            var lastEvents = LastEvents();
            var state = new ServiceWatchStateDto();
            foreach (var s in all.OrderBy(s => s.DisplayName))
            {
                Row w;
                watched.TryGetValue(s.Name, out w);
                string last;
                lastEvents.TryGetValue(s.Name, out last);
                state.Services.Add(new WatchedServiceDto
                {
                    Name = s.Name,
                    DisplayName = s.DisplayName,
                    Status = s.Status,
                    StartType = s.StartType,
                    Watched = w != null,
                    AutoRestart = w != null && w.AutoRestart,
                    Recommended = IsRecommended(s),
                    LastEvent = w == null ? null : last
                });
            }
            // İzlenen ama artık olmayan servisler
            foreach (var w in watched.Values.Where(w => !all.Any(s => string.Equals(s.Name, w.Name, StringComparison.OrdinalIgnoreCase))))
                state.Services.Add(new WatchedServiceDto { Name = w.Name, DisplayName = w.DisplayName, Status = "Bulunamadı", Watched = true, AutoRestart = w.AutoRestart });
            return state;
        }

        public async Task SaveAsync(ServiceWatchSaveRequest req, string userName)
        {
            var items = (req == null ? null : req.Items) ?? new List<WatchedServiceDto>();
            var all = ServiceSource().ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
            var before = LoadWatched();
            var after = items.Where(i => i.Watched && !string.IsNullOrWhiteSpace(i.Name)).GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            string now = DbTime.Now();
            await _db.Writer.EnqueueAsync(c =>
            {
                c.Execute("DELETE FROM WatchedServices");
                foreach (var i in after)
                {
                    Svc s;
                    c.Execute("INSERT INTO WatchedServices (Name, DisplayName, AutoRestart, UpdatedUtc, UpdatedBy) VALUES (@n, @d, @a, @t, @u)",
                        ("@n", i.Name), ("@d", all.TryGetValue(i.Name, out s) ? s.DisplayName : i.DisplayName), ("@a", i.AutoRestart ? 1 : 0), ("@t", now), ("@u", userName));
                }
            }).ConfigureAwait(false);
            var added = after.Where(a => !before.Any(b => string.Equals(b.Name, a.Name, StringComparison.OrdinalIgnoreCase))).Select(a => a.Name).ToList();
            var removed = before.Where(b => !after.Any(a => string.Equals(b.Name, a.Name, StringComparison.OrdinalIgnoreCase))).Select(b => b.Name).ToList();
            var restartChanged = after.Where(a => before.Any(b => string.Equals(b.Name, a.Name, StringComparison.OrdinalIgnoreCase) && b.AutoRestart != a.AutoRestart)).Select(a => a.Name + (a.AutoRestart ? " (yeniden başlatma açıldı)" : " (yeniden başlatma kapatıldı)")).ToList();
            AuditTrail.Record(_db, userName, "Servis takibi kaydedildi", after.Count + " servis izleniyor"
                + (added.Count > 0 ? "; eklenen: " + string.Join(", ", added) : "") + (removed.Count > 0 ? "; çıkarılan: " + string.Join(", ", removed) : "")
                + (restartChanged.Count > 0 ? "; " + string.Join(", ", restartChanged) : ""));
            Tick();
        }

        private void EnsureInitialized(List<Svc> all)
        {
            string flag = _db.Read(c => c.Scalar<string>("SELECT Snapshot FROM Baselines WHERE Scope = @s AND Key = @k", ("@s", Scope), ("@k", InitializedKey)));
            if (flag != null) return;
            var rec = all.Where(s => IsRecommended(s) && s.StartType == "Otomatik").ToList();
            string now = DbTime.Now();
            _db.Writer.EnqueueAsync(c =>
            {
                foreach (var s in rec)
                    c.Execute("INSERT OR IGNORE INTO WatchedServices (Name, DisplayName, AutoRestart, UpdatedUtc, UpdatedBy) VALUES (@n, @d, 0, @t, 'Sistem')",
                        ("@n", s.Name), ("@d", s.DisplayName), ("@t", now));
                c.Execute("INSERT OR REPLACE INTO Baselines (Scope, Key, Snapshot, CapturedUtc) VALUES (@s, @k, '1', @t)", ("@s", Scope), ("@k", InitializedKey), ("@t", now));
            }).Wait(TimeSpan.FromSeconds(30));
            AppLog.Info(Module, "Önerilen servisler izlemeye alındı: " + (rec.Count == 0 ? "yok" : string.Join(", ", rec.Select(s => s.Name))));
        }

        internal static bool IsRecommended(Svc s)
        {
            return RecommendedName.IsMatch(s.Name) || RecommendedDisplay.IsMatch(s.DisplayName ?? "");
        }

        // ================================================================ DB / Windows

        private List<Row> LoadWatched()
        {
            return _db.Read(c =>
            {
                var list = new List<Row>();
                using (var cmd = c.Create("SELECT Name, DisplayName, AutoRestart FROM WatchedServices ORDER BY Name"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(new Row { Name = r.GetString(0), DisplayName = r.IsDBNull(1) ? null : r.GetString(1), AutoRestart = r.GetInt64(2) != 0 });
                return list;
            });
        }

        /// <summary>Servis başına son Svc.* olayı ("09.10 12:01 durdu: ...").</summary>
        private Dictionary<string, string> LastEvents()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rows = _db.Read(c =>
            {
                var l = new List<Tuple<string, string>>();
                using (var cmd = c.Create("SELECT TimeUtc, Summary FROM EventLog WHERE EventType IN ('Svc.Stopped', 'Svc.Recovered') ORDER BY Id DESC LIMIT 500"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) l.Add(Tuple.Create(r.GetString(0), r.GetString(1)));
                return l;
            });
            foreach (var r in rows)
            {
                var m = Regex.Match(r.Item2, @"\(([^()]+)\)$");
                if (!m.Success || d.ContainsKey(m.Groups[1].Value)) continue;
                d[m.Groups[1].Value] = DbTime.FromDb(r.Item1).ToLocalTime().ToString("dd.MM HH:mm") + " " + r.Item2.Substring(0, r.Item2.IndexOf(':') < 0 ? r.Item2.Length : r.Item2.IndexOf(':'));
            }
            return d;
        }

        private Dictionary<string, string> LoadState()
        {
            string json = _db.Read(c => c.Scalar<string>("SELECT Snapshot FROM Baselines WHERE Scope = @s AND Key = @k", ("@s", Scope), ("@k", StateKey)));
            var d = json == null ? null : PipeProtocol.FromJson<Dictionary<string, string>>(json);
            return d == null ? null : new Dictionary<string, string>(d, StringComparer.OrdinalIgnoreCase);
        }

        private void SaveState(Dictionary<string, string> state)
        {
            string json = PipeProtocol.ToJson(new Dictionary<string, string>(state)), now = DbTime.Now();
            _db.Writer.Enqueue(c => c.Execute("INSERT OR REPLACE INTO Baselines (Scope, Key, Snapshot, CapturedUtc) VALUES (@s, @k, @j, @t)",
                ("@s", Scope), ("@k", StateKey), ("@j", json), ("@t", now)));
        }

        internal static List<Svc> ReadServices()
        {
            var list = new List<Svc>();
            foreach (var sc in ServiceController.GetServices())
                using (sc)
                {
                    try
                    {
                        list.Add(new Svc { Name = sc.ServiceName, DisplayName = sc.DisplayName, Status = StatusText(sc.Status), StartType = StartTypeText(sc.StartType) });
                    }
                    catch (Exception)
                    {
                        // okunamayan servis atlanır
                    }
                }
            return list;
        }

        private static bool StartService(string name)
        {
            using (var sc = new ServiceController(name))
            {
                if (sc.Status == ServiceControllerStatus.Running) return true;
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
                return true;
            }
        }

        private static string StatusText(ServiceControllerStatus s)
        {
            switch (s)
            {
                case ServiceControllerStatus.Running: return "Çalışıyor";
                case ServiceControllerStatus.Stopped: return "Durdu";
                case ServiceControllerStatus.Paused: return "Duraklatıldı";
                case ServiceControllerStatus.StartPending: return "Başlatılıyor";
                case ServiceControllerStatus.StopPending: return "Durduruluyor";
                default: return s.ToString();
            }
        }

        private static string StartTypeText(ServiceStartMode m)
        {
            switch (m)
            {
                case ServiceStartMode.Automatic: return "Otomatik";
                case ServiceStartMode.Manual: return "Elle";
                case ServiceStartMode.Disabled: return "Devre dışı";
                default: return m.ToString();
            }
        }
    }
}
