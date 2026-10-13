using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Net;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Firewall;
using SecurityGuardService.Engine.Rules;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Engine.Reports
{
    /// <summary>
    /// Aylık yönetici raporu: her ayın 1'inde (Genel Ayarlar → Aylık rapor saati) önceki ayın sade dille özeti.
    /// Saldırılar (protokol / ülke / en çok saldıran IP'ler), önemli olaylar, RDP kullanımı, yedek / bakım, sunucu sağlığı,
    /// çözülmemiş güvenlik riskleri ve önceki ayla karşılaştırma. Alıcılar: Olay Kuralları → "Aylık yönetici raporu".
    /// Servis 1'inde kapalıysa açılınca gönderilir. Reports tablosunda Kind = 'monthly' (son 24 ay saklanır).
    /// </summary>
    internal sealed class MonthlyReport : IDisposable
    {
        private const string Module = "AylıkRapor";
        public const string Kind = "monthly";
        private const int KeepReports = 24;
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        private readonly GuardContext _g;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _lock = new object();
        private Thread _thread;

        public MonthlyReport(GuardContext g)
        {
            _g = g;
        }

        public void Start()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "MonthlyReport", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        public void Dispose()
        {
            _cts.Cancel();
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(5));
        }

        private void Loop()
        {
            if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(4))) return;
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var month = new DateTime(now.Year, now.Month, 1);
                    var due = month + GeneralSettings.GetTime(_g.Settings, SettingKeys.ReportMonthlyTime);
                    string key = month.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    if (LicenseGate.Active && now >= due && _g.Settings.Get(SettingKeys.ReportLastMonthly) != key)
                    {
                        var report = Create(month.AddMonths(-1), now - due > TimeSpan.FromHours(2) ? "zamanlanmış (gecikmeli)" : "zamanlanmış");
                        _g.Settings.SetAsync(SettingKeys.ReportLastMonthly, key).Wait(TimeSpan.FromSeconds(30));
                        AppLog.Info(Module, "Aylık rapor hazırlandı: " + report.Subject + (report.Mailed ? " (mail kuyrukta)" : " (" + report.MailNote + ")"));
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Aylık rapor hazırlanamadı.", ex);
                }
                if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(5))) return;
            }
        }

        /// <summary>Önceki ay (veya bu ay şimdiye kadar) için önizleme; kaydedilmez.</summary>
        public ReportDto Preview(bool currentMonth)
        {
            var month = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            return Build(currentMonth ? month : month.AddMonths(-1), "önizleme");
        }

        public ReportDto SendNow(string userName)
        {
            var month = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-1);
            var report = Create(month, "elle (" + userName + ")");
            AuditTrail.Record(_g.Database, userName, "Aylık rapor elle gönderildi", report.Subject + (report.Mailed ? "" : " (" + report.MailNote + ")"));
            return report;
        }

        private ReportDto Create(DateTime monthLocal, string trigger)
        {
            lock (_lock)
            {
                var report = Build(monthLocal, trigger);
                var def = EventCatalog.Find(EventCatalog.MonthlyReport);
                var rule = _g.Rules.Get(EventCatalog.MonthlyReport);
                bool monitor = rule == null || rule.Monitor;
                bool mail = rule == null ? def.DefaultMail : rule.SendMail;
                if (!monitor) report.MailNote = "Olay Kuralları'nda \"" + def.Name + "\" izleme kapalı";
                else if (!mail) report.MailNote = "Olay Kuralları'nda \"" + def.Name + "\" için mail kapalı";
                else if (!_g.Mail.IsConfigured) report.MailNote = "Mail ayarı yapılmamış";
                else
                {
                    report.Mailed = _g.Mail.EnqueueForEvent(EventCatalog.MonthlyReport, rule == null ? null : rule.RecipientIds, report.Subject, report.Html);
                    if (!report.Mailed) report.MailNote = "Mail kuyruğa eklenemedi (servis logu)";
                }
                report.Id = _g.Database.Writer.EnqueueAsync(c =>
                {
                    c.Execute(@"INSERT INTO Reports (CreatedUtc, PeriodFromUtc, PeriodToUtc, Subject, TriggerText, Mailed, MailNote, Html, Kind)
                                VALUES (@c, @f, @t, @s, @tr, @m, @n, @h, @k)",
                        ("@c", report.CreatedUtc), ("@f", report.PeriodFromUtc), ("@t", report.PeriodToUtc), ("@s", report.Subject),
                        ("@tr", report.Trigger), ("@m", report.Mailed ? 1 : 0), ("@n", report.MailNote), ("@h", report.Html), ("@k", Kind));
                    long id = c.Scalar<long>("SELECT last_insert_rowid()");
                    c.Execute("DELETE FROM Reports WHERE Kind = @k AND Id NOT IN (SELECT Id FROM Reports WHERE Kind = @k ORDER BY Id DESC LIMIT @n)", ("@k", Kind), ("@n", KeepReports));
                    return id;
                }).Result;
                report.Kind = Kind;
                return report;
            }
        }

        internal ReportDto Build(DateTime monthLocal, string trigger)
        {
            var from = monthLocal.ToUniversalTime();
            var to = (monthLocal.AddMonths(1) < DateTime.Now ? monthLocal.AddMonths(1) : DateTime.Now).ToUniversalTime();
            var b = new Builder(_g, from, to, monthLocal);
            string html = b.Html();
            return new ReportDto
            {
                Kind = Kind,
                CreatedUtc = DbTime.Now(),
                PeriodFromUtc = DbTime.ToDb(from),
                PeriodToUtc = DbTime.ToDb(to),
                Trigger = trigger,
                Subject = "[SecurityGuard] Aylık yönetici raporu - " + Environment.MachineName + " - " + monthLocal.ToString("MMMM yyyy", Tr) + ": " + b.Verdict,
                Html = html
            };
        }

        // ================================================================ İçerik

        private sealed class Builder
        {
            private const string Red = "#c62828", Orange = "#ef6c00", Green = "#2e7d32", Gray = "#666";

            private readonly GuardContext _g;
            private readonly DateTime _from, _to, _month;
            private readonly string _f, _t, _pf, _pt;
            private readonly Stats _now, _prev;
            private readonly List<string> _bad = new List<string>(), _warn = new List<string>(), _good = new List<string>();

            public Builder(GuardContext g, DateTime from, DateTime to, DateTime month)
            {
                _g = g;
                _from = from;
                _to = to;
                _month = month;
                _f = DbTime.ToDb(from);
                _t = DbTime.ToDb(to);
                _pf = DbTime.ToDb(month.AddMonths(-1).ToUniversalTime());
                _pt = _f;
                _now = Stats.Read(g.Database, _f, _t);
                _prev = Stats.Read(g.Database, _pf, _pt);
            }

            public string Verdict { get; private set; }

            public string Html()
            {
                var body = new StringBuilder();
                Section(body, "1. Engellenen saldırılar", Attacks);
                Section(body, "2. Önemli olaylar", Events);
                Section(body, "3. Uzak masaüstü (RDP) kullanımı", Rdp);
                Section(body, "4. Yedekleme ve bakım", Backups);
                Section(body, "5. Sunucu sağlığı", Health);
                Section(body, "6. Çözülmemiş güvenlik riskleri", Risks);
                Section(body, "7. Önceki ayla karşılaştırma", Compare);

                Verdict = _bad.Count > 0 ? "Kötü" : _warn.Count > 0 ? "Dikkat" : "İyi";
                string color = _bad.Count > 0 ? Red : _warn.Count > 0 ? Orange : Green;
                string summary = _month.ToString("MMMM yyyy", Tr) + ": " + N(_now.Failures) + " saldırı denemesi engellendi"
                                 + (_now.Bans > 0 ? " (" + N(_now.Bans) + " IP kalıcı olarak engellendi)" : "")
                                 + ", " + N(_now.Critical) + " kritik olay" + (_now.BackupOk + _now.BackupFailed > 0 ? ", " + N(_now.BackupOk) + " başarılı yedek" : "") + ".";

                var sb = new StringBuilder();
                sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222;max-width:900px\">");
                sb.Append("<h2 style=\"margin:0 0 4px 0\">Aylık güvenlik raporu — ").Append(H(_month.ToString("MMMM yyyy", Tr))).Append("</h2>");
                sb.Append("<p style=\"margin:0 0 4px 0;color:").Append(Gray).Append("\">Sunucu ").Append(H(Environment.MachineName)).Append(" · ")
                  .Append(H(_from.ToLocalTime().ToString("dd.MM.yyyy", Tr))).Append(" - ").Append(H(_to.ToLocalTime().AddSeconds(-1).ToString("dd.MM.yyyy", Tr))).Append("</p>");
                sb.Append("<div style=\"margin:10px 0 16px 0;padding:10px 14px;background:#f5f5f5;border-left:6px solid ").Append(color).Append("\">");
                sb.Append("<div style=\"font-size:18px;font-weight:700;color:").Append(color).Append("\">Genel durum: ").Append(H(Verdict)).Append("</div>");
                sb.Append("<div style=\"margin-top:4px\">").Append(H(summary)).Append("</div>");
                if (_bad.Count + _warn.Count > 0)
                {
                    sb.Append("<ul style=\"margin:6px 0 0 18px;padding:0\">");
                    foreach (var x in _bad) sb.Append("<li style=\"color:").Append(Red).Append("\">").Append(H(x)).Append("</li>");
                    foreach (var x in _warn) sb.Append("<li style=\"color:").Append(Orange).Append("\">").Append(H(x)).Append("</li>");
                    sb.Append("</ul>");
                }
                else if (_good.Count > 0) sb.Append("<div style=\"margin-top:4px;color:").Append(Green).Append("\">").Append(H(string.Join(" ", _good))).Append("</div>");
                sb.Append("</div>");
                sb.Append(body);
                sb.Append("<p style=\"margin:18px 0 0 0;color:").Append(Gray).Append(";font-size:12px\">Bu rapor SecurityGuard tarafından otomatik hazırlandı. Ayrıntılar SecurityGuard arayüzünde; günlük teknik rapor IT ekibine ayrıca gider. ")
                  .Append("Ülke bilgisi: IP Geolocation by DB-IP (db-ip.com).</p></div>");
                return sb.ToString();
            }

            private void Section(StringBuilder body, string title, Action<StringBuilder> fill)
            {
                body.Append("<h3 style=\"margin:22px 0 6px 0;border-bottom:1px solid #ddd;padding-bottom:4px\">").Append(H(title)).Append("</h3>");
                try
                {
                    fill(body);
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Rapor bölümü hazırlanamadı: " + title, ex);
                    body.Append(P("Bu bölüm hazırlanamadı: " + ex.Message, Red));
                }
            }

            // ------------------------------------------------ 1. Saldırılar

            private static readonly Dictionary<string, string> Protocols = new Dictionary<string, string>
            {
                { EventCatalog.RdpLoginFailed, "Uzak masaüstü (RDP)" }, { EventCatalog.SqlLoginFailed, "SQL Server" },
                { EventCatalog.NetLoginFailed, "Dosya paylaşımı / uzaktan yönetim (SMB, WinRM)" }, { EventCatalog.FtpLoginFailed, "FTP" },
                { EventCatalog.WebLoginFailed, "Web (IIS)" }, { EventCatalog.SmtpLoginFailed, "Mail (SMTP)" }
            };

            private void Attacks(StringBuilder sb)
            {
                var rows = Rows("SELECT EventType, IpAddress, COUNT(*) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND EventType LIKE '%.LoginFailed' GROUP BY EventType, IpAddress");
                if (rows.Count == 0)
                {
                    sb.Append(P("Bu ay dışarıdan şifre deneme saldırısı görülmedi.", Green));
                    _good.Add("Saldırı denemesi görülmedi.");
                    return;
                }
                var banned = new HashSet<string>(Rows("SELECT IpAddress FROM BannedIps").Select(r => Convert.ToString(r[0])), StringComparer.OrdinalIgnoreCase);
                sb.Append(P("Sunucuya şifre tahmin ederek girmeye çalışan " + N(rows.Select(r => Convert.ToString(r[1])).Distinct().Count()) + " farklı adresten toplam "
                            + N(_now.Failures) + " deneme yapıldı; " + N(_now.Bans) + " adres eşiği aşınca otomatik olarak kalıcı engellendi. Hiçbiri başarılı olmadı"
                            + (_now.SuccessAfterFailures > 0 ? " — ANCAK " + _now.SuccessAfterFailures + " kez deneme sonrası başarılı giriş görüldü (aşağıda)" : "") + ".", null));
                if (_now.SuccessAfterFailures > 0) _bad.Add(_now.SuccessAfterFailures + " kez başarısız denemelerden sonra başarılı giriş (hesap ele geçirilmiş olabilir).");

                var byProto = new Table("Saldırılan hizmet", "Deneme");
                foreach (var g in rows.GroupBy(r => Convert.ToString(r[0])).OrderByDescending(g => g.Sum(r => Convert.ToInt64(r[2]))))
                {
                    string name;
                    byProto.Add(H(Protocols.TryGetValue(g.Key, out name) ? name : g.Key), N(g.Sum(r => Convert.ToInt64(r[2]))));
                }
                sb.Append(byProto);

                var perIp = rows.GroupBy(r => Convert.ToString(r[1])).Select(g => new { Ip = g.Key, Count = g.Sum(r => Convert.ToInt64(r[2])), Types = g.Select(r => Convert.ToString(r[0])).ToList() })
                    .OrderByDescending(x => x.Count).ToList();
                var byCountry = perIp.GroupBy(x => Country(x.Ip)).Select(g => new { Country = g.Key, Count = g.Sum(x => x.Count), Ips = g.Count() }).OrderByDescending(x => x.Count).ToList();
                var countries = new Table("Ülke", "Deneme", "Adres");
                foreach (var c in byCountry.Take(10)) countries.Add(H(c.Country), N(c.Count), N(c.Ips));
                sb.Append(P("Saldırıların geldiği ülkeler:", null)).Append(countries);

                var top = new Table("Adres", "Ülke", "Deneme", "Hizmet", "Durum");
                foreach (var x in perIp.Take(10))
                    top.Add(H(x.Ip), H(Country(x.Ip)), N(x.Count), H(string.Join(", ", x.Types.Select(t => t.Replace(".LoginFailed", "").Replace("Net", "SMB").ToUpperInvariant().Replace("SMTP", "SMTP")).Distinct())),
                        banned.Contains(x.Ip) ? Bold("engellendi", Green) : IsLocal(x.Ip) ? "iç ağ (engellenmez)" : "engellenmedi");
                sb.Append(P("En çok saldıran 10 adres:", null)).Append(top);
                // "İnternetten hedef alınıyor" sonucu yalnızca gerçekten dış IP'lerden yoğun deneme varsa yazılır:
                // tüm denemeler iç ağdan / localhost'tan geliyorsa (ör. yanlış kayıtlı şifreyle hizmete vuran bir uygulama) bu bir internet saldırısı değildir.
                long externalFailures = perIp.Where(x => !IsLocal(x.Ip)).Sum(x => x.Count);
                if (externalFailures > 1000) _warn.Add("Ay boyunca dış IP'lerden " + N(externalFailures) + " şifre deneme saldırısı: sunucu internetten hedef alınıyor (engellendi).");
                else if (_now.Failures > 1000) _warn.Add("Ay boyunca " + N(_now.Failures) + " başarısız giriş denemesi (çoğu iç ağdan / sunucunun kendisinden): yanlış kayıtlı şifreyle bir hizmete / uygulamaya vuruluyor olabilir.");
            }

            private string Country(string ip)
            {
                IPAddress a;
                if (ip == null || !IpRange.TryParseAddress(ip, out a)) return "bilinmiyor";
                if (IsLocal(ip)) return "İç ağ / sunucunun kendisi";
                string cc = _g.Geo == null ? null : _g.Geo.CountryOf(a);
                return cc == null ? "bilinmiyor" : GeoIpDatabase.CountryName(cc);
            }

            private static bool IsLocal(string ip)
            {
                IPAddress a;
                if (ip == null || !IpRange.TryParseAddress(ip, out a)) return false;
                if (IPAddress.IsLoopback(a)) return true;
                foreach (var lan in PortRestrictions.LanRanges)
                {
                    IpRange r; string err;
                    if (IpRange.TryParse(lan, out r, out err, allowWide: true) && r.Contains(a)) return true;
                }
                return false;
            }

            // ------------------------------------------------ 2. Önemli olaylar

            private static readonly string[] Alarming = { "Win.CanaryTouched", "Win.RansomwarePrecursor", "Win.RemoteToolInstalled", "Guard.LogIntegrity", "Guard.BanRuleTampered",
                                                          "Guard.PortRuleTampered", "Guard.ServiceTamper", "Guard.DataTamper", "Win.EventLogCleared", "Win.AdminGroupAdded" };

            private void Events(StringBuilder sb)
            {
                var rows = Rows("SELECT EventType, COUNT(*), MAX(Summary) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND Severity >= 2 AND EventType NOT LIKE '%.LoginFailed' AND EventType <> 'Ban.IpBanned' GROUP BY EventType ORDER BY COUNT(*) DESC");
                if (rows.Count == 0)
                {
                    sb.Append(P("Bu ay kritik bir olay yaşanmadı.", Green));
                    _good.Add("Kritik olay yok.");
                }
                else
                {
                    var table = new Table("Olay", "Kez", "Son örnek");
                    foreach (var r in rows.Take(15))
                    {
                        string type = Convert.ToString(r[0]);
                        bool alarming = Alarming.Contains(type);
                        table.Add(alarming ? Bold(Name(type), Red) : H(Name(type)), N(Convert.ToInt64(r[1])), H(Short(Convert.ToString(r[2]), 140)));
                        if (alarming) _bad.Add(Name(type) + " (" + r[1] + " kez).");
                    }
                    sb.Append(P("Kritik olaylar (ayrıntılar SecurityGuard → Olay Logu):", null)).Append(table);
                }
                var notable = new List<string>();
                Action<string, string> add = (type, text) =>
                {
                    long n = Count("SELECT COUNT(*) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND EventType = @x", ("@x", type));
                    if (n > 0) notable.Add(text + ": " + n);
                };
                add("Win.SoftwareInstalled", "Yeni program kurulumu");
                add("Win.SoftwareRemoved", "Kaldırılan program");
                add("Win.UsbStorage", "Sunucuya takılan USB disk");
                add("Rdp.LoginNewIp", "Daha önce görülmemiş adresten RDP girişi");
                add("Win.UserCreated", "Yeni Windows kullanıcısı");
                add("Sql.LoginChanged", "SQL kullanıcı değişikliği");
                if (notable.Count > 0) sb.Append(P("Bilgi amaçlı: " + string.Join(" · ", notable) + ".", Gray));
            }

            // ------------------------------------------------ 3. RDP

            private void Rdp(StringBuilder sb)
            {
                var rows = Rows(@"SELECT UserName, StartUtc, EndUtc, LastChangeUtc, State, OffHours, Country FROM RdpSessions
                                  WHERE StartUtc < @t AND (EndUtc IS NULL OR EndUtc >= @f) AND ClientIp <> 'konsol'");
                if (rows.Count == 0)
                {
                    sb.Append(P("Bu ay RDP ile bağlanan olmadı (veya oturum geçmişi henüz tutulmuyordu).", Gray));
                    return;
                }
                var users = rows.GroupBy(r => Convert.ToString(r[0])).Select(g => new
                {
                    User = g.Key,
                    Sessions = g.Count(),
                    Hours = g.Sum(r => Clip(r)),
                    Off = g.Count(r => Convert.ToInt64(r[5]) != 0),
                    Countries = string.Join(", ", g.Select(r => r[6] as string).Where(c => !string.IsNullOrEmpty(c)).Distinct())
                }).OrderByDescending(u => u.Hours).ToList();
                var table = new Table("Kullanıcı", "Oturum", "Toplam süre", "Mesai dışı", "Nereden");
                foreach (var u in users)
                    table.Add(H(u.User), N(u.Sessions), H(Hours(u.Hours)), u.Off > 0 ? Bold(u.Off.ToString(), Orange) : "0", H(u.Countries));
                sb.Append(P(users.Count + " kişi toplam " + Hours(users.Sum(u => u.Hours)) + " uzak masaüstü kullandı" + (_now.OffHours > 0 ? "; " + _now.OffHours + " giriş mesai dışında yapıldı" : "") + ".", null)).Append(table);
                if (_now.OffHours > 0) _warn.Add(_now.OffHours + " mesai dışı RDP girişi (kişiler ve saatler RDP Oturumları sayfasında).");
                var foreign = rows.Select(r => r[6] as string).Where(c => !string.IsNullOrEmpty(c) && c != "iç ağ" && c != "localhost" && !c.StartsWith("Türkiye")).Distinct().ToList();
                if (foreign.Count > 0) _warn.Add("Yurt dışından RDP girişi: " + string.Join(", ", foreign) + ".");
            }

            private double Clip(object[] r)
            {
                DateTime start = DbTime.FromDb(Convert.ToString(r[1]));
                DateTime end = r[2] is string ? DbTime.FromDb((string)r[2]) : Convert.ToString(r[4]) == "lost" ? DbTime.FromDb(Convert.ToString(r[3])) : DateTime.UtcNow;
                if (start < _from) start = _from;
                if (end > _to) end = _to;
                return Math.Max(0, (end - start).TotalHours);
            }

            // ------------------------------------------------ 4. Yedek / bakım

            private void Backups(StringBuilder sb)
            {
                if (_now.BackupOk + _now.BackupFailed == 0)
                {
                    bool enabled = _g.Backup.LoadConfig().Enabled;
                    sb.Append(P(enabled ? "Bu ay SecurityGuard ile yedek alınmadı!" : "SecurityGuard yedeklemesi kullanılmıyor (yedekler başka araçla alınıyor olabilir).", enabled ? Red : Gray));
                    if (enabled) _bad.Add("Bu ay hiç yedek alınmadı.");
                }
                else
                {
                    string last = Convert.ToString(Rows("SELECT MAX(FinishedUtc) FROM BackupRuns WHERE Status = 'Tamamlandı'")[0][0]);
                    sb.Append(P("Yedekleme: " + N(_now.BackupOk) + " başarılı, " + (_now.BackupFailed > 0 ? Bold(N(_now.BackupFailed) + " hatalı", Red) : "0 hatalı")
                                + ". Son başarılı yedek: " + (string.IsNullOrEmpty(last) ? "yok" : Local(last)) + ".", null, true));
                    if (_now.BackupFailed > 0) (_now.BackupOk == 0 ? _bad : _warn).Add(_now.BackupFailed + " yedekleme hatalı bitti.");
                }
                var maint = Rows("SELECT Status, COUNT(*) FROM MaintenanceRuns WHERE DryRun = 0 AND StartedUtc >= @f AND StartedUtc < @t GROUP BY Status");
                if (maint.Count > 0)
                    sb.Append(P("Veritabanı bakımı: " + string.Join(", ", maint.Select(r => r[1] + " " + Convert.ToString(r[0]).ToLower(Tr))) + ".", null));
            }

            // ------------------------------------------------ 5. Sağlık

            private void Health(StringBuilder sb)
            {
                var res = Rows("SELECT AVG(Cpu), MAX(Cpu), AVG(Ram), MAX(Ram), COUNT(*) FROM ResourceSamples WHERE TimeUtc >= @f AND TimeUtc < @t");
                var table = new Table("Konu", "Durum");
                if (res.Count > 0 && res[0][4] != null && Convert.ToInt64(res[0][4]) > 0)
                {
                    table.Add("İşlemci", "ortalama %" + (int)Convert.ToDouble(res[0][0]) + ", en yüksek %" + (int)Convert.ToDouble(res[0][1]));
                    table.Add("Bellek", "ortalama %" + (int)Convert.ToDouble(res[0][2]) + ", en yüksek %" + (int)Convert.ToDouble(res[0][3]));
                    if (Convert.ToDouble(res[0][0]) > 70) _warn.Add("İşlemci ay boyunca ortalama %" + (int)Convert.ToDouble(res[0][0]) + " dolu: sunucu kapasitesi sınırda.");
                }
                foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                {
                    double used = 100.0 * (d.TotalSize - d.TotalFreeSpace) / Math.Max(1, d.TotalSize);
                    table.Add("Disk " + H(d.Name.TrimEnd('\\')), "%" + (int)used + " dolu, " + (d.TotalFreeSpace / 1073741824.0).ToString("0", Tr) + " GB boş" + (used >= 90 ? " — " + Bold("yer açılmalı", Red) : ""));
                    if (used >= 90) _warn.Add("Disk " + d.Name.TrimEnd('\\') + " %" + (int)used + " dolu.");
                }
                var stops = Rows("SELECT Summary, COUNT(*) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND EventType = 'Svc.Stopped' GROUP BY Summary ORDER BY COUNT(*) DESC");
                table.Add("Servis kesintileri", stops.Count == 0 ? "yok" : string.Join("; ", stops.Take(5).Select(r => H(Convert.ToString(r[0]).Replace("İzlenen servis durdu: ", "")) + " (" + r[1] + ")")));
                if (stops.Count > 0) _warn.Add("İzlenen servislerde " + stops.Sum(r => Convert.ToInt64(r[1])) + " kesinti.");
                sb.Append(table);
            }

            // ------------------------------------------------ 6. Riskler

            private void Risks(StringBuilder sb)
            {
                var run = _g.Checks.Latest(SecurityGuard.Shared.Dto.CheckScopes.Security);
                if (run == null || run.Items == null || run.Items.Count == 0)
                {
                    sb.Append(P("Güvenlik taraması henüz yapılmadı.", Gray));
                    return;
                }
                var open = run.Items.Where(i => !i.IsIgnored && (i.Status == CheckStatus.Kritik || i.Status == CheckStatus.Uyari))
                    .OrderBy(i => i.Status == CheckStatus.Kritik ? 0 : 1).ToList();
                if (open.Count == 0)
                {
                    sb.Append(P("Güvenlik taramasında açık risk yok.", Green));
                    _good.Add("Açık güvenlik riski yok.");
                    return;
                }
                int critical = open.Count(i => i.Status == CheckStatus.Kritik);
                sb.Append(P("Son güvenlik taramasında (" + Local(run.FinishedUtc ?? run.StartedUtc) + ") " + critical + " kritik, " + (open.Count - critical)
                            + " uyarı seviyesinde çözülmemiş madde var. Bunlar IT ekibinin kararıyla düzeltilir:", null));
                var table = new Table("Önem", "Konu", "Durum");
                foreach (var i in open.Take(15))
                    table.Add(i.Status == CheckStatus.Kritik ? Bold("Kritik", Red) : Bold("Uyarı", Orange), H(i.Title), H(Short(i.Summary, 160)));
                sb.Append(table);
                if (critical > 0) _warn.Add(critical + " kritik güvenlik önerisi hâlâ uygulanmadı.");
            }

            // ------------------------------------------------ 7. Karşılaştırma

            private void Compare(StringBuilder sb)
            {
                var table = new Table("", _month.ToString("MMMM", Tr), _month.AddMonths(-1).ToString("MMMM", Tr), "Değişim");
                Action<string, double, double, bool> row = (label, a, b, lowerIsBetter) =>
                {
                    string change = b == 0 ? (a == 0 ? "-" : "yeni") : ((a - b) / b * 100).ToString("+0;-0;0", Tr) + "%";
                    string color = a == b ? null : (a < b) == lowerIsBetter ? Green : Orange;
                    table.Add(H(label), N(a), N(b), color == null ? H(change) : Bold(change, color));
                };
                row("Saldırı denemesi", _now.Failures, _prev.Failures, true);
                row("Engellenen adres", _now.Bans, _prev.Bans, true);
                row("Kritik olay", _now.Critical, _prev.Critical, true);
                row("Mesai dışı RDP girişi", _now.OffHours, _prev.OffHours, true);
                row("Servis kesintisi", _now.ServiceStops, _prev.ServiceStops, true);
                row("Başarılı yedek", _now.BackupOk, _prev.BackupOk, false);
                sb.Append(table);
            }

            // ------------------------------------------------ Yardımcılar

            private List<object[]> Rows(string sql, params (string Name, object Value)[] extra)
            {
                var args = new List<(string, object)> { ("@f", _f), ("@t", _t) };
                args.AddRange(extra);
                return _g.Database.Read(c =>
                {
                    var list = new List<object[]>();
                    using (var cmd = c.Create(sql, args.ToArray()))
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            var v = new object[r.FieldCount];
                            for (int i = 0; i < v.Length; i++) v[i] = r.IsDBNull(i) ? null : r.GetValue(i);
                            list.Add(v);
                        }
                    return list;
                });
            }

            private long Count(string sql, params (string Name, object Value)[] extra)
            {
                var r = Rows(sql, extra);
                return r.Count == 0 || r[0][0] == null ? 0 : Convert.ToInt64(r[0][0]);
            }

            private static string Name(string type)
            {
                var d = EventCatalog.Find(type);
                return d == null ? type : d.Name;
            }

            private static string Hours(double h)
            {
                return h >= 1 ? h.ToString("0.#", Tr) + " saat" : Math.Max(1, (int)(h * 60)) + " dakika";
            }

            private static string Local(string utc)
            {
                try
                {
                    return DbTime.FromDb(utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm", Tr);
                }
                catch (Exception)
                {
                    return utc;
                }
            }

            private static string N(double v)
            {
                return v.ToString("N0", Tr);
            }

            private static string Short(string s, int max)
            {
                return s == null || s.Length <= max ? s : s.Substring(0, max) + "…";
            }

            private static string H(string s)
            {
                return WebUtility.HtmlEncode(s ?? "");
            }

            private static string Bold(string text, string color)
            {
                return "<b" + (color == null ? "" : " style=\"color:" + color + "\"") + ">" + H(text) + "</b>";
            }

            private static string P(string text, string color, bool html = false)
            {
                return "<p style=\"margin:4px 0" + (color == null ? "" : ";color:" + color) + "\">" + (html ? text : H(text)) + "</p>";
            }

            private sealed class Table
            {
                private readonly StringBuilder _sb = new StringBuilder();

                public Table(params string[] headers)
                {
                    _sb.Append("<table cellpadding=\"5\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px;margin:4px 0 8px 0\"><tr style=\"background:#f2f2f2\">");
                    foreach (var h in headers) _sb.Append("<th align=\"left\">").Append(H(h)).Append("</th>");
                    _sb.Append("</tr>");
                }

                public void Add(params string[] cells)
                {
                    _sb.Append("<tr>");
                    foreach (var c in cells) _sb.Append("<td style=\"border-bottom:1px solid #eee;vertical-align:top\">").Append(c ?? "").Append("</td>");
                    _sb.Append("</tr>");
                }

                public override string ToString()
                {
                    return _sb.Append("</table>").ToString();
                }
            }
        }

        /// <summary>Karşılaştırma için tek sorguyla sayılan ay değerleri.</summary>
        private sealed class Stats
        {
            public long Failures, Bans, Critical, OffHours, ServiceStops, BackupOk, BackupFailed, SuccessAfterFailures;

            public static Stats Read(GuardDatabase db, string from, string to)
            {
                return db.Read(c =>
                {
                    Func<string, long> n = sql => c.Scalar<long>(sql, ("@f", from), ("@t", to));
                    return new Stats
                    {
                        Failures = n("SELECT COUNT(*) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND EventType LIKE '%.LoginFailed'"),
                        Bans = n("SELECT COUNT(*) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND EventType = 'Ban.IpBanned'"),
                        Critical = n("SELECT COUNT(*) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND Severity >= 2 AND EventType NOT LIKE '%.LoginFailed' AND EventType <> 'Ban.IpBanned'"),
                        OffHours = n("SELECT COUNT(*) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND EventType = 'Rdp.OffHoursLogin'"),
                        ServiceStops = n("SELECT COUNT(*) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND EventType = 'Svc.Stopped'"),
                        BackupOk = n("SELECT COUNT(*) FROM BackupRuns WHERE StartedUtc >= @f AND StartedUtc < @t AND Status = 'Tamamlandı'"),
                        BackupFailed = n("SELECT COUNT(*) FROM BackupRuns WHERE StartedUtc >= @f AND StartedUtc < @t AND Status <> 'Tamamlandı' AND FinishedUtc IS NOT NULL"),
                        SuccessAfterFailures = n("SELECT COUNT(*) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND EventType = 'Login.SuccessAfterFailures'")
                    };
                });
            }
        }
    }
}
