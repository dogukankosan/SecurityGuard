using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Checks.Performance;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Net;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Firewall;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Engine.Health
{
    /// <summary>
    /// RDP / konsol oturum geçmişi (TerminalServices-LocalSessionManager 21 açıldı, 23 kapandı, 24 koptu, 25 yeniden bağlanıldı)
    /// ve mesai dışı giriş kontrolü (Genel Ayarlar → Mesai saatleri + kullanıcı istisnaları).
    /// Her oturum tek satır: kim, hangi IP / ülkeden, ne zaman, ne kadar; kopma / yeniden bağlanma sayıları.
    /// Olay kaçtıysa (sunucu kapandı, servis kapalıyken oturum bitti) açık kalan satır, oturum artık yoksa "bitiş bilinmiyor" ile kapatılır.
    /// </summary>
    internal sealed class RdpSessionTracker
    {
        private const string Module = "Oturum";
        public const string StateActive = "active";
        public const string StateDisconnected = "disconnected";
        public const string StateEnded = "ended";
        public const string StateLost = "lost";
        public const string Console = "konsol";
        private const int MaxRows = 5000;

        private readonly GuardDatabase _db;
        private readonly SettingsStore _settings;
        private readonly GeoIpDatabase _geo;
        private readonly object _lock = new object();
        private DateTime _lastReconcileUtc = DateTime.MinValue;

        /// <summary>Şu anki oturum numaraları (test için değiştirilebilir).</summary>
        internal Func<HashSet<int>> LiveSessions = () => new HashSet<int>(WindowsNative.Sessions().Select(s => s.Id));

        private System.Threading.Timer _timer;

        public RdpSessionTracker(GuardDatabase db, SettingsStore settings, GeoIpDatabase geo)
        {
            _db = db;
            _settings = settings;
            _geo = geo;
        }

        /// <summary>Kaçmış kapanışların temizliği: açılıştan 2 dk sonra (olay toplayıcı kaldığı yeri okusun), sonra 10 dk'da bir.</summary>
        public void Start()
        {
            _timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (LicenseGate.Active) Reconcile(true);
                }
                catch (Exception ex)
                {
                    AppLog.Warn(Module, "Oturum temizliği yapılamadı.", ex);
                }
            }, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));
        }

        public void Dispose()
        {
            var t = _timer;
            _timer = null;
            if (t != null) t.Dispose();
        }

        // ================================================================ Olaylar (işleyici çağırır)

        /// <summary>Oturum açıldı (21) / yeniden bağlanıldı (25). Mesai dışıysa açıklamayı döner (olay üretilecek), değilse null.</summary>
        public string OnConnect(int sessionId, string user, string ip, DateTime timeUtc, bool reconnect)
        {
            string offHours = ip == null ? null : OffHoursReason(user, timeUtc); // konsol girişi mesai uyarısı üretmez
            string country = ip == null ? null : CountryOf(ip);
            string address = ip ?? Console;
            string t = DbTime.ToDb(timeUtc);
            lock (_lock)
            {
                var open = OpenRow(sessionId, user);
                _db.Writer.EnqueueAsync(c =>
                {
                    if (!reconnect || open == null)
                    {
                        if (open != null) // aynı numarada açık kalmış eski oturum (kapanışı kaçmış)
                            c.Execute("UPDATE RdpSessions SET State = @s, LastChangeUtc = @t WHERE Id = @id", ("@s", StateLost), ("@t", t), ("@id", open.Item1));
                        c.Execute(@"INSERT INTO RdpSessions (SessionId, UserName, ClientIp, Ips, Country, StartUtc, LastChangeUtc, State, Reconnects, OffHours, StartUnknown)
                                    VALUES (@sid, @u, @ip, @ip, @cc, @t, @t, @s, @rc, @off, @unk)",
                            ("@sid", sessionId), ("@u", user), ("@ip", address), ("@cc", country), ("@t", t), ("@s", StateActive),
                            ("@rc", reconnect ? 1 : 0), ("@off", offHours != null ? 1 : 0), ("@unk", reconnect ? 1 : 0));
                    }
                    else
                    {
                        string ips = open.Item3 ?? "";
                        if (!ips.Split(',').Contains(address)) ips = ips.Length == 0 ? address : ips + "," + address;
                        c.Execute(@"UPDATE RdpSessions SET State = @s, LastChangeUtc = @t, Reconnects = Reconnects + 1, Ips = @ips,
                                           OffHours = CASE WHEN @off = 1 THEN 1 ELSE OffHours END WHERE Id = @id",
                            ("@s", StateActive), ("@t", t), ("@ips", ips), ("@off", offHours != null ? 1 : 0), ("@id", open.Item1));
                    }
                }).Wait(TimeSpan.FromSeconds(30));
            }
            return offHours;
        }

        /// <summary>Bağlantı koptu (24): oturum açık kalır.</summary>
        public void OnDisconnect(int sessionId, string user, DateTime timeUtc)
        {
            Update(sessionId, user, timeUtc, "State = '" + StateDisconnected + "', Disconnects = Disconnects + 1");
        }

        /// <summary>Oturum kapandı (23).</summary>
        public void OnLogoff(int sessionId, string user, DateTime timeUtc)
        {
            Update(sessionId, user, timeUtc, "State = '" + StateEnded + "', EndUtc = @t");
        }

        private void Update(int sessionId, string user, DateTime timeUtc, string set)
        {
            string t = DbTime.ToDb(timeUtc);
            lock (_lock)
            {
                var open = OpenRow(sessionId, user);
                if (open == null) return; // açılışı görülmemiş (servisten önce açılmış ve hiç bağlanılmamış) oturum
                _db.Writer.EnqueueAsync(c => c.Execute("UPDATE RdpSessions SET " + set + ", LastChangeUtc = @t WHERE Id = @id", ("@t", t), ("@id", open.Item1)))
                    .Wait(TimeSpan.FromSeconds(30));
            }
        }

        /// <summary>Bu numaradaki açık (kapanmamış) en son oturum: (Id, kullanıcı, IP listesi).</summary>
        private Tuple<long, string, string> OpenRow(int sessionId, string user)
        {
            return _db.Read(c =>
            {
                using (var cmd = c.Create(@"SELECT Id, UserName, Ips FROM RdpSessions WHERE SessionId = @sid AND State IN (@a, @d)
                                            AND (@u IS NULL OR UserName = @u COLLATE NOCASE) ORDER BY Id DESC LIMIT 1",
                    ("@sid", sessionId), ("@a", StateActive), ("@d", StateDisconnected), ("@u", user)))
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? Tuple.Create(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)) : null;
            });
        }

        /// <summary>
        /// Açık görünen ama artık var olmayan oturumları "bitiş bilinmiyor" ile kapatır (kapanış olayı kaçmış: sunucu kapandı vb.).
        /// En fazla 10 dakikada bir; olay toplayıcı kaldığı yeri okuduktan sonra anlamlı olduğu için açılıştan 2 dk sonra başlar.
        /// </summary>
        public void Reconcile(bool force = false)
        {
            if (!force && DateTime.UtcNow - _lastReconcileUtc < TimeSpan.FromMinutes(10)) return;
            _lastReconcileUtc = DateTime.UtcNow;
            HashSet<int> live;
            try
            {
                live = LiveSessions();
            }
            catch (Exception ex)
            {
                AppLog.Debug(Module, "Oturumlar okunamadı: " + ex.Message);
                return;
            }
            string cutoff = DbTime.ToDb(DateTime.UtcNow.AddMinutes(-5));
            lock (_lock)
            {
                var stale = _db.Read(c =>
                {
                    var list = new List<Tuple<long, int>>();
                    using (var cmd = c.Create("SELECT Id, SessionId FROM RdpSessions WHERE State IN (@a, @d) AND LastChangeUtc < @cut",
                        ("@a", StateActive), ("@d", StateDisconnected), ("@cut", cutoff)))
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) list.Add(Tuple.Create(r.GetInt64(0), r.GetInt32(1)));
                    return list;
                }).Where(s => !live.Contains(s.Item2)).ToList();
                if (stale.Count == 0) return;
                _db.Writer.EnqueueAsync(c =>
                {
                    foreach (var s in stale) c.Execute("UPDATE RdpSessions SET State = @s WHERE Id = @id", ("@s", StateLost), ("@id", s.Item1));
                }).Wait(TimeSpan.FromSeconds(30));
                AppLog.Info(Module, stale.Count + " oturum kapanışı kaçırılmış (sunucu yeniden başlamış olabilir): bitiş bilinmiyor olarak kapatıldı.");
            }
        }

        // ================================================================ Liste

        public RdpSessionResult List(RdpSessionQuery q)
        {
            q = q ?? new RdpSessionQuery();
            string from = Time(q.FromUtc, DateTime.UtcNow.AddDays(-7));
            string to = Time(q.ToUtc, DateTime.UtcNow.AddMinutes(1));
            string user = string.IsNullOrWhiteSpace(q.User) ? null : "%" + q.User.Trim().Replace("%", "").Replace("_", "") + "%";
            var result = new RdpSessionResult { WorkHours = GeneralText() };
            var now = DateTime.UtcNow;
            _db.Read(c =>
            {
                // Aralıkla kesişen oturumlar: aralıkta başlayan veya aralık başında hâlâ süren
                using (var cmd = c.Create(@"SELECT Id, UserName, ClientIp, Ips, Country, StartUtc, EndUtc, LastChangeUtc, State, Disconnects, Reconnects, OffHours, StartUnknown
                                            FROM RdpSessions
                                            WHERE StartUtc <= @to AND (EndUtc IS NULL OR EndUtc >= @from) AND (State <> @lost OR LastChangeUtc >= @from)
                                              AND (@u IS NULL OR UserName LIKE @u) AND (@off = 0 OR OffHours = 1) AND (@con = 1 OR ClientIp <> @console)
                                            ORDER BY StartUtc DESC LIMIT @max",
                    ("@from", from), ("@to", to), ("@lost", StateLost), ("@u", user), ("@off", q.OnlyOffHours ? 1 : 0),
                    ("@con", q.IncludeConsole ? 1 : 0), ("@console", Console), ("@max", MaxRows + 1)))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        string state = r.GetString(8);
                        DateTime start = DbTime.FromDb(r.GetString(5));
                        DateTime? end = r.IsDBNull(6) ? (DateTime?)null : DbTime.FromDb(r.GetString(6));
                        DateTime until = end ?? (state == StateLost ? DbTime.FromDb(r.GetString(7)) : now);
                        result.Rows.Add(new RdpSessionDto
                        {
                            Id = r.GetInt64(0),
                            User = r.GetString(1),
                            ClientIp = r.IsDBNull(2) ? null : r.GetString(2),
                            Ips = r.IsDBNull(3) ? null : r.GetString(3),
                            Country = r.IsDBNull(4) ? null : r.GetString(4),
                            StartUtc = r.GetString(5),
                            EndUtc = r.IsDBNull(6) ? null : r.GetString(6),
                            State = state == StateActive ? "Açık" : state == StateDisconnected ? "Bağlantı kopuk" : state == StateEnded ? "Kapandı" : "Kapandı (bitiş bilinmiyor)",
                            Disconnects = r.GetInt32(9),
                            Reconnects = r.GetInt32(10),
                            OffHours = r.GetInt32(11) != 0,
                            StartUnknown = r.GetInt32(12) != 0,
                            DurationSeconds = (long)Math.Max(0, (until - start).TotalSeconds)
                        });
                    }
                return 0;
            });
            if (result.Rows.Count > MaxRows)
            {
                result.Rows.RemoveAt(MaxRows);
                result.Truncated = true;
            }
            return result;
        }

        private static string Time(string text, DateTime fallback)
        {
            if (string.IsNullOrEmpty(text)) return DbTime.ToDb(fallback);
            try
            {
                return DbTime.ToDb(UtcTime.Parse(text));
            }
            catch (FormatException)
            {
                throw new GuardException(ErrorCodes.Validation, "Geçersiz tarih.");
            }
        }

        // ================================================================ Mesai saatleri

        /// <summary>Giriş mesai dışıysa açıklama ("Cumartesi 03:12; mesai Hafta içi 08:00-19:00"), içindeyse / uyarı kapalıysa null.</summary>
        public string OffHoursReason(string user, DateTime timeUtc)
        {
            if (!GeneralSettings.GetBool(_settings, SettingKeys.WorkHoursAlarm)) return null;
            var local = timeUtc.ToLocalTime();
            var ex = FindException(user);
            if (ex != null && ex.Mode == "always") return null;

            ICollection<int> days;
            TimeSpan start, end;
            string schedule;
            if (ex != null)
            {
                days = ex.Days ?? new List<int>();
                start = ParseTime(ex.Start, new TimeSpan(8, 0, 0));
                end = ParseTime(ex.End, new TimeSpan(19, 0, 0));
                schedule = "kişiye özel: " + DaysText(days) + " " + ex.Start + "-" + ex.End;
            }
            else
            {
                days = GeneralDays();
                start = GeneralSettings.GetTime(_settings, SettingKeys.WorkStart);
                end = GeneralSettings.GetTime(_settings, SettingKeys.WorkEnd);
                schedule = GeneralText();
            }
            if (IsWorkTime(local, days, start, end)) return null;
            return local.ToString("dddd HH:mm", CultureInfo.GetCultureInfo("tr-TR")) + " (mesai: " + schedule + ")";
        }

        /// <summary>Gece vardiyasında (bitiş &lt; başlangıç) gece yarısından sonraki kısım önceki günün vardiyası sayılır.</summary>
        internal static bool IsWorkTime(DateTime local, ICollection<int> days, TimeSpan start, TimeSpan end)
        {
            var t = local.TimeOfDay;
            if (start < end) return days.Contains((int)local.DayOfWeek) && t >= start && t < end;
            if (t >= start) return days.Contains((int)local.DayOfWeek);
            if (t < end) return days.Contains((int)local.AddDays(-1).DayOfWeek);
            return false;
        }

        private List<int> GeneralDays()
        {
            switch (GeneralSettings.GetChoice(_settings, SettingKeys.WorkDays))
            {
                case "all": return new List<int> { 0, 1, 2, 3, 4, 5, 6 };
                case "saturday": return new List<int> { 1, 2, 3, 4, 5, 6 };
                default: return new List<int> { 1, 2, 3, 4, 5 };
            }
        }

        public string GeneralText()
        {
            return DaysText(GeneralDays()) + " " + GeneralSettings.GetTime(_settings, SettingKeys.WorkStart).ToString(@"hh\:mm")
                   + "-" + GeneralSettings.GetTime(_settings, SettingKeys.WorkEnd).ToString(@"hh\:mm");
        }

        private static string DaysText(ICollection<int> days)
        {
            var set = new HashSet<int>(days);
            if (set.SetEquals(new[] { 1, 2, 3, 4, 5 })) return "Hafta içi";
            if (set.SetEquals(new[] { 1, 2, 3, 4, 5, 6 })) return "Pzt-Cmt";
            if (set.Count == 7) return "Her gün";
            var names = new[] { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" };
            return string.Join(",", new[] { 1, 2, 3, 4, 5, 6, 0 }.Where(set.Contains).Select(d => names[d]));
        }

        private static TimeSpan ParseTime(string text, TimeSpan fallback)
        {
            TimeSpan t;
            return TimeSpan.TryParseExact(text ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out t) ? t : fallback;
        }

        private WorkHoursExceptionDto FindException(string user)
        {
            if (string.IsNullOrEmpty(user)) return null;
            string sam = user.Contains("\\") ? user.Substring(user.LastIndexOf('\\') + 1) : user;
            return LoadExceptions().FirstOrDefault(e => string.Equals(e.User, user, StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(e.User, sam, StringComparison.OrdinalIgnoreCase));
        }

        private List<WorkHoursExceptionDto> LoadExceptions()
        {
            try
            {
                return PipeProtocol.FromJson<List<WorkHoursExceptionDto>>(_settings.Get(SettingKeys.WorkHoursExceptions)) ?? new List<WorkHoursExceptionDto>();
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Mesai istisnaları okunamadı.", ex);
                return new List<WorkHoursExceptionDto>();
            }
        }

        public WorkHoursDto GetWorkHours()
        {
            return new WorkHoursDto
            {
                Exceptions = LoadExceptions(),
                General = GeneralText(),
                AlarmEnabled = GeneralSettings.GetBool(_settings, SettingKeys.WorkHoursAlarm),
                KnownUsers = _db.Read(c =>
                {
                    var list = new List<string>();
                    using (var cmd = c.Create("SELECT DISTINCT UserName FROM RdpSessions ORDER BY UserName"))
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) list.Add(r.GetString(0));
                    return list;
                })
            };
        }

        public async Task<WorkHoursDto> SaveWorkHoursAsync(WorkHoursDto dto, string userName)
        {
            var list = new List<WorkHoursExceptionDto>();
            foreach (var e in (dto == null ? null : dto.Exceptions) ?? new List<WorkHoursExceptionDto>())
            {
                string u = (e.User ?? "").Trim();
                if (u.Length == 0) continue;
                if (list.Any(x => string.Equals(x.User, u, StringComparison.OrdinalIgnoreCase)))
                    throw new GuardException(ErrorCodes.Validation, u + " iki kez yazılmış.");
                string mode = e.Mode == "custom" ? "custom" : "always";
                if (mode == "custom")
                {
                    TimeSpan a, b;
                    if (!TimeSpan.TryParseExact(e.Start ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out a) || !TimeSpan.TryParseExact(e.End ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out b))
                        throw new GuardException(ErrorCodes.Validation, u + ": saatleri SS:dd biçiminde yazın (örn. 07:30).");
                    if (e.Days == null || e.Days.Count == 0) throw new GuardException(ErrorCodes.Validation, u + ": en az bir gün seçin.");
                }
                list.Add(new WorkHoursExceptionDto
                {
                    User = u,
                    Mode = mode,
                    Days = mode == "custom" ? e.Days.Distinct().Where(d => d >= 0 && d <= 6).OrderBy(d => d).ToList() : new List<int>(),
                    Start = mode == "custom" ? e.Start : null,
                    End = mode == "custom" ? e.End : null,
                    Note = string.IsNullOrWhiteSpace(e.Note) ? null : e.Note.Trim()
                });
            }
            await _settings.SetAsync(SettingKeys.WorkHoursExceptions, PipeProtocol.ToJson(list)).ConfigureAwait(false);
            AuditTrail.Record(_db, userName, "Mesai istisnaları kaydedildi",
                list.Count == 0 ? "istisna yok" : string.Join("; ", list.Select(e => e.User + ": " + (e.Mode == "always" ? "her zaman serbest" : DaysText(e.Days) + " " + e.Start + "-" + e.End))));
            return GetWorkHours();
        }

        // ================================================================ Ülke

        /// <summary>Olay mailleri için ülke metni ("Türkiye (TR)", "iç ağ"...).</summary>
        public string CountryText(string ip)
        {
            return CountryOf(ip);
        }

        private string CountryOf(string ip)
        {
            IPAddress a;
            if (!IpRange.TryParseAddress(ip, out a)) return null;
            if (IPAddress.IsLoopback(a)) return "localhost";
            foreach (var lan in PortRestrictions.LanRanges)
            {
                IpRange r; string err;
                if (IpRange.TryParse(lan, out r, out err, allowWide: true) && r.Contains(a)) return "iç ağ";
            }
            try
            {
                string cc = _geo == null ? null : _geo.CountryOf(a);
                return cc == null ? null : GeoIpDatabase.CountryName(cc) + " (" + cc + ")";
            }
            catch (Exception ex)
            {
                AppLog.Debug(Module, "Ülke bulunamadı: " + ex.Message);
                return null;
            }
        }
    }
}
