using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Engine.Rules
{
    /// <summary>
    /// Olay kuralları (EventRules tablosu). Motorlar kuralı <see cref="Get"/> ile bellekten okur;
    /// UI'den yapılan değişiklik servis yeniden başlatılmadan hemen geçerli olur.
    /// Katalogdaki yeni olay tipleri açılışta varsayılan değerleriyle eklenir; kullanıcı ayarlarına dokunulmaz.
    /// </summary>
    internal sealed class RuleStore
    {
        private const string Module = "Kurallar";
        public const int MinThreshold = 3;
        public const int MaxThreshold = 1000;

        private readonly GuardDatabase _db;
        private volatile Dictionary<string, EventRuleDto> _rules = new Dictionary<string, EventRuleDto>();

        public RuleStore(GuardDatabase db)
        {
            _db = db;
        }

        /// <summary>Olay tipinin güncel kuralı (motorlar her olayda çağırır). Bilinmeyen tipte null.</summary>
        public EventRuleDto Get(string eventType)
        {
            EventRuleDto r;
            return eventType != null && _rules.TryGetValue(eventType, out r) ? r : null;
        }

        /// <summary>Katalog sırasıyla tüm kurallar (UI için kopya).</summary>
        public List<EventRuleDto> ReadAll()
        {
            var rules = _rules;
            return EventCatalog.All.Where(d => rules.ContainsKey(d.Type)).Select(d => Clone(rules[d.Type])).ToList();
        }

        /// <summary>DB'de olmayan olay tiplerini varsayılan değerlerle ekler.</summary>
        public void SeedMissing()
        {
            var existing = new HashSet<string>(_db.Read(c =>
            {
                var list = new List<string>();
                using (var cmd = c.Create("SELECT EventType FROM EventRules"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(r.GetString(0));
                return list;
            }), StringComparer.Ordinal);

            var missing = EventCatalog.All.Where(d => !existing.Contains(d.Type)).ToList();
            if (missing.Count == 0) return;

            string now = DbTime.Now();
            _db.Writer.EnqueueAsync(c =>
            {
                foreach (var d in missing)
                {
                    var def = Default(d);
                    c.Execute(@"INSERT INTO EventRules (EventType, Monitor, WriteLog, SendMail, Severity, BanEnabled, Threshold, UpdatedUtc)
                                VALUES (@t, @m, @l, @s, @v, @b, @th, @u)",
                        ("@t", d.Type), ("@m", 1), ("@l", 1), ("@s", def.SendMail ? 1 : 0), ("@v", def.Severity),
                        ("@b", def.BanEnabled ? 1 : 0), ("@th", d.CanBan ? (object)def.Threshold : null), ("@u", now));
                }
            }).Wait(TimeSpan.FromSeconds(30));

            AppLog.Info(Module, missing.Count + " yeni olay kuralı varsayılan değerlerle eklendi.");
        }

        /// <summary>Kuralları DB'den belleğe yükler.</summary>
        public void Reload()
        {
            var rules = _db.Read(c =>
            {
                var dict = new Dictionary<string, EventRuleDto>(StringComparer.Ordinal);
                using (var cmd = c.Create("SELECT EventType, Monitor, WriteLog, SendMail, Severity, BanEnabled, Threshold FROM EventRules"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var def = EventCatalog.Find(r.GetString(0));
                        if (def == null) continue; // katalogdan kaldırılmış eski tip
                        dict[def.Type] = new EventRuleDto
                        {
                            EventType = def.Type,
                            Category = def.Category,
                            Name = def.Name,
                            Source = def.Source,
                            Monitor = r.GetInt64(1) != 0,
                            WriteLog = r.GetInt64(2) != 0,
                            SendMail = r.GetInt64(3) != 0,
                            Severity = r.GetInt32(4),
                            CanBan = def.CanBan,
                            BanEnabled = def.CanBan && r.GetInt64(5) != 0,
                            Threshold = def.CanBan ? (r.IsDBNull(6) ? def.DefaultThreshold : r.GetInt32(6)) : 0,
                            RecipientIds = new List<long>()
                        };
                    }
                }

                using (var cmd = c.Create("SELECT EventType, RecipientId FROM RuleRecipients ORDER BY RecipientId"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        EventRuleDto rule;
                        if (dict.TryGetValue(r.GetString(0), out rule)) rule.RecipientIds.Add(r.GetInt64(1));
                    }
                }
                return dict;
            });
            _rules = rules;
        }

        /// <summary>Alıcı Id → mail adresi (tüm alıcılar, pasifler dahil).</summary>
        private Dictionary<long, string> ReadRecipientEmails()
        {
            return _db.Read(c =>
            {
                var dict = new Dictionary<long, string>();
                using (var cmd = c.Create("SELECT Id, Email FROM Recipients"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) dict[r.GetInt64(0)] = r.GetString(1);
                return dict;
            });
        }

        public async Task SaveAsync(IList<EventRuleDto> changes, string userName)
        {
            if (changes == null || changes.Count == 0) return;

            var emails = ReadRecipientEmails();
            var updates = new List<Tuple<EventRuleDto, string>>();
            foreach (var c in changes)
            {
                var def = EventCatalog.Find(c.EventType);
                if (def == null)
                    throw new GuardException(ErrorCodes.Validation, "Bilinmeyen olay tipi: " + c.EventType);
                if (!Enum.IsDefined(typeof(Severity), c.Severity))
                    throw new GuardException(ErrorCodes.Validation, def.Name + ": geçersiz önem derecesi.");
                if (def.CanBan && (c.Threshold < MinThreshold || c.Threshold > MaxThreshold))
                    throw new GuardException(ErrorCodes.Validation, def.Name + ": ban eşiği " + MinThreshold + "-" + MaxThreshold + " arasında olmalı.");

                var recipientIds = (c.RecipientIds ?? new List<long>()).Distinct().OrderBy(id => id).ToList();
                if (recipientIds.Any(id => !emails.ContainsKey(id)))
                    throw new GuardException(ErrorCodes.Validation, def.Name + ": seçilen alıcılardan biri artık yok. Sayfayı yenileyip tekrar deneyin.");

                var current = Get(def.Type);
                var normalized = new EventRuleDto
                {
                    EventType = def.Type,
                    Monitor = c.Monitor,
                    WriteLog = c.WriteLog,
                    SendMail = c.SendMail,
                    Severity = c.Severity,
                    BanEnabled = def.CanBan && c.BanEnabled,
                    Threshold = def.CanBan ? c.Threshold : 0,
                    RecipientIds = recipientIds
                };
                string diff = Describe(current, normalized, emails);
                if (diff.Length > 0) updates.Add(Tuple.Create(normalized, def.Name + ": " + diff));
            }
            if (updates.Count == 0) return;

            await WriteAsync(updates.Select(u => u.Item1)).ConfigureAwait(false);
            Reload();

            foreach (var u in updates)
            {
                AppLog.Info(Module, "Kural değişti (" + userName + ") " + u.Item2);
                AuditTrail.Record(_db, userName, "Olay kuralı değiştirildi", u.Item2);
            }
        }

        public async Task ResetDefaultsAsync(string userName)
        {
            await WriteAsync(EventCatalog.All.Select(Default)).ConfigureAwait(false);
            Reload();
            AppLog.Info(Module, "Tüm kurallar varsayılana döndürüldü (" + userName + ").");
            AuditTrail.Record(_db, userName, "Olay kuralları varsayılana döndürüldü");
        }

        private Task WriteAsync(IEnumerable<EventRuleDto> rules)
        {
            var list = rules.ToList();
            string now = DbTime.Now();
            return _db.Writer.EnqueueAsync(c =>
            {
                foreach (var r in list)
                {
                    c.Execute(@"UPDATE EventRules SET Monitor = @m, WriteLog = @l, SendMail = @s, Severity = @v,
                                BanEnabled = @b, Threshold = @th, UpdatedUtc = @u WHERE EventType = @t",
                        ("@m", r.Monitor ? 1 : 0), ("@l", r.WriteLog ? 1 : 0), ("@s", r.SendMail ? 1 : 0), ("@v", r.Severity),
                        ("@b", r.BanEnabled ? 1 : 0), ("@th", r.Threshold > 0 ? (object)r.Threshold : null), ("@u", now), ("@t", r.EventType));

                    c.Execute("DELETE FROM RuleRecipients WHERE EventType = @t", ("@t", r.EventType));
                    foreach (var id in r.RecipientIds ?? new List<long>())
                        c.Execute("INSERT INTO RuleRecipients (EventType, RecipientId) VALUES (@t, @r)", ("@t", r.EventType), ("@r", id));
                }
            });
        }

        private static EventRuleDto Default(EventDefinition d)
        {
            return new EventRuleDto
            {
                EventType = d.Type,
                Category = d.Category,
                Name = d.Name,
                Source = d.Source,
                Monitor = true,
                WriteLog = true,
                SendMail = d.DefaultMail,
                Severity = (int)d.DefaultSeverity,
                CanBan = d.CanBan,
                BanEnabled = d.CanBan,
                Threshold = d.CanBan ? d.DefaultThreshold : 0,
                RecipientIds = new List<long>()
            };
        }

        /// <summary>İşlem kaydı için okunur fark: "Mail: kapalı → açık, Eşik: 10 → 5, Alıcılar: Tüm aktif alıcılar → a@b.com".</summary>
        private static string Describe(EventRuleDto before, EventRuleDto after, Dictionary<long, string> emails)
        {
            if (before == null) return "yeni";
            var parts = new List<string>();
            if (before.Monitor != after.Monitor) parts.Add("İzle: " + OnOff(before.Monitor) + " → " + OnOff(after.Monitor));
            if (before.WriteLog != after.WriteLog) parts.Add("Logla: " + OnOff(before.WriteLog) + " → " + OnOff(after.WriteLog));
            if (before.SendMail != after.SendMail) parts.Add("Mail: " + OnOff(before.SendMail) + " → " + OnOff(after.SendMail));
            if (before.Severity != after.Severity) parts.Add("Önem: " + SeverityText(before.Severity) + " → " + SeverityText(after.Severity));
            if (before.BanEnabled != after.BanEnabled) parts.Add("Ban: " + OnOff(before.BanEnabled) + " → " + OnOff(after.BanEnabled));
            if (before.Threshold != after.Threshold) parts.Add("Eşik: " + before.Threshold + " → " + after.Threshold);
            var b = before.RecipientIds ?? new List<long>();
            var a = after.RecipientIds ?? new List<long>();
            if (!b.OrderBy(x => x).SequenceEqual(a.OrderBy(x => x)))
                parts.Add("Alıcılar: " + RecipientsText(b, emails) + " → " + RecipientsText(a, emails));
            return string.Join(", ", parts);
        }

        private static string RecipientsText(List<long> ids, Dictionary<long, string> emails)
        {
            if (ids.Count == 0) return "Tüm aktif alıcılar";
            return string.Join(", ", ids.Select(id => { string e; return emails.TryGetValue(id, out e) ? e : "#" + id; }));
        }

        private static string OnOff(bool v) { return v ? "açık" : "kapalı"; }

        private static string SeverityText(int s)
        {
            switch ((Severity)s)
            {
                case Severity.Kritik: return "Kritik";
                case Severity.Uyari: return "Uyarı";
                default: return "Bilgi";
            }
        }

        private static EventRuleDto Clone(EventRuleDto r)
        {
            return new EventRuleDto
            {
                EventType = r.EventType, Category = r.Category, Name = r.Name, Source = r.Source,
                Monitor = r.Monitor, WriteLog = r.WriteLog, SendMail = r.SendMail, Severity = r.Severity,
                CanBan = r.CanBan, BanEnabled = r.BanEnabled, Threshold = r.Threshold,
                RecipientIds = new List<long>(r.RecipientIds ?? new List<long>())
            };
        }
    }
}
