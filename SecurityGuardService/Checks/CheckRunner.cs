using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Events;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Checks
{
    /// <summary>
    /// Taramaları (Performans / Güvenlik Önerileri) çalıştırır: her gün rapor saatinde otomatik, UI'den elle.
    /// - Tarama sadece OKUR, sunucuda hiçbir şeyi değiştirmez.
    /// - Aynı türden aynı anda tek tarama; düşük öncelikli ayrı iş parçacığında, güvenlik izlemeyi etkilemez.
    /// - Servis o saatte kapalıysa, aynı gün açılınca kaçırılan tarama yapılır.
    /// - Yeni çıkan (bir önceki taramada olmayan) kritik bulgu olay olarak kaydedilir (kurala göre mail).
    /// </summary>
    internal sealed class CheckRunner : IDisposable
    {
        private const string Module = "Tarama";
        private const string LastScheduledKeyPrefix = "checks.lastScheduledDate:";
        public const string NewCriticalEvent = "Check.NewCritical";

        private readonly GuardDatabase _db;
        private readonly SettingsStore _settings;
        private readonly EventRecorder _events;
        private readonly CheckStore _store;
        private readonly Dictionary<string, Scan> _scans = new Dictionary<string, Scan>(StringComparer.Ordinal);
        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Thread _scheduler;

        public CheckRunner(GuardDatabase db, SettingsStore settings, EventRecorder events)
        {
            _db = db;
            _settings = settings;
            _events = events;
            _store = new CheckStore(db);
        }

        /// <summary>Tarama türünü tanımlar (ad: UI ve maillerde, run: maddeleri context'e ekler).</summary>
        public void Register(string scope, string displayName, Action<CheckContext> run)
        {
            _scans[scope] = new Scan { Scope = scope, Name = displayName, RunAction = run };
        }

        public void Start()
        {
            _scheduler = new Thread(SchedulerLoop) { IsBackground = true, Name = "CheckScheduler" };
            _scheduler.Start();
        }

        // ================================================================ UI

        public CheckRunDto Latest(string scope)
        {
            var scan = Get(scope);
            var dto = _store.LoadLatest(scope);
            lock (_lock)
            {
                dto.IsRunning = scan.Running;
                dto.Progress = scan.Running ? scan.Progress : null;
            }
            dto.NextRunUtc = DbTime.ToDb(NextScheduledLocal().ToUniversalTime());
            return dto;
        }

        /// <summary>Bu türde tarama sürüyor mu (günlük rapor bitmesini bekler).</summary>
        public bool IsRunning(string scope)
        {
            var scan = Get(scope);
            lock (_lock) return scan.Running;
        }

        /// <summary>Bugünün zamanlanmış taraması yapıldı mı / başlatıldı mı.</summary>
        public bool ScheduledToday(string scope)
        {
            return _settings.Get(LastScheduledKeyPrefix + scope) == DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>Elle tarama. Sürüyorsa yenisi başlamaz (false).</summary>
        public bool RunNow(string scope, string userName)
        {
            var scan = Get(scope);
            bool started = TryStart(scan, "elle (" + userName + ")");
            if (started) AuditTrail.Record(_db, userName, scan.Name + " taraması başlatıldı");
            return started;
        }

        public async Task SetIgnoreAsync(CheckIgnoreRequest req, string userName)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.CheckId)) throw new GuardException(ErrorCodes.InvalidRequest, "Madde seçin.");
            var scan = Get(req.Scope);
            string note = (req.Note ?? "").Trim();
            if (note.Length > 500) note = note.Substring(0, 500);
            if (req.Ignore && note.Length == 0) throw new GuardException(ErrorCodes.Validation, "Neden görmezden gelindiğini kısaca yazın (ör. \"Uygulama üreticisi bu ayarı istiyor\").");

            await _store.SetIgnoreAsync(req.CheckId, req.Ignore, req.Ignore ? note : null).ConfigureAwait(false);
            AuditTrail.Record(_db, userName, req.Ignore ? scan.Name + ": madde görmezden gelindi" : scan.Name + ": görmezden gelme kaldırıldı",
                req.CheckId + (req.Ignore ? " (not: " + note + ")" : ""));
        }

        private Scan Get(string scope)
        {
            Scan scan;
            if (scope == null || !_scans.TryGetValue(scope, out scan))
                throw new GuardException(ErrorCodes.InvalidRequest, "Bilinmeyen tarama türü: " + scope);
            return scan;
        }

        // ================================================================ Çalıştırma

        private bool TryStart(Scan scan, string trigger)
        {
            lock (_lock)
            {
                if (scan.Running || _cts.IsCancellationRequested) return false;
                scan.Running = true;
                scan.Progress = "Başlıyor";
            }

            var thread = new Thread(() => Execute(scan, trigger))
            {
                IsBackground = true,
                Name = "Check-" + scan.Scope,
                Priority = ThreadPriority.BelowNormal
            };
            thread.Start();
            return true;
        }

        private void Execute(Scan scan, string trigger)
        {
            var started = DateTime.UtcNow;
            string runId = Guid.NewGuid().ToString("N");
            AppLog.Info(Module, scan.Name + " taraması başladı (" + trigger + ").");
            try
            {
                var ctx = new CheckContext(_settings, _cts.Token, p => { lock (_lock) scan.Progress = p; });
                scan.RunAction(ctx);

                var previous = _store.LoadLatestStatuses(scan.Scope);
                _store.SaveRunAsync(scan.Scope, runId, trigger, started, DateTime.UtcNow, ctx.Items).Wait();

                var items = ctx.Items;
                AppLog.Info(Module, scan.Name + " taraması bitti (" + (int)(DateTime.UtcNow - started).TotalSeconds + " sn): "
                    + items.Count(x => x.Status == CheckStatus.Kritik) + " kritik, " + items.Count(x => x.Status == CheckStatus.Uyari) + " uyarı, "
                    + items.Count(x => x.Status == CheckStatus.Bilgi) + " bilgi, " + items.Count(x => x.Status == CheckStatus.Gecti) + " geçti, "
                    + items.Count(x => x.Status == CheckStatus.Hata) + " kontrol edilemedi.");

                ReportNewCritical(scan, items, previous);
            }
            catch (OperationCanceledException)
            {
                AppLog.Info(Module, scan.Name + " taraması servis kapandığı için yarıda bırakıldı.");
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                AppLog.Info(Module, scan.Name + " taraması servis kapandığı için yarıda bırakıldı.");
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, scan.Name + " taraması başarısız.", ex);
            }
            finally
            {
                lock (_lock)
                {
                    scan.Running = false;
                    scan.Progress = null;
                }
            }
        }

        /// <summary>Yeni çıkan kritik bulgular tek olay (tek mail) olarak bildirilir; görmezden gelinenler hariç.</summary>
        private void ReportNewCritical(Scan scan, List<CheckItemDto> items, Dictionary<string, CheckStatus> previous)
        {
            var ignores = _store.LoadIgnores();
            var fresh = items.Where(x => x.Status == CheckStatus.Kritik && !ignores.ContainsKey(x.CheckId)
                                         && (previous == null || !previous.ContainsKey(x.CheckId) || previous[x.CheckId] != CheckStatus.Kritik)).ToList();
            if (fresh.Count == 0) return;

            var e = new GuardEvent(NewCriticalEvent, scan.Name + " taramasında " + fresh.Count + " yeni kritik bulgu: " + string.Join(", ", fresh.Take(3).Select(x => x.Title))
                                                     + (fresh.Count > 3 ? "..." : ""))
            {
                Source = scan.Name + " taraması"
            };
            foreach (var x in fresh.Take(20))
                e.With(x.Area + " / " + x.Title, x.Summary);
            e.With("Ayrıntı", "SecurityGuard arayüzünde " + scan.Name + " sayfasında nasıl düzeltileceği yazıyor. SecurityGuard kendisi hiçbir şeyi değiştirmez.");
            _events.Record(e);
        }

        // ================================================================ Zamanlama

        private void SchedulerLoop()
        {
            // Açılışta biraz bekle: servis açılışı ve olay toplayıcılar önce otursun
            if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(2))) return;
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var due = now.Date + GeneralSettings.GetTime(_settings, SettingKeys.ReportDailyTime);
                    string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (now >= due && LicenseGate.Active)
                    {
                        foreach (var scan in _scans.Values)
                        {
                            string key = LastScheduledKeyPrefix + scan.Scope;
                            if (_settings.Get(key) == today) continue;
                            if (!TryStart(scan, now - due > TimeSpan.FromMinutes(5) ? "zamanlanmış (kaçırılan " + due.ToString("HH:mm", CultureInfo.InvariantCulture) + " taraması)" : "zamanlanmış"))
                                continue; // elle başlatılmış tarama sürüyor: bitince tekrar denenir
                            _settings.SetAsync(key, today).Wait(TimeSpan.FromSeconds(30));
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Tarama zamanlayıcısı hata verdi.", ex);
                }
                if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1))) return;
            }
        }

        private DateTime NextScheduledLocal()
        {
            var now = DateTime.Now;
            var due = now.Date + GeneralSettings.GetTime(_settings, SettingKeys.ReportDailyTime);
            return now < due ? due : due.AddDays(1);
        }

        public void Dispose()
        {
            _cts.Cancel();
            if (_scheduler != null) _scheduler.Join(TimeSpan.FromSeconds(5));
            // Süren tarama iptal işaretini görüp kendisi çıkar (SQL komutları da iptal edilir)
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (!_scans.Values.Any(s => s.Running)) return;
                }
                Thread.Sleep(100);
            }
            AppLog.Warn(Module, "Süren tarama 10 sn içinde durmadı, bırakıldı.");
        }

        private sealed class Scan
        {
            public string Scope;
            public string Name;
            public Action<CheckContext> RunAction;
            public bool Running;
            public string Progress;
        }
    }
}
