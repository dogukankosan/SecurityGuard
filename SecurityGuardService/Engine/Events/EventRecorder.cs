using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Security;
using SecurityGuardService.Engine.Rules;
using SecurityGuardService.Mail;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Engine.Events
{
    /// <summary>Bir güvenlik olayı (collector'lar ve servis modülleri üretir).</summary>
    internal sealed class GuardEvent
    {
        public GuardEvent(string eventType, string summary)
        {
            EventType = eventType;
            Summary = summary;
            TimeUtc = DateTime.UtcNow;
        }

        public string EventType { get; private set; }
        public string Summary { get; private set; }
        public DateTime TimeUtc { get; set; }

        /// <summary>Olayın geldiği yer (ör. "Security 4625"); boşsa katalogdaki kaynak.</summary>
        public string Source { get; set; }

        public string UserName { get; set; }
        public string IpAddress { get; set; }

        /// <summary>Ek bilgiler (maile ve Detay alanına satır satır yazılır).</summary>
        public List<KeyValuePair<string, string>> Details { get; } = new List<KeyValuePair<string, string>>();

        public GuardEvent With(string name, object value)
        {
            if (value != null) Details.Add(new KeyValuePair<string, string>(name, Convert.ToString(value, CultureInfo.CurrentCulture)));
            return this;
        }
    }

    /// <summary>
    /// Olayları kurala göre işler: izleme kapalıysa yok sayar, Logla açıksa EventLog'a HMAC zinciriyle yazar,
    /// Mail açıksa kuraldaki alıcılara mail kuyruğa alır. Çağıranı bekletmez (yazma kuyruğu).
    /// </summary>
    internal sealed class EventRecorder
    {
        private const string Module = "Olay";
        private const int MaxProblemIds = 20;

        private readonly GuardDatabase _db;
        private readonly RuleStore _rules;
        private readonly MailService _mail;
        private readonly HmacChain _chain;
        private readonly MailBatcher _batcher;

        /// <summary>Toplama olmadan (her olay hemen mail).</summary>
        public EventRecorder(GuardDatabase db, RuleStore rules, MailService mail, HmacChain chain)
            : this(db, rules, mail, chain, new MailBatcher(mail, () => 0))
        {
        }

        public EventRecorder(GuardDatabase db, RuleStore rules, MailService mail, HmacChain chain, MailBatcher batcher)
        {
            _db = db;
            _rules = rules;
            _mail = mail;
            _chain = chain;
            _batcher = batcher;
        }

        /// <summary>Olay maillerini toplayan bileşen (servis kapanırken bekleyenleri gönderir).</summary>
        public MailBatcher Batcher { get { return _batcher; } }

        /// <summary>Zincir alanları: sıra ve içerik değişirse eski kayıtlar doğrulanamaz; değiştirilmemeli.</summary>
        internal static string[] ChainFields(string timeUtc, string source, string eventType, int severity, string user, string ip, string summary, string detail)
        {
            return new[] { timeUtc, source, eventType, severity.ToString(CultureInfo.InvariantCulture), user, ip, summary, detail };
        }

        public void Record(GuardEvent e)
        {
            if (!LicenseGate.Active && !LicenseGate.AlwaysRecorded(e.EventType)) return; // lisans yok: izleme durdu
            try
            {
                var def = EventCatalog.All.FirstOrDefault(d => d.Type == e.EventType);
                if (def == null)
                {
                    AppLog.Warn(Module, "Katalogda olmayan olay tipi kaydedilmedi: " + e.EventType);
                    return;
                }

                var rule = _rules.Get(e.EventType);
                bool monitor = rule == null || rule.Monitor;
                if (!monitor) return;

                bool writeLog = rule == null || rule.WriteLog;
                bool sendMail = rule == null ? def.DefaultMail : rule.SendMail;
                var severity = rule == null ? def.DefaultSeverity : (Severity)rule.Severity;

                string source = string.IsNullOrEmpty(e.Source) ? def.Source : e.Source;
                string detail = e.Details.Count == 0 ? null : string.Join(Environment.NewLine, e.Details.Select(p => p.Key + ": " + p.Value));

                if (sendMail)
                    _batcher.Add(def, severity, rule == null ? null : rule.RecipientIds, source, e);

                if (writeLog) Write(e, def, severity, source, detail, sendMail);

                AppLog.Info(Module, def.Name + ": " + e.Summary + (sendMail ? " (mail kuyrukta)" : ""));
            }
            catch (Exception ex)
            {
                // Olay kaydı hiçbir modülü düşürmez
                AppLog.Error(Module, "Olay işlenemedi: " + e.EventType, ex);
            }
        }

        private void Write(GuardEvent e, EventDefinition def, Severity severity, string source, string detail, bool mailQueued)
        {
            string time = DbTime.ToDb(e.TimeUtc);
            int sev = (int)severity;

            bool queued = _db.Writer.Enqueue(c =>
            {
                // Önceki imza yazma kuyruğunda okunur: tek yazıcı olduğu için sıra kesin, geri alınan yazma zinciri bozmaz
                string prev = null, hmac = null;
                if (_chain != null)
                {
                    prev = c.Scalar<string>("SELECT Hmac FROM EventLog ORDER BY Id DESC LIMIT 1");
                    hmac = _chain.Compute(prev, ChainFields(time, source, e.EventType, sev, e.UserName, e.IpAddress, e.Summary, detail));
                }
                c.Execute(@"INSERT INTO EventLog (TimeUtc, Source, EventType, Severity, UserName, IpAddress, Summary, Detail, MailSent, PrevHmac, Hmac)
                            VALUES (@t, @src, @type, @sev, @u, @ip, @sum, @d, @m, @p, @h)",
                    ("@t", time), ("@src", source), ("@type", e.EventType), ("@sev", sev), ("@u", e.UserName), ("@ip", e.IpAddress),
                    ("@sum", e.Summary), ("@d", detail), ("@m", mailQueued ? 1 : 0), ("@p", prev), ("@h", hmac));
            });
            if (!queued) AppLog.Error(Module, "Olay loga yazılamadı (kuyruk kapalı): " + def.Name);
        }

        /// <summary>
        /// Zinciri baştan sona doğrular: satır imzası tutmuyorsa içerik değişmiş, önceki imza bağı kopuksa
        /// arada kayıt silinmiş demektir. SQLite ekranından yöneticinin yaptığı ve "mevcut durumu kabul et" ile
        /// onaylanan değişiklikler (imzalı kayıtları var) oynama sayılmaz.
        /// </summary>
        public EventLogVerifyResult Verify()
        {
            var result = Verify(null);
            LastVerify = result;
            LastVerifyUtc = DateTime.UtcNow;
            return result;
        }

        /// <summary>Son bütünlük kontrolünün sonucu (servis açıldığından beri hiç yapılmadıysa null).</summary>
        public EventLogVerifyResult LastVerify { get; private set; }

        public DateTime? LastVerifyUtc { get; private set; }

        /// <param name="unexplained">Doluysa açıklanamayan her sorun (Id, kabul tipi) buraya eklenir.</param>
        private EventLogVerifyResult Verify(List<KeyValuePair<long, string>> unexplained)
        {
            var chain = _chain;
            var result = new EventLogVerifyResult { ChainEnabled = chain != null, ProblemIds = new List<long>() };
            if (chain == null) return result;

            _db.Read(c =>
            {
                var admin = EventLogAdminChanges.LoadVerified(c, chain);
                var adminDeleted = admin[EventLogAdminChanges.Deleted];
                var adminModified = admin[EventLogAdminChanges.Modified];
                var accepted = admin[EventLogAdminChanges.Accepted];
                var acceptedDeleted = admin[EventLogAdminChanges.AcceptedDeleted];
                long lastId = 0;
                Func<long, bool> isDeleted = i => adminDeleted.Contains(i) || acceptedDeleted.Contains(i);
                Func<long, long, bool> explained = (from, to) =>
                {
                    for (long i = from; i <= to; i++)
                        if (!isDeleted(i)) return false;
                    return true;
                };
                // Açıklanan silmeler: SQLite ekranından yönetici silmesi mi, sonradan "kabul edildi" mi ayrı sayılır
                Action<long, long> countDeleted = (from, to) =>
                {
                    for (long i = from; i <= to; i++)
                    {
                        if (adminDeleted.Contains(i)) result.AdminDeletedRows++;
                        else if (acceptedDeleted.Contains(i)) result.AcceptedRows++;
                    }
                };
                Action<long, long> collectDeleted = (from, to) =>
                {
                    if (unexplained == null) return;
                    for (long i = from; i <= to; i++)
                        if (!isDeleted(i)) unexplained.Add(new KeyValuePair<long, string>(i, EventLogAdminChanges.AcceptedDeleted));
                };

                // AUTOINCREMENT sayacı satırlardan ÖNCE okunur: doğrulama sürerken yeni olay yazılırsa (sayaç artar ama satır taramaya
                // girmemiş olabilir) "sondan kayıt silinmiş" diye yanlış alarm çıkmasın. Önce okunan sayaç en fazla taranan son Id kadardır.
                long seq = c.Scalar<long>("SELECT seq FROM sqlite_sequence WHERE name = 'EventLog'");

                using (var cmd = c.Create("SELECT Id, TimeUtc, Source, EventType, Severity, UserName, IpAddress, Summary, Detail, PrevHmac, Hmac FROM EventLog ORDER BY Id"))
                using (var rd = cmd.ExecuteReader())
                {
                    string lastHmac = null;
                    bool first = true;
                    while (rd.Read())
                    {
                        result.CheckedRows++;
                        long id = rd.GetInt64(0);
                        long previousId = lastId;
                        lastId = id;
                        string prev = rd.IsDBNull(9) ? null : rd.GetString(9);
                        string hmac = rd.IsDBNull(10) ? null : rd.GetString(10);

                        if (hmac == null)
                        {
                            result.UnsignedRows++;
                            lastHmac = null;
                            first = false;
                            continue;
                        }

                        var fields = ChainFields(rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4),
                            rd.IsDBNull(5) ? null : rd.GetString(5), rd.IsDBNull(6) ? null : rd.GetString(6),
                            rd.GetString(7), rd.IsDBNull(8) ? null : rd.GetString(8));

                        bool problem = false;
                        if (!chain.Verify(hmac, prev, fields))
                        {
                            if (adminModified.Contains(id)) result.AdminModifiedRows++;
                            else if (accepted.Contains(id)) result.AcceptedRows++;
                            else
                            {
                                result.ModifiedRows++;
                                problem = true;
                                if (unexplained != null) unexplained.Add(new KeyValuePair<long, string>(id, EventLogAdminChanges.Accepted));
                            }
                        }
                        // İlk satırın öncesi boş olmalı; değilse baştaki kayıtlar silinmiş
                        if ((first && prev != null) || (!first && lastHmac != null && prev != lastHmac))
                        {
                            long from = previousId + 1, to = id - 1;
                            if (to >= from && explained(from, to)) countDeleted(from, to);
                            // Arada eksik Id yokken bağ kopuksa (önceki imza alanı değişmiş) sadece kabul edilen satır affedilir.
                            // Eksik Id'ler tek tek kabul edilir: kabul edilmiş satırın önünden sonradan silinen kayıt yine yakalanır.
                            else if (to < from && accepted.Contains(id)) result.AcceptedRows++;
                            else
                            {
                                result.Gaps++;
                                problem = true;
                                if (to >= from) collectDeleted(from, to);
                                else if (unexplained != null) unexplained.Add(new KeyValuePair<long, string>(id, EventLogAdminChanges.Accepted));
                            }
                        }
                        if (problem && result.ProblemIds.Count < MaxProblemIds) result.ProblemIds.Add(id);

                        lastHmac = hmac;
                        first = false;
                    }
                }

                // Sondan silinen kayıtlar zincirde iz bırakmaz: AUTOINCREMENT sayacı son Id'den büyükse silinmiştir
                if (seq > lastId)
                {
                    for (long i = lastId + 1; i <= seq; i++)
                        if (!isDeleted(i)) result.TailDeletedRows++;
                    countDeleted(lastId + 1, seq);
                    collectDeleted(lastId + 1, seq);
                }
                return 0;
            });
            return result;
        }

        /// <summary>
        /// Şu anki açıklanamayan sorunları (eski sürümden / bilinen müdahaleden kalan) "incelendi, kabul edildi" olarak imzalar.
        /// Sonradan oluşan yeni oynamalar yine yakalanır. Kabul edilen sorun sayısı döner.
        /// </summary>
        public async Task<int> AcceptCurrentStateAsync(string userName)
        {
            var chain = _chain;
            if (chain == null) throw new GuardException(ErrorCodes.InvalidRequest, "Log imza anahtarı okunamıyor: kabul işlemi yapılamaz.");
            var items = new List<KeyValuePair<long, string>>();
            var before = Verify(items);
            if (items.Count == 0) return 0;

            string now = DbTime.Now();
            await _db.Writer.EnqueueAsync(c =>
            {
                foreach (var item in items)
                    EventLogAdminChanges.Write(c, chain, item.Key, item.Value, now, userName);
            }).ConfigureAwait(false);

            string text = before.ModifiedRows + " değişmiş kayıt, " + before.Gaps + " kopukluk, sondan " + before.TailDeletedRows + " silinmiş kayıt";
            AppLog.Warn(Module, "Olay logundaki mevcut sorunlar kabul edildi (" + userName + "): " + text + ". İlk Id'ler: " + string.Join(", ", before.ProblemIds));
            AuditTrail.Record(_db, userName, "Olay logu bütünlük sorunları kabul edildi", text + " (Id: " + string.Join(", ", before.ProblemIds) + ")");
            return items.Count;
        }

        internal static string BuildMailBody(EventDefinition def, Severity severity, string source, GuardEvent e)
        {
            string color = severity == Severity.Kritik ? "#c62828" : severity == Severity.Uyari ? "#ef6c00" : "#1565c0";
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px\">");
            sb.Append("<h2 style=\"margin:0 0 4px 0;color:").Append(color).Append("\">").Append(H(def.Name)).Append("</h2>");
            sb.Append("<p style=\"margin:0 0 12px 0\">").Append(H(e.Summary)).Append("</p>");
            sb.Append("<table cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px\">");
            Row(sb, "Önem", SeverityText(severity));
            Row(sb, "Sunucu", Environment.MachineName);
            Row(sb, "Zaman", e.TimeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.GetCultureInfo("tr-TR")) + " (UTC " + e.TimeUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + ")");
            Row(sb, "Kaynak", source);
            if (!string.IsNullOrEmpty(e.UserName)) Row(sb, "Kullanıcı", e.UserName);
            if (!string.IsNullOrEmpty(e.IpAddress)) Row(sb, "IP", e.IpAddress);
            foreach (var d in e.Details) Row(sb, d.Key, d.Value);
            sb.Append("</table></div>");
            return sb.ToString();
        }

        private static void Row(StringBuilder sb, string name, string value)
        {
            sb.Append("<tr><td style=\"color:#666;border-bottom:1px solid #eee;white-space:nowrap\">").Append(H(name))
              .Append("</td><td style=\"border-bottom:1px solid #eee\">").Append(H(value)).Append("</td></tr>");
        }

        private static string H(string text)
        {
            return WebUtility.HtmlEncode(text ?? "");
        }

        internal static string SeverityText(Severity s)
        {
            return s == Severity.Kritik ? "Kritik" : s == Severity.Uyari ? "Uyarı" : "Bilgi";
        }
    }
}
