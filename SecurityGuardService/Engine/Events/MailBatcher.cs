using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Engine.Rules;
using SecurityGuardService.Mail;

namespace SecurityGuardService.Engine.Events
{
    /// <summary>
    /// Olay maillerini toplar (Genel Ayarlar → Mail → "Toplama süresi"):
    /// - Kritik: o olay tipinde son süre içinde mail gitmediyse HEMEN gider; gittiyse sonrakiler toplanır,
    ///   süre dolunca tek özet mail ("son X dakikada N olay daha").
    /// - Kritik olmayan: her zaman toplanır; süre dolunca tek mail (tek olaysa normal mail, çoksa özet).
    /// - Toplama olay tipine göre: her kuralın kendi alıcıları korunur.
    /// - Süre 0 = her olay hemen. Servis dururken bekleyenler hemen kuyruğa alınır (kaybolmaz).
    /// </summary>
    internal sealed class MailBatcher : IDisposable
    {
        private const string Module = "Mail";
        private const int MaxListedEvents = 50;

        private readonly MailService _mail;
        private readonly Func<int> _windowMinutes;
        private readonly Func<DateTime> _clock;
        private readonly Dictionary<string, Bucket> _buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        private readonly object _sync = new object();
        private readonly ManualResetEventSlim _stopping = new ManualResetEventSlim(false);
        private Thread _thread;

        public MailBatcher(MailService mail, Func<int> windowMinutes, Func<DateTime> clock = null)
        {
            _mail = mail;
            _windowMinutes = windowMinutes;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        public void Start()
        {
            _thread = new Thread(() =>
            {
                while (!_stopping.Wait(TimeSpan.FromSeconds(5)))
                {
                    try { FlushDue(); }
                    catch (Exception ex) { AppLog.Error(Module, "Toplanan olay mailleri gönderilemedi.", ex); }
                }
            }) { IsBackground = true, Name = "MailBatcher" };
            _thread.Start();
        }

        /// <summary>Olay mailini gönderir veya toplar.</summary>
        public void Add(EventDefinition def, Severity severity, IList<long> recipients, string source, GuardEvent e)
        {
            var item = new Item { Event = e, Source = source };
            TimeSpan window = TimeSpan.FromMinutes(Math.Max(0, _windowMinutes()));
            DateTime now = _clock();

            if (window == TimeSpan.Zero)
            {
                SendSingle(def, severity, recipients, item);
                return;
            }

            lock (_sync)
            {
                Bucket b;
                if (!_buckets.TryGetValue(def.Type, out b)) _buckets[def.Type] = b = new Bucket();
                b.Def = def;
                b.Severity = severity;
                b.Recipients = recipients;

                bool quiet = b.LastSentUtc == null || now - b.LastSentUtc.Value >= window;
                if (severity == Severity.Kritik && quiet && b.Pending.Count == 0)
                {
                    b.LastSentUtc = now;
                    SendSingle(def, severity, recipients, item); // ilk kritik uyarı beklemez
                    return;
                }

                b.Pending.Add(item);
                if (b.FlushAtUtc == null)
                    b.FlushAtUtc = severity == Severity.Kritik && b.LastSentUtc != null ? b.LastSentUtc.Value + window : now + window;
            }
        }

        /// <summary>Süresi dolan toplamaları gönderir.</summary>
        public void FlushDue()
        {
            Flush(false);
        }

        /// <summary>Bekleyen her şeyi şimdi gönderir (servis durdurulurken).</summary>
        public void FlushAll()
        {
            Flush(true);
        }

        /// <summary>Bekleyen toplam olay sayısı (test / durum ekranı).</summary>
        public int PendingCount
        {
            get { lock (_sync) return _buckets.Values.Sum(b => b.Pending.Count); }
        }

        private void Flush(bool all)
        {
            var due = new List<KeyValuePair<Bucket, List<Item>>>();
            DateTime now = _clock();
            lock (_sync)
            {
                foreach (var b in _buckets.Values)
                {
                    if (b.Pending.Count == 0 || (!all && (b.FlushAtUtc == null || now < b.FlushAtUtc.Value))) continue;
                    due.Add(new KeyValuePair<Bucket, List<Item>>(b, b.Pending.ToList()));
                    b.Pending.Clear();
                    b.FlushAtUtc = null;
                    b.LastSentUtc = now;
                }
            }

            foreach (var d in due)
            {
                var b = d.Key;
                if (d.Value.Count == 1) SendSingle(b.Def, b.Severity, b.Recipients, d.Value[0]);
                else SendSummary(b.Def, b.Severity, b.Recipients, d.Value);
            }
        }

        // ================================================================ Mail içeriği

        private void SendSingle(EventDefinition def, Severity severity, IList<long> recipients, Item item)
        {
            string subject = "[SecurityGuard] " + EventRecorder.SeverityText(severity) + ": " + def.Name + " - " + Environment.MachineName;
            _mail.EnqueueForEvent(def.Type, recipients, subject, EventRecorder.BuildMailBody(def, severity, item.Source, item.Event),
                severity == Severity.Kritik ? 1 : 0);
        }

        private void SendSummary(EventDefinition def, Severity severity, IList<long> recipients, List<Item> items)
        {
            items = items.OrderBy(i => i.Event.TimeUtc).ToList();
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            DateTime first = items[0].Event.TimeUtc.ToLocalTime(), last = items[items.Count - 1].Event.TimeUtc.ToLocalTime();
            string subject = "[SecurityGuard] " + EventRecorder.SeverityText(severity) + ": " + def.Name + " (" + items.Count + " olay) - " + Environment.MachineName;
            string color = severity == Severity.Kritik ? "#c62828" : severity == Severity.Uyari ? "#ef6c00" : "#1565c0";

            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px\">");
            sb.Append("<h2 style=\"margin:0 0 4px 0;color:").Append(color).Append("\">").Append(H(def.Name)).Append(" — ").Append(items.Count).Append(" olay</h2>");
            sb.Append("<p style=\"margin:0 0 12px 0\">Sunucu ").Append(H(Environment.MachineName)).Append(", ")
              .Append(H(first.ToString("dd.MM.yyyy HH:mm:ss", tr))).Append(" – ").Append(H(last.ToString("HH:mm:ss", tr)))
              .Append(" arasında. (Toplu bildirim: aynı olay tipi kısa sürede tekrarlandı.)</p>");
            sb.Append("<table cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px\">");
            sb.Append("<tr style=\"background:#f2f2f2\"><th align=\"left\">Zaman</th><th align=\"left\">Olay</th><th align=\"left\">Kullanıcı</th><th align=\"left\">IP</th></tr>");
            foreach (var i in items.Take(MaxListedEvents))
            {
                var e = i.Event;
                sb.Append("<tr><td style=\"border-bottom:1px solid #eee;white-space:nowrap;vertical-align:top\">")
                  .Append(H(e.TimeUtc.ToLocalTime().ToString("HH:mm:ss", tr))).Append("</td><td style=\"border-bottom:1px solid #eee\">")
                  .Append(H(e.Summary));
                if (e.Details.Count > 0)
                {
                    sb.Append("<div style=\"color:#666;font-size:12px\">");
                    sb.Append(string.Join("<br/>", e.Details.Take(6).Select(p => H(p.Key) + ": " + H(Short(p.Value, 200)))));
                    sb.Append("</div>");
                }
                sb.Append("</td><td style=\"border-bottom:1px solid #eee;vertical-align:top\">").Append(H(e.UserName))
                  .Append("</td><td style=\"border-bottom:1px solid #eee;vertical-align:top\">").Append(H(e.IpAddress)).Append("</td></tr>");
            }
            sb.Append("</table>");
            if (items.Count > MaxListedEvents)
                sb.Append("<p>… ve ").Append(items.Count - MaxListedEvents).Append(" olay daha. Tamamı SecurityGuard → Olay Logu'nda.</p>");
            sb.Append("</div>");

            _mail.EnqueueForEvent(def.Type, recipients, subject, sb.ToString(), severity == Severity.Kritik ? 1 : 0);
            AppLog.Info(Module, def.Name + ": " + items.Count + " olay tek mailde toplandı.");
        }

        private static string H(string text)
        {
            return WebUtility.HtmlEncode(text ?? "");
        }

        private static string Short(string s, int max)
        {
            return s == null || s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        public void Dispose()
        {
            _stopping.Set();
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(5));
            try
            {
                FlushAll();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Kapanışta bekleyen olay mailleri kuyruğa alınamadı.", ex);
            }
        }

        private sealed class Bucket
        {
            public EventDefinition Def;
            public Severity Severity;
            public IList<long> Recipients;
            public DateTime? LastSentUtc;
            public DateTime? FlushAtUtc;
            public readonly List<Item> Pending = new List<Item>();
        }

        private sealed class Item
        {
            public GuardEvent Event;
            public string Source;
        }
    }
}
