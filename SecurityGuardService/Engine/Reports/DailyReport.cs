using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Checks;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Ban;
using SecurityGuardService.Engine.Events;
using SecurityGuardService.Engine.Rules;
using SecurityGuardService.Mail;
using SecurityGuardService.Sql;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Engine.Reports
{
    /// <summary>
    /// Günlük rapor maili (Genel Ayarlar → Rapor saati, varsayılan 20:00): tek mailde
    /// güvenlik olayları, banlar, performans taraması özeti, SQL bakımı ve servis sağlığı.
    /// - Rapor aynı saatte başlayan performans taramasının bitmesini bekler (en fazla 45 dk).
    /// - Dönem: bir önceki zamanlanmış rapordan bu yana (ilk raporda / uzun aradan sonra en fazla 7 gün).
    /// - Servis o saatte kapalıysa aynı gün açılınca gönderilir.
    /// - Her rapor kaydedilir (son 90), arayüzden geçmiş raporlara bakılabilir.
    /// - Otomatik bütünlük kontrolü: olay loguna dışarıdan müdahale bulunursa ayrıca Kritik olay (aynı sorun her gün tekrarlanmaz).
    /// - Alıcılar ve açık / kapalı: Olay Kuralları → "Günlük güvenlik ve performans raporu" kuralı.
    /// </summary>
    internal sealed class DailyReport : IDisposable
    {
        private const string Module = "Rapor";
        private const int KeepReports = 90;
        private static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(7);
        private static readonly TimeSpan ScanWait = TimeSpan.FromMinutes(45);
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");


        private readonly GuardContext _g;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _sendLock = new object();
        private Thread _thread;

        public DailyReport(GuardContext guard)
        {
            _g = guard;
        }

        public void Start()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "DailyReport", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        // ================================================================ Zamanlama

        private void Loop()
        {
            if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(3))) return;
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var due = now.Date + GeneralSettings.GetTime(_g.Settings, SettingKeys.ReportDailyTime);
                    string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (LicenseGate.Active && now >= due && _g.Settings.Get(SettingKeys.ReportLastDate) != today && ScanSettled(now - due))
                    {
                        var to = DateTime.UtcNow;
                        var report = Create(PeriodStart(to), to, now - due > TimeSpan.FromMinutes(60) ? "zamanlanmış (kaçırılan " + due.ToString("HH:mm") + " raporu)" : "zamanlanmış", true);
                        _g.Settings.SetAsync(SettingKeys.ReportLastDate, today).Wait(TimeSpan.FromSeconds(30));
                        _g.Settings.SetAsync(SettingKeys.ReportLastPeriodEnd, DbTime.ToDb(to)).Wait(TimeSpan.FromSeconds(30));
                        AppLog.Info(Module, "Günlük rapor hazırlandı: " + report.Subject + (report.Mailed ? " (mail kuyrukta)" : " (" + report.MailNote + ")"));
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Günlük rapor hazırlanamadı.", ex);
                }
                if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1))) return;
            }
        }

        /// <summary>Aynı saatte başlayan performans taraması bitti mi (bitmediyse en fazla 45 dk beklenir).</summary>
        private bool ScanSettled(TimeSpan sinceDue)
        {
            if (sinceDue >= ScanWait) return true;
            var scopes = new[] { CheckScopes.Security, CheckScopes.Performance };
            if (scopes.Any(s => _g.Checks.IsRunning(s))) return false;
            // Tarama zamanlayıcısı dakikada bir bakar: bugün başlatılmadıysa ilk 5 dk beklenir
            return scopes.All(s => _g.Checks.ScheduledToday(s)) || sinceDue >= TimeSpan.FromMinutes(5);
        }

        private DateTime PeriodStart(DateTime to)
        {
            string last = _g.Settings.Get(SettingKeys.ReportLastPeriodEnd);
            DateTime from;
            try
            {
                from = string.IsNullOrEmpty(last) ? to.AddDays(-1) : DbTime.FromDb(last);
            }
            catch (FormatException)
            {
                from = to.AddDays(-1);
            }
            if (to - from > MaxPeriod) from = to - MaxPeriod;
            if (from >= to) from = to.AddDays(-1);
            return from;
        }

        // ================================================================ UI

        public ReportDto Preview()
        {
            var to = DateTime.UtcNow;
            return Build(PeriodStart(to), to, "önizleme", false);
        }

        public ReportDto SendNow(string userName)
        {
            var to = DateTime.UtcNow;
            var report = Create(PeriodStart(to), to, "elle (" + userName + ")", false);
            AuditTrail.Record(_g.Database, userName, "Günlük rapor elle gönderildi", report.Subject + (report.Mailed ? "" : " (" + report.MailNote + ")"));
            return report;
        }

        public List<ReportDto> List()
        {
            return _g.Database.Read(c =>
            {
                var list = new List<ReportDto>();
                using (var cmd = c.Create("SELECT Id, CreatedUtc, PeriodFromUtc, PeriodToUtc, Subject, TriggerText, Mailed, MailNote, Kind FROM Reports ORDER BY Id DESC LIMIT 150"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(ReadRow(r));
                }
                return list;
            });
        }

        public ReportDto Get(long id)
        {
            var dto = _g.Database.Read(c =>
            {
                using (var cmd = c.Create("SELECT Id, CreatedUtc, PeriodFromUtc, PeriodToUtc, Subject, TriggerText, Mailed, MailNote, Kind, Html FROM Reports WHERE Id = @id", ("@id", id)))
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    var d = ReadRow(r);
                    d.Html = r.GetString(9);
                    return d;
                }
            });
            if (dto == null) throw new GuardException(ErrorCodes.InvalidRequest, "Rapor bulunamadı.");
            return dto;
        }

        private static ReportDto ReadRow(System.Data.SQLite.SQLiteDataReader r)
        {
            return new ReportDto
            {
                Id = r.GetInt64(0),
                CreatedUtc = r.GetString(1),
                PeriodFromUtc = r.GetString(2),
                PeriodToUtc = r.GetString(3),
                Subject = r.GetString(4),
                Trigger = r.IsDBNull(5) ? null : r.GetString(5),
                Mailed = r.GetInt64(6) != 0,
                MailNote = r.IsDBNull(7) ? null : r.GetString(7),
                Kind = r.IsDBNull(8) ? "daily" : r.GetString(8)
            };
        }

        // ================================================================ Üret / kaydet / gönder

        private ReportDto Create(DateTime from, DateTime to, string trigger, bool scheduled)
        {
            lock (_sendLock)
            {
                var report = Build(from, to, trigger, scheduled);

                var def = EventCatalog.Find(EventCatalog.DailyReport);
                var rule = _g.Rules.Get(EventCatalog.DailyReport);
                bool monitor = rule == null || rule.Monitor;
                bool mail = rule == null ? def.DefaultMail : rule.SendMail;
                if (!monitor) report.MailNote = "Olay Kuralları'nda \"" + def.Name + "\" izleme kapalı";
                else if (!mail) report.MailNote = "Olay Kuralları'nda \"" + def.Name + "\" için mail kapalı";
                else if (!_g.Mail.IsConfigured) report.MailNote = "Mail ayarı yapılmamış";
                else
                {
                    report.Mailed = _g.Mail.EnqueueForEvent(EventCatalog.DailyReport, rule == null ? null : rule.RecipientIds, report.Subject, report.Html);
                    if (!report.Mailed) report.MailNote = "Mail kuyruğa eklenemedi (servis logu)";
                }

                report.Id = _g.Database.Writer.EnqueueAsync(c =>
                {
                    c.Execute(@"INSERT INTO Reports (CreatedUtc, PeriodFromUtc, PeriodToUtc, Subject, TriggerText, Mailed, MailNote, Html)
                                VALUES (@c, @f, @t, @s, @tr, @m, @n, @h)",
                        ("@c", report.CreatedUtc), ("@f", report.PeriodFromUtc), ("@t", report.PeriodToUtc), ("@s", report.Subject),
                        ("@tr", report.Trigger), ("@m", report.Mailed ? 1 : 0), ("@n", report.MailNote), ("@h", report.Html));
                    long id = c.Scalar<long>("SELECT last_insert_rowid()");
                    c.Execute("DELETE FROM Reports WHERE Kind = 'daily' AND Id NOT IN (SELECT Id FROM Reports WHERE Kind = 'daily' ORDER BY Id DESC LIMIT @k)", ("@k", KeepReports));
                    return id;
                }).Result;
                return report;
            }
        }

        private ReportDto Build(DateTime from, DateTime to, string trigger, bool scheduled)
        {
            var b = new Builder(_g, from, to, scheduled);
            string html = b.Html();
            return new ReportDto
            {
                CreatedUtc = DbTime.Now(),
                PeriodFromUtc = DbTime.ToDb(from),
                PeriodToUtc = DbTime.ToDb(to),
                Trigger = trigger,
                Subject = "[SecurityGuard] Günlük rapor - " + Environment.MachineName + " - " + DateTime.Now.ToString("dd.MM.yyyy", Tr) + ": " + b.Headline,
                Html = html
            };
        }

        public void Dispose()
        {
            _cts.Cancel();
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(5));
        }

        // ================================================================ HTML

        /// <summary>Rapor içeriği. Her bölüm ayrı hesaplanır: biri hata verirse rapor yine çıkar, o bölümde hata yazar.</summary>
        private sealed class Builder
        {
            private const string Red = "#c62828", Orange = "#ef6c00", Green = "#2e7d32", Blue = "#1565c0", Gray = "#666";

            private readonly GuardContext _g;
            private readonly DateTime _from, _to;
            private readonly bool _scheduled;
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly List<string> _headline = new List<string>();
            private string _fromDb, _toDb;

            public Builder(GuardContext g, DateTime from, DateTime to, bool scheduled)
            {
                _g = g;
                _from = from;
                _to = to;
                _scheduled = scheduled;
                _fromDb = DbTime.ToDb(from);
                _toDb = DbTime.ToDb(to);
            }

            public string Headline
            {
                get
                {
                    if (_headline.Count == 0) return "sorun yok";
                    return string.Join(", ", _headline.GroupBy(h => h).Select(g => g.Count() > 1 ? g.Count() + " " + g.Key : g.Key));
                }
            }

            public string Html()
            {
                var body = new StringBuilder();
                Section(body, "Güvenlik olayları", SecurityEvents);
                Section(body, "Giriş denemeleri ve banlar", Logins);
                Section(body, "Güvenlik önerileri", s => Checks(s, CheckScopes.Security, "Güvenlik Önerileri", "güvenlik"));
                Section(body, "Performans önerileri", s => Checks(s, CheckScopes.Performance, "Performans Önerileri", "performans"));
                Section(body, "SQL yedekleri", Backups);
                Section(body, "SQL bakımı", Maintenance);
                Section(body, "Servis sağlığı", Health);

                var sb = new StringBuilder();
                sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222\">");
                sb.Append("<h2 style=\"margin:0 0 4px 0\">Günlük güvenlik ve performans raporu</h2>");
                sb.Append("<p style=\"margin:0 0 4px 0;color:").Append(Gray).Append("\">").Append(H(Environment.MachineName)).Append(" · ")
                  .Append(H(_from.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Tr))).Append(" - ").Append(H(_to.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Tr))).Append("</p>");
                string color = _headline.Any(h => h.Contains("kritik") || h.Contains("oynama")) ? Red : _headline.Count > 0 ? Orange : Green;
                sb.Append("<p style=\"margin:8px 0 16px 0;padding:8px 12px;background:#f5f5f5;border-left:4px solid ").Append(color).Append(";font-weight:600\">")
                  .Append(H(_headline.Count == 0 ? "Sorun yok: dönem içinde kritik olay veya kritik bulgu yok." : "Özet: " + Headline + ".")).Append("</p>");
                sb.Append(body);
                sb.Append("<p style=\"margin:16px 0 0 0;color:").Append(Gray).Append(";font-size:12px\">Ayrıntılar SecurityGuard arayüzünde. ")
                  .Append("SecurityGuard sunucuda hiçbir ayarı kendiliğinden değiştirmez; öneriler sizin onayınızla uygulanır.</p></div>");
                return sb.ToString();
            }

            private void Section(StringBuilder body, string title, Action<StringBuilder> fill)
            {
                body.Append("<h3 style=\"margin:20px 0 6px 0;border-bottom:1px solid #ddd;padding-bottom:4px\">").Append(H(title)).Append("</h3>");
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

            // ------------------------------------------------ Güvenlik

            private void SecurityEvents(StringBuilder sb)
            {
                var counts = Rows("SELECT Severity, COUNT(*) FROM EventLog WHERE TimeUtc >= @f AND TimeUtc < @t AND EventType <> @r GROUP BY Severity",
                    ("@r", EventCatalog.DailyReport));
                long crit = counts.Where(r => Convert.ToInt32(r[0]) == (int)Severity.Kritik).Sum(r => Convert.ToInt64(r[1]));
                long warn = counts.Where(r => Convert.ToInt32(r[0]) == (int)Severity.Uyari).Sum(r => Convert.ToInt64(r[1]));
                long info = counts.Where(r => Convert.ToInt32(r[0]) == (int)Severity.Bilgi).Sum(r => Convert.ToInt64(r[1]));
                if (crit > 0) _headline.Add(crit + " kritik olay");

                sb.Append(P(Bold(crit + " kritik", crit > 0 ? Red : null) + ", " + Bold(warn + " uyarı", warn > 0 ? Orange : null) + ", " + info + " bilgi olayı.", null, true));

                var byType = Rows(@"SELECT EventType, MAX(Severity), COUNT(*), MAX(TimeUtc) FROM EventLog
                                    WHERE TimeUtc >= @f AND TimeUtc < @t AND Severity >= 1 AND EventType <> @r
                                    GROUP BY EventType ORDER BY MAX(Severity) DESC, COUNT(*) DESC LIMIT 20", ("@r", EventCatalog.DailyReport));
                if (byType.Count > 0)
                {
                    var table = new Table("Önem", "Olay", "Adet", "Son");
                    foreach (var r in byType)
                    {
                        int sev = Convert.ToInt32(r[1]);
                        table.Add(SevCell(sev), H(Name(Convert.ToString(r[0]))), Convert.ToString(r[2]), H(Local(Convert.ToString(r[3]))));
                    }
                    sb.Append(table);
                }

                var critical = Rows(@"SELECT TimeUtc, EventType, Summary, UserName, IpAddress FROM EventLog
                                      WHERE TimeUtc >= @f AND TimeUtc < @t AND Severity = 2 ORDER BY Id DESC LIMIT 30");
                if (critical.Count > 0)
                {
                    sb.Append(P("Kritik olaylar" + (crit > critical.Count ? " (son " + critical.Count + ")" : "") + ":", null));
                    var table = new Table("Zaman", "Olay", "Özet", "Kullanıcı / IP");
                    foreach (var r in critical)
                        table.Add(H(Local(Convert.ToString(r[0]))), H(Name(Convert.ToString(r[1]))), H(Convert.ToString(r[2])),
                            H(string.Join(" / ", new[] { r[3] as string, r[4] as string }.Where(x => !string.IsNullOrEmpty(x)))));
                    sb.Append(table);
                }
            }

            private void Logins(StringBuilder sb)
            {
                // Tüm protokoller: RDP, SQL, SMB / WinRM, FTP, Web, SMTP
                var fails = Rows(@"SELECT IpAddress, COUNT(*), MAX(TimeUtc), MAX(UserName), GROUP_CONCAT(DISTINCT REPLACE(EventType, '.LoginFailed', '')) FROM EventLog
                                   WHERE TimeUtc >= @f AND TimeUtc < @t AND EventType LIKE '%.LoginFailed'
                                   GROUP BY IpAddress ORDER BY COUNT(*) DESC");
                long total = fails.Sum(r => Convert.ToInt64(r[1]));
                // Dönemdeki banlar olay logundan sayılır: sonradan kaldırılan ban da o gün yapılmış bir bandır
                // (sadece BannedIps'e bakılırsa "IP banlandı" olay sayısıyla tutmaz). Olayı loga yazılmayan ban tablodan eklenir.
                var bans = Rows(@"SELECT e.IpAddress, e.Source, e.Summary, e.TimeUtc, CASE WHEN b.IpAddress IS NULL THEN 0 ELSE 1 END
                                  FROM EventLog e LEFT JOIN BannedIps b ON b.IpAddress = e.IpAddress
                                  WHERE e.TimeUtc >= @f AND e.TimeUtc < @t AND e.EventType = 'Ban.IpBanned'
                                  UNION ALL
                                  SELECT b.IpAddress, CASE WHEN b.IsManual = 1 THEN 'Elle ban' ELSE 'Ban motoru' END, b.Reason, b.BannedUtc, 1
                                  FROM BannedIps b
                                  WHERE b.BannedUtc >= @f AND b.BannedUtc < @t
                                    AND NOT EXISTS (SELECT 1 FROM EventLog e WHERE e.TimeUtc >= @f AND e.TimeUtc < @t AND e.EventType = 'Ban.IpBanned' AND e.IpAddress = b.IpAddress)
                                  ORDER BY 4 DESC");
                long active = _g.Database.Read(c => c.Scalar<long>("SELECT COUNT(*) FROM BannedIps"));
                int lifted = bans.Count(r => Convert.ToInt64(r[4]) == 0);

                sb.Append(P(total + " başarısız giriş denemesi (" + fails.Count + " farklı IP), " + Bold(bans.Count + " yeni ban", bans.Count > 0 ? Orange : null)
                            + (lifted > 0 ? " (" + lifted + " tanesi sonradan kaldırıldı)" : "") + ", şu an toplam " + active + " banlı IP.", null, true));
                if (fails.Count > 0)
                {
                    var table = new Table("IP", "Deneme", "Protokol", "Son", "Son kullanıcı adı");
                    foreach (var r in fails.Take(10))
                        table.Add(H(Convert.ToString(r[0] ?? "(IP yok)")), Convert.ToString(r[1]), H((r[4] as string ?? "").Replace("Net", "SMB").ToUpperInvariant()), H(Local(Convert.ToString(r[2]))), H(r[3] as string));
                    sb.Append(P("En çok deneyen IP'ler:", null)).Append(table);
                }
                if (bans.Count > 0)
                {
                    var table = new Table("IP", "Kaynak", "Sebep", "Zaman", "Durum");
                    foreach (var r in bans.Take(30))
                    {
                        string ip = Convert.ToString(r[0]);
                        string reason = Convert.ToString(r[2]);
                        string prefix = ip + " banlandı: "; // olay özeti IP ile başlar; tabloda IP ayrı sütunda
                        if (reason.StartsWith(prefix, StringComparison.Ordinal)) reason = reason.Substring(prefix.Length);
                        table.Add(H(ip), H(Convert.ToString(r[1])), H(reason), H(Local(Convert.ToString(r[3]))), Convert.ToInt64(r[4]) == 1 ? "banlı" : "kaldırıldı");
                    }
                    sb.Append(P("Yeni banlar" + (bans.Count > 30 ? " (son 30)" : "") + ":", null)).Append(table);
                }
            }

            // ------------------------------------------------ Performans

            private void Checks(StringBuilder sb, string scope, string page, string kind)
            {
                var run = _g.Checks.Latest(scope);
                if (run.RunId == null)
                {
                    sb.Append(P("Henüz " + kind + " taraması yapılmadı.", Gray));
                    return;
                }
                var started = DbTime.FromDb(run.StartedUtc);
                bool stale = DateTime.UtcNow - started > TimeSpan.FromHours(30);
                var items = run.Items.Where(x => !x.IsIgnored).ToList();
                int crit = items.Count(x => x.Status == CheckStatus.Kritik), warn = items.Count(x => x.Status == CheckStatus.Uyari);
                int fresh = items.Count(x => x.IsNew);
                if (crit > 0) _headline.Add(crit + " kritik " + kind + " bulgusu");
                if (fresh > 0) _headline.Add(fresh + " yeni " + kind + " bulgusu");

                sb.Append(P("Tarama: " + Local(run.StartedUtc) + (stale ? " (bugün tarama yapılmadı, son tarama gösteriliyor)" : "") + ". "
                            + Bold(crit + " kritik", crit > 0 ? Red : null) + ", " + Bold(warn + " uyarı", warn > 0 ? Orange : null) + ", "
                            + items.Count(x => x.Status == CheckStatus.Bilgi) + " bilgi, " + items.Count(x => x.Status == CheckStatus.Gecti) + " geçti"
                            + (items.Any(x => x.Status == CheckStatus.Hata) ? ", " + items.Count(x => x.Status == CheckStatus.Hata) + " kontrol edilemedi" : "")
                            + (run.Items.Count > items.Count ? " (" + (run.Items.Count - items.Count) + " madde görmezden geliniyor)" : "") + ".", null, true));

                var problems = items.Where(x => x.Status == CheckStatus.Kritik || x.Status == CheckStatus.Uyari)
                    .OrderByDescending(x => x.Status).ThenByDescending(x => x.IsNew).ToList();
                if (problems.Count > 0)
                {
                    var table = new Table("Durum", "Nerede", "Konu", "Sonuç");
                    foreach (var x in problems.Take(30))
                        table.Add(Bold(x.Status == CheckStatus.Kritik ? "Kritik" : "Uyarı", x.Status == CheckStatus.Kritik ? Red : Orange) + (x.IsNew ? " <b>YENİ</b>" : ""),
                            H(x.Area), H(x.Title), H(x.Summary));
                    sb.Append(table);
                    sb.Append(P("Nasıl düzeltileceği ve hazır script'ler: SecurityGuard → Öneriler → " + page + ".", Gray));
                }
            }

            // ------------------------------------------------ Yedek

            private void Backups(StringBuilder sb)
            {
                var cfg = _g.Backup.LoadConfig();
                var runs = Rows("SELECT Instance, StartedUtc, Status, Summary, TriggerText FROM BackupRuns WHERE StartedUtc >= @f AND StartedUtc < @t ORDER BY StartedUtc");
                if (!cfg.Enabled && runs.Count == 0)
                {
                    sb.Append(P("SecurityGuard yedeklemesi kapalı. (Başka bir araçla yedek alınıyorsa durumu Güvenlik Önerileri → Yedekler maddesinde görünür.)", Gray));
                    return;
                }
                if (runs.Count == 0)
                {
                    sb.Append(P("Dönem içinde yedek alınmadı" + (cfg.Enabled ? " (planlanan günler: " + string.Join(", ", cfg.Days.Select(d => new[] { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" }[d])) + " " + cfg.StartTime + ")." : "."), Gray));
                }
                else
                {
                    var table = new Table("Başlangıç", "Instance", "Durum", "Özet");
                    foreach (var r in runs)
                    {
                        string status = Convert.ToString(r[2]);
                        bool bad = status != "Tamamlandı" && status != "Sürüyor";
                        if (bad) _headline.Add("yedek " + status.ToLowerInvariant());
                        table.Add(H(Local(Convert.ToString(r[1]))), H(Convert.ToString(r[0])), Bold(status, bad ? Red : Green), H(Convert.ToString(r[3])));
                    }
                    sb.Append(table);
                }
                // Seçili DB'lerden 2 günden uzun süredir başarılı SecurityGuard yedeği olmayanlar
                if (cfg.Enabled)
                {
                    var last = Rows(@"SELECT r.Instance || '|' || f.DatabaseName, MAX(f.TimeUtc) FROM BackupFiles f JOIN BackupRuns r ON r.RunId = f.RunId
                                      WHERE f.Step = 'ARŞİV' AND f.Result = 'Tamam' AND f.TimeUtc < @t AND f.TimeUtc >= '0' AND @f IS NOT NULL GROUP BY r.Instance, f.DatabaseName")
                        .ToDictionary(r => Convert.ToString(r[0]), r => Convert.ToString(r[1]), StringComparer.OrdinalIgnoreCase);
                    var stale = cfg.Databases.Where(d =>
                    {
                        string t;
                        return !last.TryGetValue(d, out t) || DbTime.FromDb(t) < DateTime.UtcNow.AddDays(-2);
                    }).ToList();
                    if (stale.Count > 0)
                    {
                        _headline.Add(stale.Count + " DB'nin güncel yedeği yok");
                        sb.Append(P("2 günden uzun süredir başarılı yedeği olmayan: " + string.Join(", ", stale.Select(s => s.Replace("|", " / "))) + ".", Red));
                    }
                }
            }

            // ------------------------------------------------ Bakım

            private void Maintenance(StringBuilder sb)
            {
                var cfg = _g.Maintenance.LoadConfig();
                var runs = Rows("SELECT Instance, DryRun, StartedUtc, Status, Summary, TriggerText FROM MaintenanceRuns WHERE StartedUtc >= @f AND StartedUtc < @t ORDER BY StartedUtc");
                string state = !cfg.Enabled ? "Bakım kapalı." : "Bakım açık" + (cfg.DryRun ? " (deneme modu: hiçbir şey uygulanmıyor)" : "") + ".";
                if (runs.Count == 0)
                {
                    sb.Append(P(state + " Dönem içinde bakım çalışmadı.", Gray));
                    return;
                }
                sb.Append(P(state, null));
                var table = new Table("Başlangıç", "Instance", "Tür", "Durum", "Özet");
                foreach (var r in runs)
                {
                    string status = Convert.ToString(r[3]);
                    bool bad = status == "Hatalı" || status == "Yarıda kesildi";
                    if (bad) _headline.Add("bakım " + status.ToLowerInvariant());
                    table.Add(H(Local(Convert.ToString(r[2]))), H(Convert.ToString(r[0])), Convert.ToInt64(r[1]) == 1 ? "Deneme" : "Uygulama",
                        Bold(status, bad ? Red : status == "Süre doldu" ? Orange : Green), H(Convert.ToString(r[4])));
                }
                sb.Append(table);
            }

            // ------------------------------------------------ Sağlık

            private void Health(StringBuilder sb)
            {
                var table = new Table("Konu", "Durum");

                var asm = Assembly.GetExecutingAssembly();
                var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
                string running = uptime.TotalDays >= 1 ? (int)uptime.TotalDays + " gün " + uptime.Hours + " saat"
                               : uptime.TotalHours >= 1 ? uptime.Hours + " saat " + uptime.Minutes + " dakika"
                               : Math.Max(1, uptime.Minutes) + " dakika";
                table.Add("Servis", H("Sürüm " + asm.GetName().Version + ", çalışma süresi " + running));

                // Olay logu bütünlüğü (otomatik kontrol)
                var v = _g.Events.Verify();
                string integrity;
                if (!v.ChainEnabled) integrity = Bold("Kontrol edilemedi: imza anahtarı okunamıyor", Orange);
                else if (v.IsIntact)
                    integrity = Bold("Sağlam", Green) + " (" + v.CheckedRows + " kayıt"
                                + (v.AdminDeletedRows + v.AdminModifiedRows > 0 ? "; yönetici SQLite ekranından " + v.AdminDeletedRows + " kayıt sildi, " + v.AdminModifiedRows + " kayıt değiştirdi" : "")
                                + (v.AcceptedRows > 0 ? "; " + v.AcceptedRows + " eski sorun yönetici tarafından kabul edildi" : "") + ")";
                else
                {
                    integrity = Bold("OYNAMA TESPİT EDİLDİ", Red) + ": " + v.ModifiedRows + " değişmiş kayıt, " + v.Gaps + " kopukluk"
                                + (v.TailDeletedRows > 0 ? ", sondan " + v.TailDeletedRows + " kayıt silinmiş" : "") + ". Bu değişiklikler SecurityGuard arayüzünden yapılmadı.";
                    _headline.Add("olay logunda oynama");
                }
                if (_scheduled) ReportIntegrity(v);
                table.Add("Olay logu bütünlüğü", integrity);

                // Mail kuyruğu
                var mail = Rows("SELECT Status, COUNT(*) FROM MailQueue WHERE CreatedUtc >= @f AND CreatedUtc < @t GROUP BY Status");
                Func<int, long> m = s => mail.Where(r => Convert.ToInt32(r[0]) == s).Sum(r => Convert.ToInt64(r[1]));
                int pending = _g.Mail.PendingCount();
                long failed = m(2), expired = m(3);
                table.Add("Mail", (_g.Mail.IsConfigured ? "" : Bold("Mail ayarı yapılmamış. ", Orange)) + m(1) + " gönderildi"
                                  + (pending > 0 ? ", " + Bold(pending + " bekliyor", Orange) : "")
                                  + (failed > 0 ? ", " + Bold(failed + " gönderilemedi", Red) : "")
                                  + (expired > 0 ? ", " + Bold(expired + " süresi geçti (özet gönderildi)", Orange) : ""));
                if (failed > 0) _headline.Add(failed + " mail gönderilemedi");

                // İşlemci / bellek (dakikalık geçmişten)
                var res = Rows("SELECT AVG(Cpu), MAX(Cpu), AVG(Ram), MAX(Ram), COUNT(*) FROM ResourceSamples WHERE TimeUtc >= @f AND TimeUtc < @t");
                if (res.Count > 0 && res[0][4] != null && Convert.ToInt64(res[0][4]) > 0)
                {
                    int cpuLimit = GeneralSettings.GetInt(_g.Settings, SettingKeys.CpuAlarmPercent), ramLimit = GeneralSettings.GetInt(_g.Settings, SettingKeys.RamAlarmPercent);
                    long cpuOver = Convert.ToInt64(Rows("SELECT COUNT(*) FROM ResourceSamples WHERE TimeUtc >= @f AND TimeUtc < @t AND Cpu >= @l", ("@l", cpuLimit))[0][0]);
                    long ramOver = Convert.ToInt64(Rows("SELECT COUNT(*) FROM ResourceSamples WHERE TimeUtc >= @f AND TimeUtc < @t AND Ram >= @l", ("@l", ramLimit))[0][0]);
                    table.Add("İşlemci", "ort. %" + (int)Convert.ToDouble(res[0][0]) + ", en yüksek %" + (int)Convert.ToDouble(res[0][1])
                                          + (cpuOver > 0 ? ", " + Bold("%" + cpuLimit + " üstünde " + cpuOver + " dk", Orange) : ""));
                    table.Add("Bellek", "ort. %" + (int)Convert.ToDouble(res[0][2]) + ", en yüksek %" + (int)Convert.ToDouble(res[0][3])
                                         + (ramOver > 0 ? ", " + Bold("%" + ramLimit + " üstünde " + ramOver + " dk", Orange) : ""));
                }

                // Firewall / ban
                var ban = _g.Ban.List();
                table.Add("Firewall ban kuralları", ban.FirewallOk ? Bold("Uyumlu", Green) + " (" + ban.Banned.Count + " banlı IP)" : Bold(ban.FirewallMessage ?? "Kontrol edilemedi", Orange));

                // Port kısıtları (ülke seçimi varsa veritabanı yaşı)
                var ports = _g.PortRestrictions.GetState();
                var portOn = ports.Items.Where(i => i.Enabled).ToList();
                if (portOn.Count > 0)
                    table.Add("Port kısıtları", (ports.FirewallOk ? Bold(portOn.Count + " kısıt uygulanıyor", Green) : Bold(H(ports.FirewallMessage), Orange))
                        + (portOn.Any(i => !string.IsNullOrEmpty(i.Countries)) ? "; " + (ports.GeoWarning != null ? Bold(H(ports.GeoWarning), Orange) : H(ports.GeoInfo)) : ""));

                // Tuzak dosyalar
                var canary = _g.Canary.GetState();
                var canaryOn = canary.Folders.Where(f => f.Enabled).ToList();
                var canaryBad = canaryOn.Where(f => !f.State.StartsWith("İzleniyor", StringComparison.Ordinal)).ToList();
                table.Add("Tuzak dosyalar", canaryOn.Count == 0 ? Bold("İzlenen klasör yok (paylaşım / yedek klasörü bulunamadı)", Orange)
                    : (canaryBad.Count == 0 ? Bold("İzleniyor", Green) : Bold(canaryBad.Count + " klasörde sorun: " + H(string.Join("; ", canaryBad.Select(f => f.Path + " - " + f.State))), Orange))
                      + " (" + canaryOn.Count + " klasör" + (canary.FileAuditEnabled ? "" : "; dosya sistemi denetimi kapalı, mailde kullanıcı / program yazmaz") + ")");

                // SQL izleme
                var instances = _g.Sql.List().Where(i => i.IsConfigured).ToList();
                if (instances.Count == 0) table.Add("SQL Server izleme", Bold("Bağlantı tanımlı değil", Orange));
                foreach (var i in instances)
                {
                    if (!i.IsEnabled)
                    {
                        table.Add("SQL izleme (" + H(i.InstanceName) + ")", Bold("İzleme kapalı", Orange));
                        continue;
                    }
                    var a = _g.SqlAudit.GetState(i.InstanceName);
                    table.Add("SQL izleme (" + H(i.InstanceName) + ")", a.Running ? Bold("Çalışıyor", Green) : Bold(a.Message ?? "Kurulu değil", Orange));
                }

                sb.Append(table);
            }

            /// <summary>Dışarıdan oynama bulunursa Kritik olay; aynı durum her gün tekrar olay üretmez.</summary>
            private void ReportIntegrity(EventLogVerifyResult v)
            {
                string key = v.IsIntact || !v.ChainEnabled ? "" : v.ModifiedRows + "/" + v.Gaps + "/" + v.TailDeletedRows;
                if (key == (_g.Settings.Get(SettingKeys.ReportLastIntegrity) ?? "")) return;
                _g.Settings.SetAsync(SettingKeys.ReportLastIntegrity, key).Wait(TimeSpan.FromSeconds(30));
                if (key.Length == 0) return;
                _g.Events.Record(new GuardEvent("Guard.LogIntegrity",
                        "Olay logunda dışarıdan oynama tespit edildi: " + v.ModifiedRows + " değişmiş kayıt, " + v.Gaps + " kopukluk, sondan " + v.TailDeletedRows + " silinmiş kayıt.")
                    { Source = "Günlük otomatik bütünlük kontrolü" }
                    .With("İlk sorunlu kayıt Id'leri", string.Join(", ", v.ProblemIds))
                    .With("Açıklama", "Bu değişiklikler SecurityGuard arayüzünden (SQLite Sorgu ekranı) yapılmadı: veritabanı dosyasına dışarıdan müdahale edilmiş olabilir."));
            }

            // ------------------------------------------------ Yardımcılar

            private List<object[]> Rows(string sql, params (string Name, object Value)[] extra)
            {
                var args = new List<(string, object)> { ("@f", _fromDb), ("@t", _toDb) };
                args.AddRange(extra);
                return _g.Database.Read(c =>
                {
                    var list = new List<object[]>();
                    using (var cmd = c.Create(sql, args.ToArray()))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var row = new object[r.FieldCount];
                            r.GetValues(row);
                            for (int i = 0; i < row.Length; i++) if (row[i] is DBNull) row[i] = null;
                            list.Add(row);
                        }
                    }
                    return list;
                });
            }

            private static string Name(string type)
            {
                var d = EventCatalog.Find(type);
                return d == null ? type : d.Name;
            }

            private static string Local(string utc)
            {
                try
                {
                    return DbTime.FromDb(utc).ToLocalTime().ToString("dd.MM HH:mm", Tr);
                }
                catch (Exception)
                {
                    return utc;
                }
            }

            private static string SevCell(int severity)
            {
                return severity == (int)Severity.Kritik ? Bold("Kritik", Red) : severity == (int)Severity.Uyari ? Bold("Uyarı", Orange) : Bold("Bilgi", Blue);
            }

            private static string H(string s)
            {
                return WebUtility.HtmlEncode(s ?? "");
            }

            private static string Bold(string text, string color)
            {
                return "<span style=\"font-weight:600" + (color != null ? ";color:" + color : "") + "\">" + H(text) + "</span>";
            }

            /// <param name="html">true: metin zaten HTML (Bold vb. içeriyor).</param>
            private static string P(string text, string color, bool html = false)
            {
                return "<p style=\"margin:4px 0 8px 0" + (color != null ? ";color:" + color : "") + "\">" + (html ? text : H(text)) + "</p>";
            }

            private sealed class Table
            {
                private readonly StringBuilder _sb = new StringBuilder();

                public Table(params string[] headers)
                {
                    _sb.Append("<table cellpadding=\"5\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px;margin:4px 0 10px 0\"><tr>");
                    foreach (var h in headers)
                        _sb.Append("<th style=\"text-align:left;background:#f0f0f0;border-bottom:1px solid #ccc\">").Append(WebUtility.HtmlEncode(h)).Append("</th>");
                    _sb.Append("</tr>");
                }

                /// <summary>Hücreler HTML (çağıran kaçışı yapar).</summary>
                public void Add(params string[] cells)
                {
                    _sb.Append("<tr>");
                    foreach (var c in cells) _sb.Append("<td style=\"border-bottom:1px solid #eee;vertical-align:top\">").Append(c).Append("</td>");
                    _sb.Append("</tr>");
                }

                public override string ToString()
                {
                    return _sb + "</table>";
                }
            }
        }
    }
}
