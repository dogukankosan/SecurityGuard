using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Net;
using SecurityGuardService.Engine.Events;
using SecurityGuardService.Engine.Rules;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Engine.Ban
{
    /// <summary>
    /// Ban motoru.
    /// - Dış IP'den başarısız giriş sayacı (IP + protokol başına, kümülatif, başarılı giriş sıfırlamaz).
    ///   Kuralda ban açıksa ve eşik aşılırsa IP banlanır.
    /// - Ban: firewall'da gelen trafik, tüm portlar engellenir. Kurallar "SecurityGuard-Block-001" gibi, her birinde
    ///   en fazla <see cref="MaxPerRule"/> IP. Kural içeriği her değişiklikte DB'den yeniden yazılır (tek doğru kaynak DB).
    /// - Korunan adresler (localhost, iç ağ, sunucu IP'leri, beyaz liste) hiçbir yoldan banlanamaz.
    /// - <see cref="SyncFirewall"/>: firewall DB ile uyuşmuyorsa (kural silinmiş / değiştirilmiş) düzeltir ve olay üretir.
    /// Tüm işlemler sırayla yapılır (aynı anda iki ban aynı kuralı bozmasın).
    /// </summary>
    internal sealed class BanManager
    {
        private const string Module = "Ban";
        public const int MaxPerRule = 1000;
        public const string ProtocolRdp = "RDP";
        public const string ProtocolSql = "SQL";
        public const string ProtocolNet = "SMB";
        public const string ProtocolFtp = "FTP";
        public const string ProtocolWeb = "WEB";
        public const string ProtocolSmtp = "SMTP";

        /// <summary>Protokol → eşik / ban ayarının bulunduğu olay kuralı.</summary>
        private static readonly Dictionary<string, string> ProtocolRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ProtocolRdp, EventCatalog.RdpLoginFailed },
            { ProtocolSql, EventCatalog.SqlLoginFailed },
            { ProtocolNet, EventCatalog.NetLoginFailed },
            { ProtocolFtp, EventCatalog.FtpLoginFailed },
            { ProtocolWeb, EventCatalog.WebLoginFailed },
            { ProtocolSmtp, EventCatalog.SmtpLoginFailed }
        };

        private readonly GuardDatabase _db;
        private readonly Whitelist _whitelist;
        private readonly RuleStore _rules;
        private readonly EventRecorder _events;
        private readonly IBanFirewall _firewall;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _queueLock = new object();
        private readonly List<BanRequest> _queue = new List<BanRequest>();
        private bool _queueScheduled;
        private volatile ConcurrentDictionary<string, string> _banned;

        /// <summary>Eşik aşan IP'ler bu süre toplanıp tek seferde banlanır.</summary>
        public static readonly TimeSpan BanBatchDelay = TimeSpan.FromMilliseconds(300);
        private int _syncRequested;
        private Timer _watchdog;

        /// <summary>Periyodik firewall kontrolü (olay kaçsa bile silinen / değiştirilen kural en geç bu sürede onarılır).</summary>
        public static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(5);

        private bool _firewallOk = true;
        private string _firewallMessage = "Kontrol edilmedi.";

        public BanManager(GuardDatabase db, Whitelist whitelist, RuleStore rules, EventRecorder events, IBanFirewall firewall)
        {
            _db = db;
            _whitelist = whitelist;
            _rules = rules;
            _events = events;
            _firewall = firewall;
        }

        // ================================================================ Sayaç (collector'lar çağırır)

        /// <summary>
        /// Başarısız girişi sayar; eşik aşıldıysa banlar. Korunan adresler sayılır ama banlanmaz.
        /// </summary>
        /// <returns>Yeni başarısız deneme sayısı.</returns>
        public async Task<int> RegisterFailureAsync(string ip, string protocol, string userName)
        {
            if (!LicenseGate.Active) return 0; // lisans yok: yeni ban yapılmaz (mevcutlar kalır)
            IPAddress address = ParseOrThrow(ip);
            string key = address.ToString();
            string now = DbTime.Now();

            int count = await _db.Writer.EnqueueAsync(c =>
            {
                c.Execute(@"INSERT INTO LoginAttempts (IpAddress, Protocol, FailCount, SuccessCount, LastUserName, FirstUtc, LastUtc, FailsSinceSuccess)
                            VALUES (@ip, @p, 1, 0, @u, @t, @t, 1)
                            ON CONFLICT(IpAddress, Protocol) DO UPDATE SET FailCount = FailCount + 1, FailsSinceSuccess = FailsSinceSuccess + 1,
                                                                          LastUserName = @u, LastUtc = @t",
                    ("@ip", key), ("@p", protocol), ("@u", userName), ("@t", now));
                return (int)c.Scalar<long>("SELECT FailCount FROM LoginAttempts WHERE IpAddress = @ip AND Protocol = @p", ("@ip", key), ("@p", protocol));
            }).ConfigureAwait(false);

            var rule = _rules.Get(RuleFor(protocol));
            if (rule == null || !rule.Monitor || !rule.BanEnabled || count < rule.Threshold) return count;
            if (_whitelist.IsProtected(address) || IsBanned(key)) return count;

            QueueBan(new BanRequest
            {
                Address = address,
                Protocol = protocol,
                FailCount = count,
                Reason = ProtocolName(protocol) + " " + count + " başarısız giriş (eşik " + rule.Threshold + ")" +
                         (string.IsNullOrEmpty(userName) ? "" : ", son denenen kullanıcı: " + userName)
            });
            return count;
        }

        /// <summary>
        /// Başarılı girişi sayar (toplam başarısız sayacı sıfırlanmaz: kümülatif). "Son başarılıdan beri başarısız"
        /// sayacı sıfırlanır.
        /// </summary>
        /// <returns>Giriş ÖNCESİ sayaçlar (yeni IP / hatalardan sonra başarı tespiti için).</returns>
        public Task<LoginHistory> RegisterSuccessAsync(string ip, string protocol, string userName)
        {
            IPAddress address = ParseOrThrow(ip);
            string key = address.ToString();
            string now = DbTime.Now();
            return _db.Writer.EnqueueAsync(c =>
            {
                var before = new LoginHistory();
                // IP bazında, TÜM protokoller toplanarak: başarısız denemeler farklı kovaya düşmüş olabilir
                // (uzaktan NLA-RDP Windows'ta tip-3 = "ağ/SMB" kovasına yazılır, başarılı RDP ise RDP kovasına).
                using (var cmd = c.Create("SELECT COALESCE(SUM(FailCount),0), COALESCE(SUM(SuccessCount),0), COALESCE(SUM(FailsSinceSuccess),0) FROM LoginAttempts WHERE IpAddress = @ip",
                    ("@ip", key)))
                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        before.FailCount = (int)r.GetInt64(0);
                        before.SuccessCount = (int)r.GetInt64(1);
                        before.FailsSinceSuccess = (int)r.GetInt64(2);
                    }
                }
                c.Execute(@"INSERT INTO LoginAttempts (IpAddress, Protocol, FailCount, SuccessCount, LastUserName, FirstUtc, LastUtc, FailsSinceSuccess)
                            VALUES (@ip, @p, 0, 1, @u, @t, @t, 0)
                            ON CONFLICT(IpAddress, Protocol) DO UPDATE SET SuccessCount = SuccessCount + 1, FailsSinceSuccess = 0,
                                                                          LastUserName = @u, LastUtc = @t",
                    ("@ip", key), ("@p", protocol), ("@u", userName), ("@t", now));
                // Başarıdan sonra tüm protokol kovalarının "son başarıdan beri başarısız" sayacı sıfırlanır (aynı başarı tekrar alarm üretmesin).
                c.Execute("UPDATE LoginAttempts SET FailsSinceSuccess = 0 WHERE IpAddress = @ip", ("@ip", key));
                return before;
            });
        }

        public bool IsProtected(string ip)
        {
            IPAddress address;
            return IpRange.TryParseAddress(ip, out address) && _whitelist.IsProtected(address);
        }

        // ================================================================ UI

        public BanListResult List()
        {
            var banned = ReadBanned();
            var bannedSet = new HashSet<string>(banned.Select(b => b.IpAddress), StringComparer.OrdinalIgnoreCase);

            var attempts = _db.Read(c =>
            {
                var list = new List<LoginAttemptDto>();
                using (var cmd = c.Create("SELECT IpAddress, Protocol, FailCount, SuccessCount, LastUserName, FirstUtc, LastUtc FROM LoginAttempts ORDER BY LastUtc DESC"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string ip = r.GetString(0);
                        string protocol = r.GetString(1);
                        var rule = _rules.Get(RuleFor(protocol));
                        IPAddress address;
                        list.Add(new LoginAttemptDto
                        {
                            IpAddress = ip,
                            Protocol = protocol,
                            FailCount = r.GetInt32(2),
                            SuccessCount = r.GetInt32(3),
                            LastUserName = r.IsDBNull(4) ? null : r.GetString(4),
                            FirstUtc = r.GetString(5),
                            LastUtc = r.GetString(6),
                            Threshold = rule == null ? 0 : rule.Threshold,
                            IsBanned = bannedSet.Contains(ip),
                            IsProtected = IpRange.TryParseAddress(ip, out address) && _whitelist.IsProtected(address)
                        });
                    }
                }
                return list;
            });

            return new BanListResult { Banned = banned, Attempts = attempts, FirewallOk = _firewallOk, FirewallMessage = _firewallMessage };
        }

        public async Task BanManualAsync(string ip, string reason, string userName)
        {
            AppLog.Info(Module, "Elle ban isteği: " + ip + " (" + userName + ")");
            try
            {
                IPAddress address = ParseOrThrow(ip);
                if (_whitelist.IsProtected(address))
                    throw new GuardException(ErrorCodes.Validation, address + " korunan bir adres (iç ağ, localhost, sunucu IP'si veya beyaz liste). Bu adresler asla banlanmaz.");
                if (IsBanned(address.ToString()))
                    throw new GuardException(ErrorCodes.Validation, address + " zaten banlı.");

                reason = string.IsNullOrWhiteSpace(reason) ? "Elle ban" : reason.Trim();
                if (reason.Length > 200) reason = reason.Substring(0, 200);
                await BanBatchAsync(new List<BanRequest> { new BanRequest { Address = address, Reason = reason } }, true, userName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Arayüz hatayı gösterir; servis logunda da görünsün (sorun araştırması için)
                AppLog.Warn(Module, "Elle ban yapılamadı: " + ip + " → " + ex.Message);
                throw;
            }
        }

        public async Task UnbanAsync(IList<string> ips, bool resetCounter, string userName)
        {
            if (ips == null || ips.Count == 0) return;
            var keys = ips.Select(ip => ParseOrThrow(ip).ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var current = ReadBanned().Where(b => keys.Contains(b.IpAddress, StringComparer.OrdinalIgnoreCase)).ToList();
                if (current.Count == 0) return;

                await _db.Writer.EnqueueAsync(c =>
                {
                    foreach (var b in current)
                    {
                        c.Execute("DELETE FROM BannedIps WHERE IpAddress = @ip", ("@ip", b.IpAddress));
                        if (resetCounter) c.Execute("UPDATE LoginAttempts SET FailCount = 0 WHERE IpAddress = @ip", ("@ip", b.IpAddress));
                    }
                }).ConfigureAwait(false);
                string removed;
                foreach (var b in current) Banned().TryRemove(b.IpAddress, out removed);

                // Etkilenen kurallar yeniden yazılır (boşalan kural silinir)
                foreach (var name in current.Select(b => b.RuleName).Distinct())
                    ApplyRule(name);

                foreach (var b in current)
                {
                    AuditTrail.Record(_db, userName, "Ban kaldırıldı", b.IpAddress + (resetCounter ? " (sayaç sıfırlandı)" : ""));
                    AppLog.Info(Module, "Ban kaldırıldı: " + b.IpAddress + " (" + userName + ")");
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task ResetAttemptsAsync(IList<string> ips, string userName)
        {
            if (ips == null || ips.Count == 0) return;
            var keys = ips.Select(ip => ParseOrThrow(ip).ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            await _db.Writer.EnqueueAsync(c =>
            {
                foreach (var k in keys) c.Execute("DELETE FROM LoginAttempts WHERE IpAddress = @ip", ("@ip", k));
            }).ConfigureAwait(false);
            AuditTrail.Record(_db, userName, "Deneme sayacı silindi", string.Join(", ", keys));
        }

        // ================================================================ Firewall senkronu

        /// <summary>Açılışta hemen, sonra her <see cref="WatchdogInterval"/>'da bir firewall'ı DB'ye göre kontrol eder.</summary>
        public void StartWatchdog()
        {
            bool first = true;
            _watchdog = new Timer(_ => { RunSync(first ? "servis açılışı" : "periyodik (5 dk)"); first = false; }, null, TimeSpan.Zero, WatchdogInterval);
        }

        public void StopWatchdog()
        {
            var t = _watchdog;
            _watchdog = null;
            if (t != null) t.Dispose();
        }

        /// <summary>
        /// Kısa süre sonra kontrol ister (kural olayı geldi). Art arda gelen olaylar tek kontrolde birleşir
        /// (bizim tek bir yazmamız birkaç firewall olayı üretir).
        /// </summary>
        public void RequestSync()
        {
            if (Interlocked.Exchange(ref _syncRequested, 1) == 1) return;
            Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ =>
            {
                Interlocked.Exchange(ref _syncRequested, 0);
                RunSync("firewall olayı");
            });
        }

        private void RunSync(string trigger)
        {
            try
            {
                SyncFirewall(trigger).Wait();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Firewall kontrolü başarısız.", ex);
            }
        }

        /// <summary>
        /// Firewall'ı DB'ye göre düzeltir: eksik / değiştirilmiş kuralı yeniden yazar, DB'de karşılığı olmayan
        /// bizim kuralımızı siler. Fark bulunursa "Ban firewall kuralı silindi / değiştirildi" olayı üretir.
        /// </summary>
        public async Task<List<string>> SyncFirewall(string trigger = "elle (Firewall Kontrol)")
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var problems = new List<string>();
                try
                {
                    _banned = LoadBanned(); // bellekteki liste DB'ye göre tazelenir (SQLite ekranından değişmiş olabilir)
                    var expected = _banned.Where(b => b.Value != null).GroupBy(b => b.Value, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Select(b => b.Key).ToList(), StringComparer.OrdinalIgnoreCase);
                    var actual = _firewall.ListRules();

                    foreach (var e in expected)
                    {
                        List<string> have;
                        if (!actual.TryGetValue(e.Key, out have))
                        {
                            problems.Add(e.Key + " kuralı firewall'da yok (" + e.Value.Count + " IP)");
                            _firewall.SetRule(e.Key, e.Value);
                        }
                        else if (!SameSet(have, e.Value))
                        {
                            problems.Add(e.Key + " kuralının adresleri değiştirilmiş");
                            _firewall.SetRule(e.Key, e.Value);
                        }
                    }
                    foreach (var name in actual.Keys.Where(n => !expected.ContainsKey(n)).ToList())
                    {
                        problems.Add(name + " kuralı DB'de karşılığı olmadığı için silindi");
                        _firewall.DeleteRule(name);
                    }

                    _firewallOk = true;
                    _firewallMessage = problems.Count == 0
                        ? "Firewall kuralları uyumlu (" + expected.Sum(e => e.Value.Count) + " IP, " + expected.Count + " kural)."
                        : "Firewall kuralları onarıldı (tetikleyen: " + trigger + "): " + string.Join("; ", problems);
                }
                catch (Exception ex)
                {
                    _firewallOk = false;
                    _firewallMessage = "Firewall kuralları okunamadı / yazılamadı: " + ex.Message;
                    AppLog.Error(Module, "Firewall senkronu başarısız.", ex);
                    return problems;
                }

                if (problems.Count > 0)
                {
                    AppLog.Warn(Module, _firewallMessage);
                    var ev = new GuardEvent("Guard.BanRuleTampered", "Ban firewall kurallarında dışarıdan değişiklik bulundu ve düzeltildi.");
                    foreach (var p in problems) ev.With("Bulgu", p);
                    _events.Record(ev);
                }
                return problems;
            }
            finally
            {
                _gate.Release();
            }
        }

        // ================================================================ Çekirdek

        /// <summary>Ban isteği (eşik aşımı veya elle).</summary>
        private sealed class BanRequest
        {
            public IPAddress Address;
            public string Protocol;
            public int FailCount;
            public string Reason;
        }

        /// <summary>
        /// Eşik aşan IP'yi ban kuyruğuna atar; çağıranı (olay işleyiciyi) bekletmez. Kuyruk <see cref="BanBatchDelay"/> içinde
        /// gelen tüm banları toplar: kural başına TEK firewall yazması (kural yazma ~20-50 ms; dağıtık saldırıda ban başına
        /// yazmak olay işlemeyi kilitliyordu) ve tek DB işlemi.
        /// </summary>
        private void QueueBan(BanRequest request)
        {
            lock (_queueLock)
            {
                _queue.Add(request);
                if (_queueScheduled) return;
                _queueScheduled = true;
            }
            Task.Delay(BanBatchDelay).ContinueWith(_ => ProcessQueue());
        }

        private void ProcessQueue()
        {
            List<BanRequest> batch;
            lock (_queueLock)
            {
                batch = _queue.ToList();
                _queue.Clear();
                _queueScheduled = false;
            }
            if (batch.Count == 0) return;
            try
            {
                BanBatchAsync(batch, false, null).Wait();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, batch.Count + " IP banlanamadı.", ex.InnerException ?? ex);
            }
        }

        /// <summary>Kuyrukta bekleyen banları hemen uygular (test / kapanış).</summary>
        public void FlushPendingBans()
        {
            ProcessQueue();
            _gate.Wait(); // sürmekte olan toplu ban bitsin
            _gate.Release();
        }

        /// <summary>
        /// Toplu ban: önce firewall (kural başına bir yazma), sonra DB. Firewall'a yazılamayan IP "banlı" yazılmaz.
        /// Elle banda hata fırlatılır; otomatikte loglanır (IP denemeye devam ederse sonraki eşikte tekrar denenir).
        /// </summary>
        private async Task<int> BanBatchAsync(List<BanRequest> requests, bool manual, string userName)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var banned = Banned();
                var todo = new List<BanRequest>();
                foreach (var r in requests)
                {
                    string ip = r.Address.ToString();
                    if (banned.ContainsKey(ip) || todo.Any(t => t.Address.Equals(r.Address))) continue;
                    if (_whitelist.IsProtected(r.Address)) continue; // son kontrol (beyaz liste arada değişmiş olabilir)
                    todo.Add(r);
                }
                if (todo.Count == 0) return 0;

                // Kural dağıtımı: dolmamış en küçük numaralı kurallar doldurulur
                var counts = banned.Values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var assigned = new List<KeyValuePair<BanRequest, string>>();
                int no = 1;
                foreach (var r in todo)
                {
                    int c;
                    while (counts.TryGetValue(RuleName(no), out c) && c >= MaxPerRule) no++;
                    string name = RuleName(no);
                    counts[name] = (counts.TryGetValue(name, out c) ? c : 0) + 1;
                    assigned.Add(new KeyValuePair<BanRequest, string>(r, name));
                }

                // Önce firewall: engel kurulamazsa DB'ye "banlı" yazılmaz (yanlış güven vermesin)
                var ok = new List<KeyValuePair<BanRequest, string>>();
                foreach (var g in assigned.GroupBy(a => a.Value, StringComparer.OrdinalIgnoreCase))
                {
                    var addresses = banned.Where(b => string.Equals(b.Value, g.Key, StringComparison.OrdinalIgnoreCase)).Select(b => b.Key)
                        .Concat(g.Select(a => a.Key.Address.ToString())).ToList();
                    try
                    {
                        _firewall.SetRule(g.Key, addresses);
                        ok.AddRange(g);
                    }
                    catch (Exception ex)
                    {
                        _firewallOk = false;
                        _firewallMessage = "Firewall kuralı yazılamadı: " + ex.Message;
                        if (manual)
                            throw new GuardException(ErrorCodes.InvalidRequest, g.First().Key.Address + " banlanamadı: firewall kuralı yazılamadı (" + ex.Message + "). Servis yönetici yetkisiyle mi çalışıyor?");
                        AppLog.Error(Module, g.Count() + " IP banlanamadı: " + g.Key + " kuralı yazılamadı.", ex);
                    }
                }
                if (ok.Count == 0) return 0;

                string now = DbTime.Now();
                try
                {
                    await _db.Writer.EnqueueAsync(c =>
                    {
                        foreach (var a in ok)
                            c.Execute(@"INSERT INTO BannedIps (IpAddress, Protocol, Reason, RuleName, FailCount, IsManual, FirewallRuleNo, BannedUtc)
                                        VALUES (@ip, @p, @r, @rn, @f, @m, @no, @t)",
                                ("@ip", a.Key.Address.ToString()), ("@p", a.Key.Protocol), ("@r", a.Key.Reason), ("@rn", a.Value), ("@f", a.Key.FailCount),
                                ("@m", manual ? 1 : 0), ("@no", RuleNo(a.Value)), ("@t", now));
                    }).ConfigureAwait(false);
                }
                catch
                {
                    // DB'ye yazılamadıysa firewall'ı DB haline geri al
                    foreach (var name in ok.Select(a => a.Value).Distinct(StringComparer.OrdinalIgnoreCase)) ApplyRule(name);
                    throw;
                }

                foreach (var a in ok)
                {
                    string ip = a.Key.Address.ToString();
                    banned[ip] = a.Value;
                    AppLog.Warn(Module, ip + " banlandı (" + a.Value + "): " + a.Key.Reason);
                    if (manual) AuditTrail.Record(_db, userName, "Elle ban", ip + " - " + a.Key.Reason);

                    _events.Record(new GuardEvent("Ban.IpBanned", ip + " banlandı: " + a.Key.Reason)
                    {
                        Source = manual ? "Elle ban (" + userName + ")" : "Ban motoru",
                        IpAddress = ip
                    }
                    .With("Protokol", a.Key.Protocol ?? "Tümü (elle)")
                    .With("Başarısız deneme", a.Key.FailCount > 0 ? (object)a.Key.FailCount : null)
                    .With("Firewall kuralı", a.Value)
                    .With("Kaldırmak için", "SecurityGuard → Ban Listesi"));
                }
                if (ok.Count > 1) AppLog.Info(Module, ok.Count + " IP tek seferde banlandı (" + string.Join(", ", ok.Select(a => a.Value).Distinct()) + ").");
                return ok.Count;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Banlı IP → kural adı (bellekte; DB'den bir kez yüklenir, her ban / kaldırma / senkronda güncellenir).</summary>
        private ConcurrentDictionary<string, string> Banned()
        {
            var b = _banned;
            if (b != null) return b;
            lock (_queueLock)
            {
                if (_banned == null) _banned = LoadBanned();
                return _banned;
            }
        }

        private ConcurrentDictionary<string, string> LoadBanned()
        {
            return _db.Read(c =>
            {
                var d = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = c.Create("SELECT IpAddress, RuleName FROM BannedIps"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) d[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
                return d;
            });
        }

        /// <summary>Kuralı DB'deki listeye göre yeniden yazar; liste boşsa kuralı siler.</summary>
        private void ApplyRule(string ruleName)
        {
            if (string.IsNullOrEmpty(ruleName)) return;
            var addresses = Banned().Where(b => string.Equals(b.Value, ruleName, StringComparison.OrdinalIgnoreCase)).Select(b => b.Key).ToList();
            try
            {
                if (addresses.Count == 0) _firewall.DeleteRule(ruleName);
                else _firewall.SetRule(ruleName, addresses);
            }
            catch (Exception ex)
            {
                _firewallOk = false;
                _firewallMessage = "Firewall kuralı güncellenemedi: " + ex.Message;
                throw new GuardException(ErrorCodes.InvalidRequest, "Firewall kuralı güncellenemedi (" + ex.Message + "). Servis yönetici yetkisiyle mi çalışıyor?");
            }
        }

        internal static string RuleName(int no)
        {
            return GuardConstants.FirewallRulePrefix + "-" + no.ToString("000", CultureInfo.InvariantCulture);
        }

        private static int RuleNo(string ruleName)
        {
            return int.Parse(ruleName.Substring(GuardConstants.FirewallRulePrefix.Length + 1), CultureInfo.InvariantCulture);
        }

        private bool IsBanned(string ip)
        {
            return Banned().ContainsKey(ip);
        }

        private List<BannedIpDto> ReadBanned()
        {
            return _db.Read(c =>
            {
                var list = new List<BannedIpDto>();
                using (var cmd = c.Create("SELECT IpAddress, Protocol, Reason, RuleName, FailCount, IsManual, BannedUtc FROM BannedIps ORDER BY BannedUtc DESC"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new BannedIpDto
                        {
                            IpAddress = r.GetString(0),
                            Protocol = r.IsDBNull(1) ? null : r.GetString(1),
                            Reason = r.GetString(2),
                            RuleName = r.IsDBNull(3) ? null : r.GetString(3),
                            FailCount = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                            IsManual = r.GetInt64(5) != 0,
                            BannedUtc = r.GetString(6)
                        });
                }
                return list;
            });
        }

        private static bool SameSet(List<string> a, List<string> b)
        {
            var sa = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
            return sa.SetEquals(b);
        }

        private static string RuleFor(string protocol)
        {
            string rule;
            return protocol != null && ProtocolRules.TryGetValue(protocol, out rule) ? rule : EventCatalog.RdpLoginFailed;
        }

        /// <summary>Sebep metni için okunabilir protokol adı. "SMB" kovası uzaktan RDP/NLA'yı da kapsar (tip-3 4625), yanıltmasın diye açık yazılır.</summary>
        private static string ProtocolName(string protocol)
        {
            if (string.Equals(protocol, ProtocolNet, StringComparison.OrdinalIgnoreCase)) return "ağ üzerinden (uzak RDP/NLA, SMB veya WinRM)";
            if (string.Equals(protocol, ProtocolRdp, StringComparison.OrdinalIgnoreCase)) return "RDP üzerinden";
            if (string.Equals(protocol, ProtocolSql, StringComparison.OrdinalIgnoreCase)) return "SQL üzerinden";
            if (string.Equals(protocol, ProtocolWeb, StringComparison.OrdinalIgnoreCase)) return "web (IIS) üzerinden";
            if (string.Equals(protocol, ProtocolFtp, StringComparison.OrdinalIgnoreCase)) return "FTP üzerinden";
            if (string.Equals(protocol, ProtocolSmtp, StringComparison.OrdinalIgnoreCase)) return "SMTP üzerinden";
            return protocol + " üzerinden";
        }

        private static IPAddress ParseOrThrow(string ip)
        {
            IPAddress address;
            if (!IpRange.TryParseAddress((ip ?? "").Trim(), out address))
                throw new GuardException(ErrorCodes.Validation, "Geçersiz IP adresi: " + ip);
            return address;
        }
    }

    /// <summary>Bir IP'nin giriş geçmişi (başarılı giriş öncesi).</summary>
    internal sealed class LoginHistory
    {
        public int FailCount;
        public int SuccessCount;
        public int FailsSinceSuccess;
    }
}
