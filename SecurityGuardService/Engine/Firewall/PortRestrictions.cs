using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Checks.Performance;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Net;
using SecurityGuardService.Engine.Events;
using SecurityGuardService.Sql;

namespace SecurityGuardService.Engine.Firewall
{
    /// <summary>
    /// Port IP kısıtları ("bu porta sadece şu IP'ler bağlansın").
    /// - Windows'un kendi firewall kurallarına dokunulmaz. Her kısıt için bizim ENGELLE kuralımız yazılır:
    ///   "SecurityGuard-Restrict-001-TCP", uzak adres = izinli IP'lerin (+ iç ağ + localhost) DIŞINDAKİ her şey.
    ///   Engelle kuralı izin kurallarından önce geldiği için başka bir izin kuralı açık olsa da kısıt geçerlidir.
    /// - Kilitlenme koruması: kayıt uygulanır ama <see cref="ConfirmWindow"/> içinde onaylanmazsa önceki hale döner
    ///   (yeni kısıt kapanır). Arayüzü açan RDP oturumunun IP'si engellenecekse kayıt hiç yapılmaz.
    /// - Nöbet: kural silinir / değiştirilirse (firewall olayı veya 5 dakikalık kontrol) DB'ye göre onarılır ve olay üretilir.
    /// Sadece kullanıcı arayüzden kaydedince firewall değişir; servis kendiliğinden yeni kısıt eklemez.
    /// </summary>
    internal sealed class PortRestrictions : IDisposable
    {
        private const string Module = "PortKısıtı";
        public const int MaxAddressLines = 500;
        public const int MaxNameLength = 100;

        /// <summary>Uygulanan değişikliğin onaylanması için süre; dolarsa geri alınır.</summary>
        public static readonly TimeSpan ConfirmWindow = TimeSpan.FromMinutes(5);

        public static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

        /// <summary>Her zaman izinli (yerel trafik).</summary>
        private static readonly string[] AlwaysAllowed = { "127.0.0.0/8", "::1" };

        /// <summary>"İç ağ her zaman izinli" seçiliyse.</summary>
        public static readonly string[] LanRanges = { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "169.254.0.0/16", "fe80::/10", "fc00::/7" };

        private readonly GuardDatabase _db;
        private readonly EventRecorder _events;
        private readonly IPortFirewall _firewall;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private Timer _timer;
        private int _ticking;
        private int _syncRequested;
        private DateTime _nextWatchdogUtc = DateTime.MinValue;
        private bool _firewallOk = true;
        private string _firewallMessage = "Kontrol edilmedi.";

        /// <summary>Aktif RDP oturumları (kullanıcı, istemci IP). Test için değiştirilebilir.</summary>
        internal Func<List<KeyValuePair<string, string>>> SessionSource = DefaultSessions;

        /// <summary>RDP portu (registry). Test için değiştirilebilir.</summary>
        internal Func<int> RdpPortSource = ReadRdpPort;

        /// <summary>Bir firewall kuralına yazılan en fazla adres / aralık (fazlası yeni kurala bölünür: ...-TCP, ...-TCP-2).</summary>
        public const int MaxPerRule = 1000;

        /// <summary>Bir kısıtın toplam engel aralığı sınırı (çok büyük ülkeler seçilirse firewall şişmesin).</summary>
        public const int MaxBlockEntries = 40000;

        private readonly GeoIpDatabase _geo;
        private DateTime _nextGeoCheckUtc = DateTime.UtcNow.AddMinutes(1);
        private int _geoChecking;

        public PortRestrictions(GuardDatabase db, EventRecorder events, IPortFirewall firewall, GeoIpDatabase geo = null)
        {
            _db = db;
            _events = events;
            _firewall = firewall;
            _geo = geo;
            // Veritabanı hiç yoksa (paket kopyası eksik) ilk kontrolde hemen indirilir; varsa açılışı yormamak için 1 dk sonra
            if (_geo != null && !_geo.Available) _nextGeoCheckUtc = DateTime.UtcNow;
            if (_geo != null) _geo.Updated += () => Task.Run(() => ReapplyAllAsync("ülke veritabanı güncellendi"));
        }

        public void Start()
        {
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(3), TickInterval);
        }

        /// <summary>
        /// Tüm kısıtları DB'ye göre yeniden yazar (ülke veritabanı değişince: aralıklar değişti, bu kurcalama değildir, olay üretilmez).
        /// </summary>
        public async Task ReapplyAllAsync(string trigger)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var row in LoadRows()) ApplyRow(row);
                AppLog.Info(Module, "Kısıtlar yeniden yazıldı (" + trigger + ").");
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Kısıtlar yeniden yazılamadı (" + trigger + ").", ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            var t = _timer;
            _timer = null;
            if (t != null) t.Dispose();
        }

        // ================================================================ Kayıt modeli

        private sealed class Row
        {
            public long Id;
            public string Name, Protocol, Ports, Addresses, Countries, Note, UpdatedUtc, UpdatedBy, PendingUntilUtc, PreviousJson;
            public bool AllowLan, Enabled;
        }

        /// <summary>Doğrulanmış kısıt: firewall'a yazılacak kurallar.</summary>
        internal sealed class Compiled
        {
            public string Ports;
            public List<Tuple<int, int>> PortRanges;
            public List<IpInterval> Allowed;
            public List<string> AllowedText = new List<string>();
            /// <summary>Engel listesi, kural başına en fazla <see cref="MaxPerRule"/> girdilik parçalar halinde.</summary>
            public List<string> BlockChunks;
            public int BlockCount;
            public List<string> Countries = new List<string>();
            public bool Tcp, Udp;
        }

        // ================================================================ Arayüz komutları

        public PortRestrictionStateDto GetState()
        {
            var rows = LoadRows();
            List<PortRule> actual = null;
            try
            {
                actual = _firewall.ListRules();
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Firewall kuralları okunamadı.", ex);
            }

            var state = new PortRestrictionStateDto
            {
                ConfirmSeconds = (int)ConfirmWindow.TotalSeconds,
                FirewallOk = _firewallOk && actual != null,
                FirewallMessage = actual == null ? "Firewall kuralları okunamadı." : _firewallMessage,
                Templates = Templates()
            };
            if (_geo != null)
            {
                string warning;
                state.GeoInfo = _geo.Describe(out warning);
                state.GeoWarning = warning;
                try
                {
                    if (_geo.Available)
                        state.Countries = _geo.Countries().Select(c => new CountryDto { Code = c, Name = GeoIpDatabase.CountryName(c) })
                            .OrderBy(c => c.Code == "TR" ? 0 : 1).ThenBy(c => c.Name, StringComparer.Create(new CultureInfo("tr-TR"), true)).ToList();
                }
                catch (Exception ex)
                {
                    state.GeoWarning = "Ülke veritabanı okunamadı: " + ex.Message;
                }
            }
            try
            {
                state.FirewallOffWarning = _firewall.FirewallOffWarning();
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Firewall profil durumu okunamadı.", ex);
            }

            foreach (var r in rows)
            {
                var dto = ToDto(r);
                var expected = Expected(r);
                dto.RuleNames = expected.Count <= 2 ? string.Join(", ", expected.Select(e => e.Name))
                    : string.Join(", ", expected.Take(2).Select(e => e.Name)) + " (+" + (expected.Count - 2) + " kural)";
                if (!r.Enabled) dto.State = "Kapalı";
                else if (actual == null) dto.State = "Firewall okunamadı";
                else if (expected.Count == 0) dto.State = "Hatalı kayıt";
                else if (expected.Any(e => !actual.Any(a => Same(a, e)))) dto.State = "Firewall'da eksik / farklı (onarılacak)";
                else dto.State = r.PendingUntilUtc != null ? "Onay bekliyor" : "Uygulandı";
                state.Items.Add(dto);
            }
            return state;
        }

        /// <summary>Kaydetmeden önce kontrol: hata, engellenecek oturumlar ve özet.</summary>
        public PortRestrictionCheckDto Check(PortRestrictionSaveRequest req)
        {
            var result = new PortRestrictionCheckDto();
            var item = req == null ? null : req.Item;
            if (item == null)
            {
                result.Error = "Kısıt bilgisi yok.";
                return result;
            }
            try
            {
                var c = Compile(item);
                if (item.Enabled)
                {
                    CheckOverlap(item, c, LoadRows());
                    CheckOwnAddress(c, req.ClientAddress);
                    result.BlockedSessions = BlockedSessions(c);
                }
                result.BlockedSummary = Summary(item, c);
            }
            catch (GuardException ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }

        /// <summary>
        /// Kaydeder. Etkinse firewall'a hemen uygulanır ve <see cref="ConfirmWindow"/> içinde onay beklenir.
        /// </summary>
        /// <returns>Kaydın Id'si.</returns>
        public async Task<long> SaveAsync(PortRestrictionSaveRequest req, string userName)
        {
            var item = req == null ? null : req.Item;
            if (item == null) throw new GuardException(ErrorCodes.Validation, "Kısıt bilgisi yok.");

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var c = Compile(item);
                var rows = LoadRows();
                Row existing = null;
                if (item.Id > 0)
                {
                    existing = rows.FirstOrDefault(r => r.Id == item.Id);
                    if (existing == null) throw new GuardException(ErrorCodes.Validation, "Kısıt bulunamadı (silinmiş olabilir).");
                }
                if (item.Enabled)
                {
                    CheckOverlap(item, c, rows);
                    CheckOwnAddress(c, req.ClientAddress);
                    var blocked = BlockedSessions(c);
                    if (blocked.Count > 0 && !req.AcceptOtherSessions)
                        throw new GuardException(ErrorCodes.Validation, "Şu aktif RDP oturumlarının bağlantısı engellenecek: " + string.Join(", ", blocked));
                }

                // Geri alma noktası: onaylanmış son hal. Zaten onay bekleyen bir kayıt yeniden düzenlenirse ilk onaylı hal korunur.
                string previous = null;
                if (item.Enabled && existing != null)
                    previous = existing.PendingUntilUtc != null ? existing.PreviousJson : PipeProtocol.ToJson(ToDto(existing));

                var row = new Row
                {
                    Id = item.Id,
                    Name = item.Name.Trim(),
                    Protocol = NormalizeProtocol(item.Protocol),
                    Ports = c.Ports,
                    Addresses = NormalizeAddressText(item.Addresses),
                    Countries = c.Countries.Count == 0 ? null : string.Join(",", c.Countries),
                    AllowLan = item.AllowLan,
                    Enabled = item.Enabled,
                    Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim(),
                    UpdatedUtc = DbTime.Now(),
                    UpdatedBy = userName,
                    PendingUntilUtc = item.Enabled ? DbTime.ToDb(DateTime.UtcNow + ConfirmWindow) : null,
                    PreviousJson = previous
                };
                row.Id = await WriteRowAsync(row).ConfigureAwait(false);

                try
                {
                    ApplyRow(row);
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Kısıt firewall'a yazılamadı: " + row.Name, ex);
                    await RevertAsync(row, null).ConfigureAwait(false);
                    throw new GuardException(ErrorCodes.InvalidRequest, "Firewall kuralı yazılamadı, değişiklik geri alındı: " + ex.Message);
                }

                AuditTrail.Record(_db, userName, item.Id > 0 ? "Port kısıtı değiştirildi" : "Port kısıtı eklendi",
                    row.Name + " (" + ProtocolText(row.Protocol) + " " + row.Ports + ")" + (row.Enabled ? ", uygulandı, onay bekleniyor" : ", kapalı")
                    + "; izinli: " + string.Join(", ", c.AllowedText));

                if (!row.Enabled && existing != null && existing.Enabled)
                    RecordChange("Port kısıtı kapatıldı: " + row.Name, row, userName, "Kapatıldı (port herkese açık; Windows'un kendi kuralları geçerli)");
                AppLog.Info(Module, "Kısıt kaydedildi: " + row.Name + (row.Enabled ? " (onay bekleniyor)" : " (kapalı)"));
                return row.Id;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task ConfirmAsync(long id, string userName)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var row = LoadRows().FirstOrDefault(r => r.Id == id);
                if (row == null) throw new GuardException(ErrorCodes.Validation, "Kısıt bulunamadı.");
                if (row.PendingUntilUtc == null)
                {
                    if (!row.Enabled) throw new GuardException(ErrorCodes.Validation, "Onay süresi dolduğu için değişiklik geri alınmış. Yeniden kaydedin.");
                    return; // zaten onaylı
                }
                await _db.Writer.EnqueueAsync(cn => cn.Execute("UPDATE PortRestrictions SET PendingUntilUtc = NULL, PreviousJson = NULL WHERE Id = @id", ("@id", id))).ConfigureAwait(false);
                AuditTrail.Record(_db, userName, "Port kısıtı onaylandı", row.Name + " (" + ProtocolText(row.Protocol) + " " + row.Ports + ")");
                RecordChange("Port kısıtı uygulandı: " + row.Name + " (" + ProtocolText(row.Protocol) + " " + row.Ports + ")", row, userName, "Uygulandı ve onaylandı");
                AppLog.Info(Module, "Kısıt onaylandı: " + row.Name);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Onay bekleyen değişikliği kullanıcı isteğiyle hemen geri alır.</summary>
        public async Task RevertNowAsync(long id, string userName)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var row = LoadRows().FirstOrDefault(r => r.Id == id);
                if (row == null || row.PendingUntilUtc == null) return; // onay bekleyen değişiklik yok (süre dolmuş olabilir)
                string name = row.Name;
                string what = await RevertAsync(row, null).ConfigureAwait(false);
                AuditTrail.Record(_db, userName, "Port kısıtı geri alındı", name + ": " + what + " (kullanıcı geri aldı)");
                AppLog.Info(Module, "Kısıt kullanıcı isteğiyle geri alındı: " + name + " → " + what);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DisableAsync(long id, string userName)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var row = LoadRows().FirstOrDefault(r => r.Id == id);
                if (row == null) throw new GuardException(ErrorCodes.Validation, "Kısıt bulunamadı.");
                bool wasEnabled = row.Enabled;
                row.Enabled = false;
                row.PendingUntilUtc = null;
                row.PreviousJson = null;
                row.UpdatedUtc = DbTime.Now();
                row.UpdatedBy = userName;
                await WriteRowAsync(row).ConfigureAwait(false);
                ApplyRow(row);
                AuditTrail.Record(_db, userName, "Port kısıtı kapatıldı", row.Name + " (" + ProtocolText(row.Protocol) + " " + row.Ports + ")");
                if (wasEnabled) RecordChange("Port kısıtı kapatıldı: " + row.Name, row, userName, "Kapatıldı (port herkese açık; Windows'un kendi kuralları geçerli)");
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DeleteAsync(long id, string userName)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var row = LoadRows().FirstOrDefault(r => r.Id == id);
                if (row == null) return;
                foreach (var name in OwnRules(id)) _firewall.DeleteRule(name);
                await _db.Writer.EnqueueAsync(cn => cn.Execute("DELETE FROM PortRestrictions WHERE Id = @id", ("@id", id))).ConfigureAwait(false);
                AuditTrail.Record(_db, userName, "Port kısıtı silindi", row.Name + " (" + ProtocolText(row.Protocol) + " " + row.Ports + ")");
                if (row.Enabled) RecordChange("Port kısıtı silindi: " + row.Name, row, userName, "Silindi (port herkese açık; Windows'un kendi kuralları geçerli)");
            }
            finally
            {
                _gate.Release();
            }
        }

        // ================================================================ Nöbet

        /// <summary>Bizim kurala dokunan firewall olayı: kısa süre sonra kontrol (art arda olaylar birleşir).</summary>
        public void RequestSync()
        {
            if (Interlocked.Exchange(ref _syncRequested, 1) == 1) return;
            Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ =>
            {
                Interlocked.Exchange(ref _syncRequested, 0);
                try
                {
                    SyncAsync("firewall olayı").Wait();
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Firewall kontrolü başarısız.", ex);
                }
            });
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            try
            {
                ExpirePendingAsync().Wait();
                if (_geo != null && DateTime.UtcNow >= _nextGeoCheckUtc && Interlocked.Exchange(ref _geoChecking, 1) == 0)
                {
                    _nextGeoCheckUtc = DateTime.UtcNow.AddHours(12);
                    Task.Run(() =>
                    {
                        try
                        {
                            _geo.TryUpdate();
                        }
                        catch (Exception ex)
                        {
                            AppLog.Warn(Module, "Ülke veritabanı indirilemedi (12 saat sonra tekrar denenecek): " + ex.Message);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _geoChecking, 0);
                        }
                    });
                }
                if (DateTime.UtcNow >= _nextWatchdogUtc)
                {
                    bool first = _nextWatchdogUtc == DateTime.MinValue;
                    _nextWatchdogUtc = DateTime.UtcNow + WatchdogInterval;
                    SyncAsync(first ? "servis açılışı" : "periyodik kontrol").Wait();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Port kısıtı kontrolü başarısız.", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _ticking, 0);
            }
        }

        /// <summary>Onay süresi dolan kayıtları geri alır.</summary>
        internal async Task ExpirePendingAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                DateTime now = DateTime.UtcNow;
                foreach (var row in LoadRows().Where(r => r.PendingUntilUtc != null && DbTime.FromDb(r.PendingUntilUtc) <= now))
                {
                    string what = await RevertAsync(row, "onay süresi doldu").ConfigureAwait(false);
                    AppLog.Warn(Module, "Kısıt onaylanmadığı için geri alındı: " + row.Name + " → " + what);
                    _events.Record(new GuardEvent("Guard.PortRestrictionReverted", "Port kısıtı " + (int)ConfirmWindow.TotalMinutes + " dakika içinde onaylanmadığı için geri alındı: " + row.Name)
                    {
                        Source = "Port Kısıtları",
                        UserName = row.UpdatedBy
                    }
                    .With("Kısıt", row.Name + " (" + ProtocolText(row.Protocol) + " " + row.Ports + ")")
                    .With("Değiştiren", row.UpdatedBy)
                    .With("Yapılan", what)
                    .With("Not", "Değişiklik yapan kişi yeni bir RDP bağlantısıyla erişimi test edip onaylamadı. Bağlantı kesildiyse bu yüzden geri alındı."));
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Kaydı onaylı son haline döndürür (yoksa kapatır) ve firewall'a uygular. Çağıran kilidi tutar.</summary>
        private async Task<string> RevertAsync(Row row, string reason)
        {
            var previous = string.IsNullOrEmpty(row.PreviousJson) ? null : PipeProtocol.FromJson<PortRestrictionDto>(row.PreviousJson);
            string what;
            if (previous == null)
            {
                row.Enabled = false;
                what = "Kısıt kapatıldı";
            }
            else
            {
                row.Name = previous.Name;
                row.Protocol = previous.Protocol;
                row.Ports = previous.Ports;
                row.Addresses = previous.Addresses;
                row.Countries = previous.Countries;
                row.AllowLan = previous.AllowLan;
                row.Enabled = previous.Enabled;
                row.Note = previous.Note;
                what = "Önceki ayarlara dönüldü (" + (previous.Enabled ? "açık" : "kapalı") + ")";
            }
            row.PendingUntilUtc = null;
            row.PreviousJson = null;
            row.UpdatedUtc = DbTime.Now();
            if (reason != null) row.UpdatedBy = "Sistem (" + reason + ")";
            await WriteRowAsync(row).ConfigureAwait(false);
            try
            {
                ApplyRow(row);
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Geri alma firewall'a yazılamadı: " + row.Name, ex); // nöbet tekrar dener
            }
            if (reason != null) AuditTrail.Record(_db, "Sistem", "Port kısıtı geri alındı", row.Name + ": " + what + " (" + reason + ")");
            return what;
        }

        /// <summary>
        /// Firewall'ı DB'ye göre düzeltir: eksik / değiştirilmiş kuralı yeniden yazar, DB'de karşılığı olmayan kuralımızı siler.
        /// </summary>
        public async Task<List<string>> SyncAsync(string trigger)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            var problems = new List<string>();
            try
            {
                try
                {
                    var expected = LoadRows().SelectMany(Expected).ToList();
                    var actual = _firewall.ListRules();

                    foreach (var e in expected)
                    {
                        var have = actual.Where(a => string.Equals(a.Name, e.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (have.Count == 0)
                        {
                            problems.Add(e.Name + " kuralı firewall'da yok");
                            _firewall.SetRule(e);
                        }
                        else if (have.Count > 1 || !Same(have[0], e))
                        {
                            problems.Add(e.Name + " kuralı değiştirilmiş" + (have.Count > 1 ? " (aynı adla " + have.Count + " kural)" : ""));
                            if (have.Count > 1) _firewall.DeleteRule(e.Name);
                            _firewall.SetRule(e);
                        }
                    }
                    foreach (var name in actual.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                                 .Where(n => !expected.Any(e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase))).ToList())
                    {
                        problems.Add(name + " kuralı DB'de karşılığı olmadığı için silindi");
                        _firewall.DeleteRule(name);
                    }

                    _firewallOk = true;
                    _firewallMessage = problems.Count == 0
                        ? "Port kısıtı kuralları uyumlu (" + expected.Count + " kural)."
                        : "Port kısıtı kuralları onarıldı (tetikleyen: " + trigger + "): " + string.Join("; ", problems);
                }
                catch (Exception ex)
                {
                    _firewallOk = false;
                    _firewallMessage = "Port kısıtı kuralları okunamadı / yazılamadı: " + ex.Message;
                    AppLog.Error(Module, "Firewall senkronu başarısız.", ex);
                    return problems;
                }
            }
            finally
            {
                _gate.Release();
            }

            if (problems.Count > 0)
            {
                AppLog.Warn(Module, _firewallMessage);
                var ev = new GuardEvent("Guard.PortRuleTampered", "Port kısıtı firewall kurallarında dışarıdan değişiklik bulundu ve düzeltildi.")
                {
                    Source = "Firewall kontrolü (" + trigger + ")"
                };
                foreach (var p in problems) ev.With("Bulgu", p);
                _events.Record(ev);
            }
            return problems;
        }

        // ================================================================ Derleme / doğrulama

        internal Compiled Compile(PortRestrictionDto item)
        {
            if (string.IsNullOrWhiteSpace(item.Name)) throw Invalid("Kısıt adı girin (ör. \"RDP - ofis\").");
            if (item.Name.Trim().Length > MaxNameLength) throw Invalid("Ad en fazla " + MaxNameLength + " karakter olabilir.");

            string protocol = NormalizeProtocol(item.Protocol);
            var c = new Compiled { Tcp = protocol != "UDP", Udp = protocol != "TCP" };

            string portError;
            if (!TryParsePorts(item.Ports, out c.Ports, out c.PortRanges, out portError)) throw Invalid(portError);

            var allowed = new List<IpInterval>();
            int lines = 0;
            foreach (var raw in (item.Addresses ?? "").Split('\n'))
            {
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0) continue;
                foreach (var token in line.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (++lines > MaxAddressLines) throw Invalid("En fazla " + MaxAddressLines + " adres girilebilir.");
                    IpInterval interval;
                    string error;
                    if (!IpIntervals.TryParse(token, out interval, out error))
                        throw Invalid(error + (token.Any(char.IsLetter) && !token.Contains(":") ? " (açıklama yazmak için satıra # ekleyin: 85.105.0.10  # merkez ofis)" : ""));
                    allowed.Add(interval);
                    c.AllowedText.Add(token);
                }
            }
            var codes = (item.Countries ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToUpperInvariant()).Distinct().ToList();
            if (codes.Count > 0)
            {
                if (_geo == null || !_geo.Available) throw Invalid("Ülke veritabanı bulunamadı; ülke seçimi kullanılamaz.");
                var known = new HashSet<string>(_geo.Countries(), StringComparer.OrdinalIgnoreCase);
                var unknown = codes.Where(x => !known.Contains(x)).ToList();
                if (unknown.Count > 0) throw Invalid("Bilinmeyen ülke kodu: " + string.Join(", ", unknown));
                foreach (var r in _geo.Ranges(codes)) allowed.AddRange(r.Value);
                c.Countries = codes.OrderBy(x => x).ToList();
                c.AllowedText.Add(string.Join(", ", c.Countries.Select(x => GeoIpDatabase.CountryName(x) + " (" + x + ")")));
            }

            if (lines == 0 && !item.AllowLan && codes.Count == 0)
                throw Invalid("En az bir izinli IP / ülke girin veya \"İç ağ her zaman izinli\" seçeneğini açık bırakın. Portu tamamen kapatmak bu ekranın işi değil.");

            if (item.AllowLan) c.AllowedText.Add("iç ağ");
            foreach (var s in AlwaysAllowed.Concat(item.AllowLan ? LanRanges : new string[0]))
            {
                IpInterval interval;
                string error;
                IpIntervals.TryParse(s, out interval, out error);
                allowed.Add(interval);
            }

            c.Allowed = IpIntervals.Merge(allowed);
            var block = IpIntervals.Complement(c.Allowed, AddressFamily.InterNetwork)
                .Concat(IpIntervals.Complement(c.Allowed, AddressFamily.InterNetworkV6)).ToList();
            if (block.Count == 0) throw Invalid("İzinli liste tüm adresleri kapsıyor; kısıt hiçbir şeyi engellemez.");
            if (block.Count > MaxBlockEntries)
                throw Invalid("Engel listesi çok büyük (" + block.Count.ToString("N0") + " aralık; en fazla " + MaxBlockEntries.ToString("N0") + "). "
                              + "Seçilen ülkeler çok parçalı IP bloklarına sahip; daha az ülke seçin veya IP aralığı girin.");
            c.BlockCount = block.Count;
            c.BlockChunks = new List<string>();
            for (int i = 0; i < block.Count; i += MaxPerRule)
                c.BlockChunks.Add(string.Join(",", block.Skip(i).Take(MaxPerRule).Select(b => b.ToString())));
            return c;
        }

        /// <summary>"3389", "1433, 1434", "5000-5100" → normalize metin + aralıklar.</summary>
        internal static bool TryParsePorts(string text, out string normalized, out List<Tuple<int, int>> ranges, out string error)
        {
            normalized = null;
            error = null;
            ranges = new List<Tuple<int, int>>();
            var parts = (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = "Port girin (ör. 3389 veya 1433,1434 veya 5000-5100).";
                return false;
            }
            if (parts.Length > 50)
            {
                error = "En fazla 50 port / aralık girilebilir.";
                return false;
            }
            foreach (var p in parts)
            {
                if (p == "*")
                {
                    error = "Tüm portlar (*) kısıtlanamaz; belirli portları yazın.";
                    return false;
                }
                int dash = p.IndexOf('-');
                int a, b;
                bool ok = dash > 0
                    ? int.TryParse(p.Substring(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out a) & int.TryParse(p.Substring(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out b)
                    : int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out a) & int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out b);
                if (!ok || a < 1 || b > 65535 || a > b)
                {
                    error = "Geçersiz port: " + p + " (1-65535 arası sayı veya aralık).";
                    return false;
                }
                ranges.Add(Tuple.Create(a, b));
            }
            ranges = ranges.OrderBy(r => r.Item1).ToList();
            for (int i = 1; i < ranges.Count; i++)
                if (ranges[i].Item1 <= ranges[i - 1].Item2)
                {
                    error = "Portlar çakışıyor: " + Format(ranges[i - 1]) + " ve " + Format(ranges[i]) + ".";
                    return false;
                }
            normalized = string.Join(",", ranges.Select(Format));
            return true;
        }

        private static string Format(Tuple<int, int> r)
        {
            return r.Item1 == r.Item2 ? r.Item1.ToString(CultureInfo.InvariantCulture) : r.Item1 + "-" + r.Item2;
        }

        private void CheckOverlap(PortRestrictionDto item, Compiled c, List<Row> rows)
        {
            foreach (var other in rows.Where(r => r.Enabled && r.Id != item.Id))
            {
                string otherProtocol = other.Protocol;
                bool protocolOverlap = (c.Tcp && otherProtocol != "UDP") || (c.Udp && otherProtocol != "TCP");
                if (!protocolOverlap) continue;
                string n; List<Tuple<int, int>> otherRanges; string err;
                if (!TryParsePorts(other.Ports, out n, out otherRanges, out err)) continue;
                var hit = c.PortRanges.SelectMany(a => otherRanges.Where(b => a.Item1 <= b.Item2 && b.Item1 <= a.Item2)
                    .Select(b => Math.Max(a.Item1, b.Item1))).FirstOrDefault();
                if (hit > 0)
                    throw Invalid("Port " + hit + " zaten \"" + other.Name + "\" kısıtında. Bir porta tek kısıt uygulanabilir "
                                  + "(iki kısıt olursa sadece ikisinde de olan IP'ler bağlanabilir); IP'leri o kısıta ekleyin.");
            }
        }

        private bool CoversRdp(Compiled c)
        {
            int port = RdpPortSource();
            return (c.Tcp || c.Udp) && c.PortRanges.Any(r => port >= r.Item1 && port <= r.Item2);
        }

        private void CheckOwnAddress(Compiled c, string clientAddress)
        {
            if (string.IsNullOrEmpty(clientAddress) || !CoversRdp(c)) return;
            IPAddress ip;
            if (!IpRange.TryParseAddress(clientAddress, out ip)) return;
            if (!IpIntervals.Contains(c.Allowed, ip))
                throw Invalid("Şu an " + ip + " adresinden RDP ile bağlısınız ve bu adres izinli listede yok: kayıt yapılırsa sunucuya bir daha RDP ile bağlanamazsınız. "
                              + "Önce kendi IP'nizi listeye ekleyin.");
        }

        private List<string> BlockedSessions(Compiled c)
        {
            var result = new List<string>();
            if (!CoversRdp(c)) return result;
            List<KeyValuePair<string, string>> sessions;
            try
            {
                sessions = SessionSource();
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "RDP oturumları okunamadı.", ex);
                return result;
            }
            foreach (var s in sessions)
            {
                IPAddress ip;
                if (!IpRange.TryParseAddress(s.Value, out ip)) continue;
                if (!IpIntervals.Contains(c.Allowed, ip)) result.Add(s.Key + " (" + ip + ")");
            }
            return result;
        }

        private static string Summary(PortRestrictionDto item, Compiled c)
        {
            return ProtocolText(NormalizeProtocol(item.Protocol)) + " " + c.Ports + " portuna sadece şunlar bağlanabilir: "
                   + (c.AllowedText.Count == 0 ? "(yok)" : string.Join(", ", c.AllowedText)) + " ve localhost. "
                   + "Diğer tüm IPv4 / IPv6 adresleri engellenir (firewall'a " + c.BlockCount.ToString("N0") + " engel aralığı olarak yazılır"
                   + (c.BlockChunks.Count > 1 ? ", protokol başına " + c.BlockChunks.Count + " kural" : "") + ").";
        }

        private static GuardException Invalid(string message)
        {
            return new GuardException(ErrorCodes.Validation, message);
        }

        // ================================================================ Firewall

        internal static string RuleName(long id, string protocol)
        {
            return GuardConstants.PortRestrictionRulePrefix + "-" + id.ToString("000", CultureInfo.InvariantCulture) + "-" + protocol;
        }

        /// <summary>Kayıt için olması gereken firewall kuralları (kapalıysa / bozuksa boş).</summary>
        private List<PortRule> Expected(Row row)
        {
            var list = new List<PortRule>();
            if (!row.Enabled) return list;
            Compiled c;
            try
            {
                c = Compile(ToDto(row));
            }
            catch (GuardException ex)
            {
                AppLog.Warn(Module, "Kayıtlı kısıt geçersiz, uygulanmadı: " + row.Name + " (" + ex.Message + ")");
                return list;
            }
            string description = "SecurityGuard port kısıtı: " + row.Name + ". Bu porta sadece izinli IP'ler bağlanabilir, bu kural diğerlerini engeller. "
                                 + "SecurityGuard arayüzünden yönetilir (Koruma → Port Kısıtları); elle değiştirmeyin, değiştirilirse geri yazılır.";
            foreach (var p in new[] { Tuple.Create(c.Tcp, "TCP", PortRule.Tcp), Tuple.Create(c.Udp, "UDP", PortRule.Udp) })
            {
                if (!p.Item1) continue;
                for (int i = 0; i < c.BlockChunks.Count; i++)
                    list.Add(new PortRule
                    {
                        Name = RuleName(row.Id, p.Item2) + (i == 0 ? "" : "-" + (i + 1).ToString(CultureInfo.InvariantCulture)),
                        Description = description,
                        Protocol = p.Item3,
                        LocalPorts = c.Ports,
                        RemoteAddresses = c.BlockChunks[i]
                    });
            }
            return list;
        }

        private void ApplyRow(Row row)
        {
            var expected = Expected(row);
            foreach (var rule in expected) _firewall.SetRule(rule);
            foreach (var name in OwnRules(row.Id).Where(n => !expected.Any(e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase))))
                _firewall.DeleteRule(name);
        }

        /// <summary>Firewall'daki bu kısıta ait kurallar (tüm protokol / parçalar).</summary>
        private List<string> OwnRules(long id)
        {
            string prefix = GuardConstants.PortRestrictionRulePrefix + "-" + id.ToString("000", CultureInfo.InvariantCulture) + "-";
            return _firewall.ListRules().Select(r => r.Name).Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static bool Same(PortRule actual, PortRule expected)
        {
            if (!string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (!actual.Enabled || !actual.IsBlockIn || !actual.AllProfiles || actual.Protocol != expected.Protocol) return false;
            string a, b; List<Tuple<int, int>> ra, rb; string err;
            if (!TryParsePorts(actual.LocalPorts, out a, out ra, out err) || !TryParsePorts(expected.LocalPorts, out b, out rb, out err) || a != b) return false;
            var x = IpIntervals.ParseFirewall(actual.RemoteAddresses);
            var y = IpIntervals.ParseFirewall(expected.RemoteAddresses);
            return x != null && y != null && IpIntervals.SameCoverage(x, y);
        }

        // ================================================================ DB

        private List<Row> LoadRows()
        {
            return _db.Read(c =>
            {
                var list = new List<Row>();
                using (var cmd = c.Create(@"SELECT Id, Name, Protocol, Ports, Addresses, AllowLan, Enabled, Note, UpdatedUtc, UpdatedBy, PendingUntilUtc, PreviousJson, Countries
                                            FROM PortRestrictions ORDER BY Name, Id"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new Row
                        {
                            Id = r.GetInt64(0),
                            Name = r.GetString(1),
                            Protocol = r.GetString(2),
                            Ports = r.GetString(3),
                            Addresses = r.IsDBNull(4) ? "" : r.GetString(4),
                            AllowLan = r.GetInt64(5) != 0,
                            Enabled = r.GetInt64(6) != 0,
                            Note = r.IsDBNull(7) ? null : r.GetString(7),
                            UpdatedUtc = r.GetString(8),
                            UpdatedBy = r.IsDBNull(9) ? null : r.GetString(9),
                            PendingUntilUtc = r.IsDBNull(10) ? null : r.GetString(10),
                            PreviousJson = r.IsDBNull(11) ? null : r.GetString(11),
                            Countries = r.IsDBNull(12) ? null : r.GetString(12)
                        });
                }
                return list;
            });
        }

        private Task<long> WriteRowAsync(Row row)
        {
            return _db.Writer.EnqueueAsync(c =>
            {
                var args = new (string, object)[]
                {
                    ("@id", row.Id), ("@n", row.Name), ("@p", row.Protocol), ("@ports", row.Ports), ("@a", row.Addresses),
                    ("@lan", row.AllowLan ? 1 : 0), ("@e", row.Enabled ? 1 : 0), ("@note", row.Note), ("@t", row.UpdatedUtc),
                    ("@u", row.UpdatedBy), ("@pend", row.PendingUntilUtc), ("@prev", row.PreviousJson), ("@cc", row.Countries)
                };
                if (row.Id > 0)
                {
                    c.Execute(@"UPDATE PortRestrictions SET Name = @n, Protocol = @p, Ports = @ports, Addresses = @a, AllowLan = @lan, Enabled = @e,
                                       Note = @note, UpdatedUtc = @t, UpdatedBy = @u, PendingUntilUtc = @pend, PreviousJson = @prev, Countries = @cc WHERE Id = @id", args);
                    return row.Id;
                }
                c.Execute(@"INSERT INTO PortRestrictions (Name, Protocol, Ports, Addresses, AllowLan, Enabled, Note, UpdatedUtc, UpdatedBy, PendingUntilUtc, PreviousJson, Countries)
                            VALUES (@n, @p, @ports, @a, @lan, @e, @note, @t, @u, @pend, @prev, @cc)", args);
                return c.Scalar<long>("SELECT last_insert_rowid()");
            });
        }

        private static PortRestrictionDto ToDto(Row r)
        {
            return new PortRestrictionDto
            {
                Id = r.Id,
                Name = r.Name,
                Protocol = r.Protocol,
                Ports = r.Ports,
                Addresses = r.Addresses,
                Countries = r.Countries,
                AllowLan = r.AllowLan,
                Enabled = r.Enabled,
                Note = r.Note,
                UpdatedUtc = string.IsNullOrEmpty(r.UpdatedUtc) ? (DateTime?)null : DbTime.FromDb(r.UpdatedUtc),
                UpdatedBy = r.UpdatedBy,
                PendingUntilUtc = r.PendingUntilUtc == null ? (DateTime?)null : DbTime.FromDb(r.PendingUntilUtc)
            };
        }

        private static string NormalizeProtocol(string protocol)
        {
            string p = (protocol ?? "").Trim().ToUpperInvariant();
            return p == "UDP" || p == "BOTH" ? p : "TCP";
        }

        private static string ProtocolText(string protocol)
        {
            return protocol == "BOTH" ? "TCP+UDP" : protocol;
        }

        private static string NormalizeAddressText(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return string.Join("\r\n", lines);
        }

        private void RecordChange(string summary, Row row, string userName, string what)
        {
            _events.Record(new GuardEvent("Guard.PortRestrictionChanged", summary)
            {
                Source = "SecurityGuard arayüzü",
                UserName = userName
            }
            .With("Kısıt", row.Name)
            .With("Port", ProtocolText(row.Protocol) + " " + row.Ports)
            .With("İzinli adresler", string.IsNullOrWhiteSpace(row.Addresses) ? "(yok)" : row.Addresses.Replace("\r\n", ", "))
            .With("Ülkeler", string.IsNullOrEmpty(row.Countries) ? null : string.Join(", ", row.Countries.Split(',').Select(x => GeoIpDatabase.CountryName(x) + " (" + x + ")")))
            .With("İç ağ", row.AllowLan ? "izinli" : "engelli")
            .With("Yapılan", what)
            .With("Yapan", userName));
        }

        // ================================================================ Sunucu bilgisi

        private List<PortTemplateDto> Templates()
        {
            var list = new List<PortTemplateDto>
            {
                new PortTemplateDto
                {
                    Name = "Uzak Masaüstü (RDP)", Protocol = "BOTH", Ports = RdpPortSource().ToString(CultureInfo.InvariantCulture),
                    Note = "RDP portu sunucunun ayarından okundu. RDP hem TCP hem UDP kullanır."
                }
            };
            try
            {
                var instances = SqlDiscovery.Detect();
                foreach (var i in instances.Where(i => i.TcpPort > 0))
                    list.Add(new PortTemplateDto
                    {
                        Name = "SQL Server (" + i.Name + ")", Protocol = "TCP", Ports = i.TcpPort.ToString(CultureInfo.InvariantCulture),
                        Note = "SQL Server TCP portu (SQL yapılandırmasından okundu)."
                    });
                if (instances.Any(i => !SqlDiscovery.IsDefault(i.Name)))
                    list.Add(new PortTemplateDto
                    {
                        Name = "SQL Browser", Protocol = "UDP", Ports = "1434",
                        Note = "Adlı instance'lara \"SUNUCU\\AD\" ile bağlanırken kullanılır."
                    });
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "SQL portları okunamadı.", ex);
            }
            return list;
        }

        private static int ReadRdpPort()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp"))
                {
                    object v = key == null ? null : key.GetValue("PortNumber");
                    if (v is int && (int)v > 0 && (int)v <= 65535) return (int)v;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "RDP portu okunamadı.", ex);
            }
            return 3389;
        }

        private static List<KeyValuePair<string, string>> DefaultSessions()
        {
            return WindowsNative.Sessions().Where(s => !string.IsNullOrEmpty(s.ClientAddress))
                .Select(s => new KeyValuePair<string, string>(s.User + (s.Disconnected ? ", bağlantısı kopuk" : ""), s.ClientAddress)).ToList();
        }
    }
}
