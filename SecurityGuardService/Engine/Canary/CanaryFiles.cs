using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Checks.Performance;
using SecurityGuardService.Checks.Security;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Engine.Events;

namespace SecurityGuardService.Engine.Canary
{
    /// <summary>
    /// Tuzak dosyalar (fidye yazılımı erken uyarısı).
    /// - Paylaşılan klasörler (C$ gibi yönetim paylaşımları hariç) ve SQL yedek klasörü kendiliğinden bulunur; elle klasör de eklenir.
    /// - Her klasörün en üstüne 3 gizli dosya konur. Adları alfabede başa / ortaya / sona düşer (fidye yazılımları klasörü sırayla şifreler).
    ///   Müşterinin kendi dosyalarına dokunulmaz; sadece bizim oluşturduğumuz (DB'de kayıtlı) dosyalar yönetilir.
    /// - Dosyaya yazılır, silinir veya adı değişirse (klasör izleyici: anında; 5 dakikalık kontrol: kaçanlar) "Win.CanaryTouched"
    ///   kritik olayı: dokunan kullanıcı / program (Windows "Dosya sistemi" denetimi açıksa 4663'ten), paylaşıma bağlı bilgisayarlar,
    ///   RDP oturumları. Sadece mail atılır, müdahale edilmez. Dokunulan dosya yeniden oluşturulur.
    /// - Okuma (antivirüs, yedek programı, arama) alarm değildir; içerik özeti (SHA-256) değişmeyen "değişti" bildirimi yok sayılır.
    /// </summary>
    internal sealed class CanaryFiles : IDisposable
    {
        private const string Module = "TuzakDosya";
        public static readonly Guid FileSystemAuditGuid = new Guid("0cce921d-69ae-11d9-bed3-505054503030");
        public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan BatchDelay = TimeSpan.FromSeconds(4); // art arda dokunuşlar + 4663'ün loga düşmesi
        /// <summary>İki bildirim arası en az süre (arada gelen dokunuşlar sonraki bildirimde toplanır). Test için değiştirilebilir.</summary>
        internal TimeSpan MinReportGap = TimeSpan.FromSeconds(60);

        public const string SourceShare = "share";
        public const string SourceBackup = "backup";
        public const string SourceUser = "user";

        private readonly GuardDatabase _db;
        private readonly EventRecorder _events;
        private readonly Func<string> _backupFolder;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _lock = new object();
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Touch> _pending = new List<Touch>();
        private HashSet<string> _paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Timer _timer;
        private Timer _batchTimer;
        private DateTime _lastReportUtc = DateTime.MinValue;
        private DateTime _testUntilUtc = DateTime.MinValue;
        private string _lastCheckUtc;
        private string _message = "Henüz kontrol edilmedi.";
        private bool _disposed;

        /// <summary>Paylaşım listesi (test için değiştirilebilir).</summary>
        internal Func<List<CanaryNative.Share>> ShareSource = CanaryNative.DiskShares;

        /// <summary>Denetim işareti koyma (test için değiştirilebilir; yönetici değilse hata verir, izleme yine çalışır).</summary>
        internal Action<string> AuditSetter = CanaryNative.SetAudit;

        public CanaryFiles(GuardDatabase db, EventRecorder events, Func<string> backupFolder)
        {
            _db = db;
            _events = events;
            _backupFolder = backupFolder;
        }

        internal sealed class Touch
        {
            public string Path;
            public string Kind;
            public string NewPath;
            public DateTime TimeUtc;
            public bool AtCheck;
        }

        private sealed class FolderRow
        {
            public long Id;
            public string Path, Source, SourceName;
            public bool Enabled;
        }

        private sealed class FileRow
        {
            public long Id, FolderId;
            public string Path, Sha256, CreatedUtc;
        }

        public void Start()
        {
            _timer = new Timer(_ => RunCheck("periyodik kontrol"), null, TimeSpan.FromSeconds(10), CheckInterval);
        }

        public void Dispose()
        {
            _disposed = true;
            var t = _timer;
            _timer = null;
            if (t != null) t.Dispose();
            lock (_lock)
            {
                if (_batchTimer != null) _batchTimer.Dispose();
                _batchTimer = null;
                foreach (var w in _watchers.Values) w.Dispose();
                _watchers.Clear();
            }
        }

        private void RunCheck(string trigger)
        {
            try
            {
                SyncAsync(trigger).Wait();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Tuzak dosya kontrolü başarısız.", ex);
            }
        }

        // ================================================================ Arayüz komutları

        public CanaryStateDto GetState()
        {
            var state = new CanaryStateDto
            {
                LastCheckUtc = _lastCheckUtc,
                Message = _message,
                AuditCommand = "auditpol /set /subcategory:\"{" + FileSystemAuditGuid.ToString().ToUpperInvariant() + "}\" /success:enable /failure:enable"
            };
            try
            {
                uint flags;
                state.FileAuditEnabled = SecurityNative.AuditPolicy(new[] { FileSystemAuditGuid }).TryGetValue(FileSystemAuditGuid, out flags)
                                         && (flags & SecurityNative.AuditSuccess) != 0;
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Denetim ayarı okunamadı.", ex);
            }

            var last = _db.Read(c =>
            {
                using (var cmd = c.Create("SELECT TimeUtc, Summary FROM EventLog WHERE EventType = 'Win.CanaryTouched' ORDER BY Id DESC LIMIT 1"))
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? new[] { r.GetString(0), r.GetString(1) } : null;
            });
            if (last != null)
            {
                state.LastIncidentUtc = last[0];
                state.LastIncident = last[1];
            }

            var files = LoadFiles();
            foreach (var f in LoadFolders())
            {
                var dto = new CanaryFolderDto
                {
                    Id = f.Id,
                    Path = f.Path,
                    Enabled = f.Enabled,
                    CanRemove = f.Source == SourceUser,
                    Source = SourceText(f)
                };
                int ok = 0, bad = 0;
                foreach (var file in files.Where(x => x.FolderId == f.Id))
                {
                    string s = FileState(file);
                    if (s == "Yerinde") ok++; else bad++;
                    dto.Files.Add(new CanaryFileDto { Name = System.IO.Path.GetFileName(file.Path), Path = file.Path, State = s, CreatedUtc = file.CreatedUtc });
                }
                if (!f.Enabled) dto.State = "Kapalı";
                else if (!Directory.Exists(f.Path)) dto.State = "Klasör bulunamadı";
                else if (ok + bad == 0) dto.State = "Dosyalar henüz konmadı";
                else if (bad == 0) dto.State = "İzleniyor (" + ok + " dosya yerinde)";
                else dto.State = bad + " dosya eksik / değişmiş (yeniden oluşturuluyor)";
                state.Folders.Add(dto);
            }
            return state;
        }

        public async Task AddFolderAsync(string path, string userName)
        {
            path = (path ?? "").Trim();
            if (path.Length == 0) throw Invalid("Klasör seçin.");
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
                throw Invalid("Ağ yolu eklenemez: tuzak dosya o klasörün bulunduğu sunucuda izlenmeli (SecurityGuard o sunucuya kurulmalı).");
            try
            {
                path = System.IO.Path.GetFullPath(path).TrimEnd('\\');
                if (path.Length == 2 && path[1] == ':') path += "\\";
            }
            catch (Exception)
            {
                throw Invalid("Geçersiz klasör yolu: " + path);
            }
            if (!Directory.Exists(path)) throw Invalid("Klasör bulunamadı: " + path);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) throw Invalid("Windows klasörüne tuzak dosya konmaz.");
            if (LoadFolders().Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                throw Invalid("Bu klasör zaten listede.");

            await _db.Writer.EnqueueAsync(c => c.Execute("INSERT INTO CanaryFolders (Path, Source, Enabled, CreatedUtc) VALUES (@p, @s, 1, @t)",
                ("@p", path), ("@s", SourceUser), ("@t", DbTime.Now()))).ConfigureAwait(false);
            AuditTrail.Record(_db, userName, "Tuzak dosya klasörü eklendi", path);
            await SyncAsync("klasör eklendi").ConfigureAwait(false);
        }

        public async Task RemoveFolderAsync(long id, string userName)
        {
            var folder = LoadFolders().FirstOrDefault(f => f.Id == id);
            if (folder == null) return;
            if (folder.Source != SourceUser) throw Invalid("Paylaşım ve yedek klasörü listeden silinemez, kapatılabilir.");
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                RemoveFiles(folder.Id);
                await _db.Writer.EnqueueAsync(c => c.Execute("DELETE FROM CanaryFolders WHERE Id = @id", ("@id", id))).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
            AuditTrail.Record(_db, userName, "Tuzak dosya klasörü kaldırıldı", folder.Path);
            await SyncAsync("klasör kaldırıldı").ConfigureAwait(false);
        }

        public async Task SetEnabledAsync(long id, bool enabled, string userName)
        {
            var folder = LoadFolders().FirstOrDefault(f => f.Id == id);
            if (folder == null) throw Invalid("Klasör bulunamadı.");
            await _db.Writer.EnqueueAsync(c => c.Execute("UPDATE CanaryFolders SET Enabled = @e WHERE Id = @id", ("@e", enabled ? 1 : 0), ("@id", id))).ConfigureAwait(false);
            AuditTrail.Record(_db, userName, enabled ? "Tuzak dosya klasörü açıldı" : "Tuzak dosya klasörü kapatıldı", folder.Path);
            await SyncAsync(enabled ? "klasör açıldı" : "klasör kapatıldı").ConfigureAwait(false);
        }

        /// <summary>Bir tuzak dosyaya kendisi yazar: izleyici → olay → mail zinciri denenir.</summary>
        public async Task<string> TestAsync(string userName)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            string target;
            try
            {
                var enabled = new HashSet<long>(LoadFolders().Where(f => f.Enabled).Select(f => f.Id));
                var file = LoadFiles().FirstOrDefault(f => enabled.Contains(f.FolderId) && File.Exists(f.Path));
                if (file == null) throw Invalid("Test için yerinde duran bir tuzak dosya yok. Önce bir klasör ekleyin / açın.");
                target = file.Path;
                lock (_lock) _testUntilUtc = DateTime.UtcNow.AddSeconds(30);
                File.AppendAllText(target, "SecurityGuard test " + DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
            }
            finally
            {
                _gate.Release();
            }
            AuditTrail.Record(_db, userName, "Tuzak dosya testi", target);
            return "Test için şu tuzak dosyaya yazıldı:\r\n" + target + "\r\n\r\nBirkaç saniye içinde Olay Logu'na \"TEST: Tuzak dosyaya dokunuldu\" düşmeli ve kritik mail gitmeli. Dosya yeniden oluşturulur.";
        }

        // ================================================================ Kontrol

        /// <summary>Klasörleri bulur, eksik dosyaları koyar, değişenleri bildirir, izleyicileri günceller.</summary>
        public async Task SyncAsync(string trigger)
        {
            if (_disposed) return;
            await _gate.WaitAsync().ConfigureAwait(false);
            var found = new List<Touch>();
            try
            {
                await DiscoverAsync().ConfigureAwait(false);
                var folders = LoadFolders();
                var files = LoadFiles();
                int watching = 0, planted = 0;
                var problems = new List<string>();

                foreach (var folder in folders)
                {
                    var own = files.Where(f => f.FolderId == folder.Id).ToList();
                    if (!folder.Enabled)
                    {
                        if (own.Count > 0) RemoveFiles(folder.Id);
                        continue;
                    }
                    if (!Directory.Exists(folder.Path)) continue;

                    if (own.Count == 0)
                    {
                        foreach (var name in NamesFor(folder))
                        {
                            string path = System.IO.Path.Combine(folder.Path, name);
                            if (File.Exists(path)) continue; // aynı adlı gerçek bir dosya: dokunulmaz
                            try
                            {
                                PlantNew(folder.Id, path);
                                planted++;
                            }
                            catch (Exception ex)
                            {
                                problems.Add(path + ": " + ex.Message);
                                AppLog.Warn(Module, "Tuzak dosya konamadı: " + path, ex);
                            }
                        }
                    }
                    else
                    {
                        foreach (var f in own)
                        {
                            string s = FileState(f);
                            if (s == "Yerinde") continue;
                            found.Add(new Touch { Path = f.Path, Kind = s == "Eksik" ? "silinmiş / adı değiştirilmiş" : "içeriği değiştirilmiş", TimeUtc = DateTime.UtcNow, AtCheck = true });
                        }
                    }
                    watching++;
                }

                UpdateWatchers(folders.Where(f => f.Enabled && Directory.Exists(f.Path)).Select(f => f.Path));
                lock (_lock) _paths = new HashSet<string>(LoadFiles().Where(f => folders.Any(d => d.Id == f.FolderId && d.Enabled)).Select(f => f.Path), StringComparer.OrdinalIgnoreCase);

                _lastCheckUtc = DbTime.Now();
                _message = watching + " klasör izleniyor" + (planted > 0 ? ", " + planted + " tuzak dosya kondu" : "")
                           + (problems.Count > 0 ? ". Konamayan: " + string.Join("; ", problems.Take(3)) : ".");
                if (planted > 0) AppLog.Info(Module, _message + " (" + trigger + ")");
            }
            finally
            {
                _gate.Release();
            }

            if (found.Count > 0)
            {
                lock (_lock) _pending.AddRange(found);
                ScheduleBatch(TimeSpan.Zero);
            }
        }

        /// <summary>Paylaşımları ve yedek klasörünü DB'deki listeyle eşitler (elle eklenenlere dokunmaz).</summary>
        private async Task DiscoverAsync()
        {
            var wanted = new List<FolderRow>();
            try
            {
                foreach (var s in ShareSource())
                    if (Directory.Exists(s.Path)) wanted.Add(new FolderRow { Path = Normalize(s.Path), Source = SourceShare, SourceName = s.Name });
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Paylaşımlar okunamadı; mevcut liste korunuyor.", ex);
                return;
            }
            try
            {
                string backup = _backupFolder == null ? null : _backupFolder();
                if (!string.IsNullOrWhiteSpace(backup) && Directory.Exists(backup))
                    wanted.Add(new FolderRow { Path = Normalize(backup), Source = SourceBackup });
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Yedek klasörü okunamadı.", ex);
            }
            wanted = wanted.GroupBy(w => w.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

            var existing = LoadFolders();
            foreach (var w in wanted.Where(w => !existing.Any(e => string.Equals(e.Path, w.Path, StringComparison.OrdinalIgnoreCase))))
            {
                var row = w;
                await _db.Writer.EnqueueAsync(c => c.Execute("INSERT INTO CanaryFolders (Path, Source, SourceName, Enabled, CreatedUtc) VALUES (@p, @s, @n, 1, @t)",
                    ("@p", row.Path), ("@s", row.Source), ("@n", row.SourceName), ("@t", DbTime.Now()))).ConfigureAwait(false);
                AppLog.Info(Module, "Tuzak dosya klasörü eklendi (" + SourceText(row) + "): " + row.Path);
            }
            foreach (var e in existing.Where(e => e.Source != SourceUser))
            {
                var match = wanted.FirstOrDefault(w => string.Equals(w.Path, e.Path, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    // Paylaşım kaldırıldı / yedek klasörü değişti: bizim dosyalarımız temizlenir
                    RemoveFiles(e.Id);
                    var id = e.Id;
                    await _db.Writer.EnqueueAsync(c => c.Execute("DELETE FROM CanaryFolders WHERE Id = @id", ("@id", id))).ConfigureAwait(false);
                    AppLog.Info(Module, "Tuzak dosya klasörü kaldırıldı (artık " + SourceText(e).ToLowerInvariant() + " değil): " + e.Path);
                }
                else if (match.SourceName != e.SourceName || match.Source != e.Source)
                {
                    var id = e.Id;
                    await _db.Writer.EnqueueAsync(c => c.Execute("UPDATE CanaryFolders SET Source = @s, SourceName = @n WHERE Id = @id",
                        ("@s", match.Source), ("@n", match.SourceName), ("@id", id))).ConfigureAwait(false);
                }
            }
        }

        private static string Normalize(string path)
        {
            string p = System.IO.Path.GetFullPath(path).TrimEnd('\\');
            return p.Length == 2 && p[1] == ':' ? p + "\\" : p;
        }

        // ================================================================ İzleyici

        private void UpdateWatchers(IEnumerable<string> folders)
        {
            var wanted = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                foreach (var path in _watchers.Keys.Where(k => !wanted.Contains(k)).ToList())
                {
                    _watchers[path].Dispose();
                    _watchers.Remove(path);
                }
                foreach (var path in wanted.Where(p => !_watchers.ContainsKey(p)))
                {
                    try
                    {
                        var w = new FileSystemWatcher(path)
                        {
                            IncludeSubdirectories = false,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                            InternalBufferSize = 64 * 1024
                        };
                        w.Changed += (s, e) => OnFileEvent(e.FullPath, "içeriği değiştirildi", null);
                        w.Deleted += (s, e) => OnFileEvent(e.FullPath, "silindi", null);
                        w.Renamed += (s, e) =>
                        {
                            OnFileEvent(e.OldFullPath, "adı değiştirildi", e.FullPath);
                            OnFileEvent(e.FullPath, "üzerine başka dosya taşındı", null);
                        };
                        w.Error += (s, e) =>
                        {
                            AppLog.Warn(Module, "Klasör izleyici hata verdi, yeniden kontrol ediliyor: " + path, e.GetException());
                            lock (_lock)
                            {
                                FileSystemWatcher broken;
                                if (_watchers.TryGetValue(path, out broken))
                                {
                                    broken.Dispose();
                                    _watchers.Remove(path);
                                }
                            }
                            Task.Run(() => RunCheck("izleyici hatası"));
                        };
                        w.EnableRaisingEvents = true;
                        _watchers[path] = w;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn(Module, "Klasör izlenemiyor (5 dakikalık kontrol yine yapılır): " + path, ex);
                    }
                }
            }
        }

        internal void OnFileEvent(string path, string kind, string newPath)
        {
            lock (_lock)
            {
                // Kendi yazmalarımız da buraya düşer: hepsi kilit altında yapılıp DB'deki özet güncellendiği için
                // bildirim hazırlanırken içerik "yerinde" çıkar ve yok sayılır (zamanla susturma yok: fidye yeni koyduğumuz dosyaya dokunursa da yakalanır)
                if (!_paths.Contains(path)) return;
                _pending.Add(new Touch { Path = path, Kind = kind, NewPath = newPath, TimeUtc = DateTime.UtcNow });
            }
            ScheduleBatch(BatchDelay);
        }

        private void ScheduleBatch(TimeSpan delay)
        {
            lock (_lock)
            {
                if (_batchTimer != null || _disposed) return; // zaten planlı: yeni dokunuşlar aynı bildirime girer
                var gap = _lastReportUtc + MinReportGap - DateTime.UtcNow;
                if (gap > delay) delay = gap;
                _batchTimer = new Timer(_ => ProcessBatch(), null, delay, Timeout.InfiniteTimeSpan);
            }
        }

        private void ProcessBatch()
        {
            List<Touch> touches;
            bool test;
            lock (_lock)
            {
                if (_batchTimer != null) _batchTimer.Dispose();
                _batchTimer = null;
                touches = _pending.ToList();
                _pending.Clear();
                test = _testUntilUtc > DateTime.UtcNow;
            }
            try
            {
                ReportAsync(touches, test).Wait();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Tuzak dosya bildirimi hazırlanamadı.", ex);
            }
        }

        /// <summary>Dokunuşları doğrular (içerik aynıysa yok sayar), olayı yazar ve dosyaları yeniden oluşturur.</summary>
        internal async Task ReportAsync(List<Touch> touches, bool test)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var files = LoadFiles();
                var real = new List<Touch>();
                foreach (var g in touches.GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase))
                {
                    var row = files.FirstOrDefault(f => string.Equals(f.Path, g.Key, StringComparison.OrdinalIgnoreCase));
                    if (row == null) continue; // bu arada kaldırıldı
                    if (FileState(row) == "Yerinde") continue; // içerik aynı: okuma / öznitelik değişikliği / aynısını geri yazma
                    var first = g.First();
                    var rename = g.FirstOrDefault(t => t.NewPath != null);
                    real.Add(rename ?? first);
                }
                if (real.Count == 0) return;

                lock (_lock) _lastReportUtc = DateTime.UtcNow;
                var ev = BuildEvent(real, test);
                _events.Record(ev);
                AppLog.Warn(Module, ev.Summary);

                foreach (var t in real)
                {
                    var row = files.First(f => string.Equals(f.Path, t.Path, StringComparison.OrdinalIgnoreCase));
                    try
                    {
                        Replant(row);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn(Module, "Tuzak dosya yeniden oluşturulamadı: " + row.Path, ex);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private GuardEvent BuildEvent(List<Touch> touches, bool test)
        {
            string summary = (test ? "TEST: " : "") + "Tuzak dosyaya dokunuldu (" + touches.Count + " dosya: "
                             + string.Join(", ", touches.Select(t => t.Kind).Distinct()) + ")"
                             + (test ? "" : ". Fidye yazılımı şifrelemeye başlamış olabilir!");
            var ev = new GuardEvent("Win.CanaryTouched", summary) { Source = "Tuzak dosyalar" };

            foreach (var t in touches.Take(20))
                ev.With("Dosya", t.Path + " → " + t.Kind + (t.NewPath != null ? " (yeni adı: " + System.IO.Path.GetFileName(t.NewPath) + ")" : "")
                                 + (t.AtCheck ? " (periyodik kontrolde fark edildi)" : ""));
            if (touches.Count > 20) ev.With("Dosya", "... ve " + (touches.Count - 20) + " dosya daha");

            // Kim / hangi program (Windows "Dosya sistemi" denetimi açıksa)
            bool auditOn = false;
            try
            {
                uint flags;
                auditOn = SecurityNative.AuditPolicy(new[] { FileSystemAuditGuid }).TryGetValue(FileSystemAuditGuid, out flags) && (flags & SecurityNative.AuditSuccess) != 0;
                if (auditOn)
                {
                    // Kendi yeniden oluşturma yazmalarımız ve önceki bildirimlerden kalan erişimler hariç:
                    // sadece ilk dokunuştan biraz önce başlayan kayıtlar (periyodik kontrolde bulunanlar için 6 dk geriye)
                    string self = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    DateTime from = touches.Min(t => t.TimeUtc) - (touches.Any(t => t.AtCheck) ? CheckInterval + TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(15));
                    var accesses = CanaryNative.RecentAccesses(touches.Select(t => t.Path).ToList(), DateTime.UtcNow - from + TimeSpan.FromSeconds(5))
                        .Where(a => a.TimeUtc >= from && !string.Equals(a.Process, self, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var g in accesses.GroupBy(a => a.User + "|" + a.Process).Take(10))
                    {
                        var a = g.First();
                        bool network = a.Process != null && (a.Process.Equals("System", StringComparison.OrdinalIgnoreCase) || a.Process.EndsWith(@"\System", StringComparison.OrdinalIgnoreCase) || a.Process == "-");
                        ev.With("Kim / hangi program", a.User + " · " + (network ? "ağ paylaşımından (başka bir bilgisayardan)" : a.Process) + " · " + a.Accesses);
                        if (ev.UserName == null) ev.UserName = a.User;
                    }
                    if (accesses.Count == 0) ev.With("Kim / hangi program", "Security logunda kayıt bulunamadı (log dolmuş / silinmiş olabilir).");
                }
                else
                    ev.With("Kim / hangi program", "Bilinmiyor: Windows \"Dosya sistemi\" denetimi kapalı (SecurityGuard → Tuzak Dosyalar sayfasındaki komutla açılabilir).");
            }
            catch (Exception ex)
            {
                ev.With("Kim / hangi program", "Okunamadı: " + ex.Message);
            }

            // Paylaşıma bağlı bilgisayarlar (ağdan şifreleme en sık senaryo)
            try
            {
                var smb = CanaryNative.SmbSessions().OrderBy(s => s.IdleSeconds).ToList();
                foreach (var s in smb.Take(10))
                    ev.With("Paylaşıma bağlı bilgisayar", s.Client + " (" + s.User + ", son işlem " + Ago(s.IdleSeconds) + ")");
                if (smb.Count == 0) ev.With("Paylaşıma bağlı bilgisayar", "Yok");
                var recent = smb.Where(s => s.IdleSeconds < 120).ToList();
                IPAddress ip;
                if (recent.Count == 1 && IPAddress.TryParse(recent[0].Client, out ip)) ev.IpAddress = ip.ToString();
            }
            catch (Exception ex)
            {
                ev.With("Paylaşıma bağlı bilgisayar", "Okunamadı: " + ex.Message);
            }

            try
            {
                var rdp = WindowsNative.Sessions();
                foreach (var s in rdp)
                    ev.With("Sunucudaki oturum", s.User + (s.ClientAddress != null ? " (" + s.ClientAddress + ")" : " (konsol)") + (s.Disconnected ? ", bağlantısı kopuk" : ""));
            }
            catch (Exception ex)
            {
                ev.With("Sunucudaki oturum", "Okunamadı: " + ex.Message);
            }

            ev.With("Ne yapmalı", test
                ? "Bu bir testtir (SecurityGuard arayüzünden başlatıldı). Gerçek bir dokunuşta bu mail aynı şekilde gelir."
                : "1) Paylaşıma bağlı ve son işlemi yeni olan bilgisayarı HEMEN ağdan ayırın (kablo / Wi-Fi). "
                  + "2) Sunucuda şüpheli oturum varsa kapatın, gerekirse sunucunun ağ bağlantısını kesin. "
                  + "3) Paylaşılan klasörlerde uzantısı değişmiş / açılmayan dosya var mı bakın. 4) Yedeklerin sağlam olduğunu kontrol edin.");
            ev.With("Not", "Dokunulan tuzak dosya yeniden oluşturuldu.");
            return ev;
        }

        private static string Ago(int seconds)
        {
            if (seconds < 60) return seconds + " sn önce";
            if (seconds < 3600) return seconds / 60 + " dk önce";
            return seconds / 3600 + " saat önce";
        }

        // ================================================================ Dosyalar

        private static IEnumerable<string> NamesFor(FolderRow folder)
        {
            string year = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);
            // Başa (!!), ortaya, sona (~~) düşen adlar: alfabe sırasıyla ilerleyen şifreleme ilk / son dosyada yakalanır
            if (folder.Source == SourceBackup)
                return new[] { "!!_LOGO_Yedek_FULL_" + year + ".bak", "Muhasebe_Kapanis_" + year + ".xlsx", "~~_Arsiv_Yedek_" + year + ".rar" };
            return new[] { "!!_Muhasebe_Kapanis_" + year + ".xlsx", "Fatura_Arsiv_Listesi.docx", "~~_Personel_Bordro_" + year + ".pdf" };
        }

        private void PlantNew(long folderId, string path)
        {
            string hash = WriteCanary(path);
            string now = DbTime.Now();
            _db.Writer.EnqueueAsync(c => c.Execute("INSERT INTO CanaryFiles (FolderId, Path, Sha256, CreatedUtc) VALUES (@f, @p, @h, @t)",
                ("@f", folderId), ("@p", path), ("@h", hash), ("@t", now))).Wait();
        }

        private void Replant(FileRow row)
        {
            string hash = WriteCanary(row.Path);
            string now = DbTime.Now();
            _db.Writer.EnqueueAsync(c => c.Execute("UPDATE CanaryFiles SET Sha256 = @h, CreatedUtc = @t WHERE Id = @id",
                ("@h", hash), ("@t", now), ("@id", row.Id))).Wait();
        }

        /// <summary>Gerçekçi başlıklı rastgele içerik yazar, gizler, denetim işareti koyar. İçerik özetini döner.</summary>
        private string WriteCanary(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            byte[] header = ext == ".pdf" ? System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n%\xE2\xE3\xCF\xD3\n")
                          : ext == ".bak" ? System.Text.Encoding.ASCII.GetBytes("TAPE")
                          : ext == ".rar" ? new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 }
                          : new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00 };
            byte[] data;
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] size = new byte[2];
                rng.GetBytes(size);
                data = new byte[24 * 1024 + (BitConverter.ToUInt16(size, 0) % (40 * 1024))];
                rng.GetBytes(data);
            }
            Array.Copy(header, data, header.Length);

            if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
            File.WriteAllBytes(path, data);
            File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.NotContentIndexed);
            try
            {
                AuditSetter(path);
            }
            catch (Exception ex)
            {
                AppLog.Debug(Module, "Denetim işareti konamadı (izleme yine çalışır): " + path + " - " + ex.Message);
            }
            return Hash(data);
        }

        private void RemoveFiles(long folderId)
        {
            foreach (var f in LoadFiles().Where(x => x.FolderId == folderId))
            {
                try
                {
                    if (File.Exists(f.Path))
                    {
                        File.SetAttributes(f.Path, FileAttributes.Normal);
                        File.Delete(f.Path);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn(Module, "Tuzak dosya silinemedi: " + f.Path, ex);
                }
            }
            _db.Writer.EnqueueAsync(c => c.Execute("DELETE FROM CanaryFiles WHERE FolderId = @f", ("@f", folderId))).Wait();
            lock (_lock) _paths.RemoveWhere(p => !File.Exists(p));
        }

        private static string FileState(FileRow f)
        {
            try
            {
                if (!File.Exists(f.Path)) return "Eksik";
                return Hash(File.ReadAllBytes(f.Path)) == f.Sha256 ? "Yerinde" : "Değişmiş";
            }
            catch (IOException)
            {
                return "Değişmiş"; // kilitli: biri yazıyor
            }
            catch (UnauthorizedAccessException)
            {
                return "Değişmiş"; // izinleri değiştirilmiş
            }
        }

        private static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        }

        // ================================================================ DB

        private List<FolderRow> LoadFolders()
        {
            return _db.Read(c =>
            {
                var list = new List<FolderRow>();
                using (var cmd = c.Create("SELECT Id, Path, Source, SourceName, Enabled FROM CanaryFolders ORDER BY Path"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new FolderRow
                        {
                            Id = r.GetInt64(0),
                            Path = r.GetString(1),
                            Source = r.GetString(2),
                            SourceName = r.IsDBNull(3) ? null : r.GetString(3),
                            Enabled = r.GetInt64(4) != 0
                        });
                return list;
            });
        }

        private List<FileRow> LoadFiles()
        {
            return _db.Read(c =>
            {
                var list = new List<FileRow>();
                using (var cmd = c.Create("SELECT Id, FolderId, Path, Sha256, CreatedUtc FROM CanaryFiles ORDER BY Path"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new FileRow { Id = r.GetInt64(0), FolderId = r.GetInt64(1), Path = r.GetString(2), Sha256 = r.GetString(3), CreatedUtc = r.GetString(4) });
                return list;
            });
        }

        private static string SourceText(FolderRow f)
        {
            if (f.Source == SourceShare) return "Paylaşım: " + f.SourceName;
            if (f.Source == SourceBackup) return "SQL yedek klasörü";
            return "Elle eklendi";
        }

        private static GuardException Invalid(string message)
        {
            return new GuardException(ErrorCodes.Validation, message);
        }
    }
}
