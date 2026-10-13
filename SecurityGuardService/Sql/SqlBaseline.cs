using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using SecurityGuard.Shared.Contracts;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Engine.Events;
using static SecurityGuardService.Checks.SqlQuery;

namespace SecurityGuardService.Sql
{
    /// <summary>
    /// SQL Server değişiklik karşılaştırması (SQL izlemesinin / Audit'in yedeği): her kontrol aralığında güvenlik açısından
    /// önemli nesnelerin anlık görüntüsü alınır, bir öncekiyle karşılaştırılır (Baselines tablosu).
    /// - İlk görüntü sadece kaydedilir (olay yok).
    /// - Aynı değişikliği SQL izlemesi zaten bildirdiyse (aynı tip, aynı ad, aynı aralıkta) tekrar olay üretilmez.
    /// - SQL izlemesi çalışırken bildirmediği değişiklik ayrıca vurgulanır (izleme atlatılmış / kapatılıp açılmış olabilir).
    /// Sadece OKUR. Kimin yaptığını göstermez (anlık görüntüde bu bilgi yok).
    /// </summary>
    internal sealed class SqlBaseline
    {
        private const string Module = "SQLKarşılaştırma";
        private const string Scope = "sql-baseline";
        private const int MaxEventsPerCategory = 15;
        private const string OwnSource = "SQL değişiklik karşılaştırması";

        private readonly GuardDatabase _db;
        private readonly SqlConnectionStore _sql;
        private readonly SqlAuditManager _audit;
        private readonly EventRecorder _events;

        public SqlBaseline(GuardDatabase db, SqlConnectionStore sql, SqlAuditManager audit, EventRecorder events)
        {
            _db = db;
            _sql = sql;
            _audit = audit;
            _events = events;
        }

        /// <summary>Bir değişiklik: olay tipi, özet, eşleştirme adı (Audit olayıyla), önce / sonra.</summary>
        private sealed class Change
        {
            public string Type, Summary, Match, Before, After;
        }

        private sealed class Category
        {
            public string Name;
            public string Sql;

            /// <summary>Anahtar → değer sözlüğünde değişiklikleri olaylara çevirir.</summary>
            public Func<Dictionary<string, string>, Dictionary<string, string>, List<Change>> Diff;
        }

        public void Check(string instance, CancellationToken token)
        {
            string cs = _sql.GetConnectionString(instance);
            if (cs == null) return;
            using (var conn = new SqlConnection(cs))
            {
                conn.Open();
                foreach (var cat in Categories)
                {
                    if (token.IsCancellationRequested) return;
                    try
                    {
                        var now = Rows(conn, cat.Sql, token).GroupBy(r => Str(r[0])).ToDictionary(g => g.Key, g => Str(g.First()[1]) ?? "", StringComparer.Ordinal);
                        CheckCategory(instance, cat, now);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        AppLog.Warn(Module, instance + " / " + cat.Name + ": karşılaştırma yapılamadı: " + ex.Message);
                    }
                }
            }
        }

        private void CheckCategory(string instance, Category cat, Dictionary<string, string> now)
        {
            string key = instance.ToUpperInvariant() + "|" + cat.Name;
            var stored = _db.Read(c =>
            {
                using (var cmd = c.Create("SELECT Snapshot, CapturedUtc FROM Baselines WHERE Scope = @s AND Key = @k", ("@s", Scope), ("@k", key)))
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? Tuple.Create(r.GetString(0), r.GetString(1)) : null;
            });
            string json = PipeProtocol.ToJson(now);
            if (stored != null && stored.Item1 == json) return;

            _db.Writer.EnqueueAsync(c => c.Execute(
                @"INSERT INTO Baselines (Scope, Key, Snapshot, CapturedUtc) VALUES (@s, @k, @j, @t)
                  ON CONFLICT(Scope, Key) DO UPDATE SET Snapshot = excluded.Snapshot, CapturedUtc = excluded.CapturedUtc",
                ("@s", Scope), ("@k", key), ("@j", json), ("@t", DbTime.Now()))).Wait(TimeSpan.FromSeconds(30));
            if (stored == null)
            {
                AppLog.Info(Module, instance + " / " + cat.Name + ": ilk görüntü kaydedildi (" + now.Count + " kayıt).");
                return;
            }

            var before = PipeProtocol.FromJson<Dictionary<string, string>>(stored.Item1) ?? new Dictionary<string, string>();
            var changes = cat.Diff(before, now);
            if (changes.Count == 0) return;

            // Son görüntüden bu yana SQL izlemesinin bildirdikleri tekrar bildirilmez
            string since = DbTime.ToDb(DbTime.FromDb(stored.Item2).AddMinutes(-2));
            bool auditRunning = _audit.GetState(instance).Running;
            if (auditRunning)
            {
                // Aynı değişikliğin SQL izlemesi olayı birkaç saniye içinde gelip yazılıyor olabilir: bekle, yazma kuyruğunu boşalt
                Thread.Sleep(TimeSpan.FromSeconds(10));
                _db.Writer.EnqueueAsync(c => { }).Wait(TimeSpan.FromSeconds(30));
            }
            var fresh = changes.Where(ch => !AlreadyReported(ch, since)).ToList();
            AppLog.Info(Module, instance + " / " + cat.Name + ": " + changes.Count + " değişiklik, " + (changes.Count - fresh.Count) + " tanesi SQL izlemesince zaten bildirilmiş.");

            foreach (var ch in fresh.Take(MaxEventsPerCategory)) Record(instance, ch, auditRunning);
            if (fresh.Count > MaxEventsPerCategory)
                Record(instance, new Change
                {
                    Type = fresh[0].Type,
                    Summary = cat.Name + ": " + (fresh.Count - MaxEventsPerCategory) + " değişiklik daha (toplu)",
                    Match = null,
                    After = string.Join(", ", fresh.Skip(MaxEventsPerCategory).Take(30).Select(c => c.Match))
                }, auditRunning);
        }

        /// <summary>
        /// Bu değişikliği SQL izlemesi (Audit) zaten bildirdi mi? Sadece Audit kaynaklı olaylara bakılır: karşılaştırmanın kendi
        /// önceki olayı (ör. az önce "xp_cmdshell kapatıldı") aynı pencereye düşer ve sayılırsa, hemen ardından gelen yeni değişiklik
        /// (tekrar "AÇILDI") yanlışlıkla bastırılırdı.
        /// </summary>
        private bool AlreadyReported(Change ch, string since)
        {
            if (string.IsNullOrEmpty(ch.Match)) return false;
            return _db.Read(c => c.Scalar<long>(
                @"SELECT COUNT(*) FROM EventLog WHERE EventType = @t AND TimeUtc >= @s AND Source NOT LIKE @own
                  AND (Summary LIKE @m ESCAPE '\' OR Detail LIKE @m ESCAPE '\')",
                ("@t", ch.Type), ("@s", since), ("@own", OwnSource + "%"),
                ("@m", "%" + ch.Match.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"))) > 0;
        }

        private void Record(string instance, Change ch, bool auditRunning)
        {
            // Sunucu ayarı: değişen ayarı karşılaştırma bulur, yapanı SQL izlemesinin RECONFIGURE kaydı söyler
            var actor = ch.Type == "Sql.DangerousConfigChanged" ? SqlConfigTrail.Latest(instance, TimeSpan.FromMinutes(15)) : null;
            bool known = actor != null && !string.IsNullOrEmpty(actor.Who);
            var e = new GuardEvent(ch.Type, ch.Summary + " (" + instance + (known ? ", yapan: " + actor.Who : "") + ")") { Source = OwnSource + " (" + instance + ")" };
            if (ch.Before != null) e.With("Önce", ch.Before);
            if (ch.After != null) e.With("Sonra", ch.After);
            if (known)
            {
                e.UserName = actor.Who;
                e.IpAddress = actor.Ip;
                e.With("Yapan", actor.Who).With("Uygulama", actor.Application).With("Bilgisayar", actor.Host).With("Komut", actor.Statement)
                 .With("Not", "Yapan kişi SQL izlemesinin (Audit) ayar değişikliği kaydından alındı (" + actor.TimeUtc.ToLocalTime().ToString("HH:mm:ss") + ").");
                _events.Record(e);
                return;
            }
            e.With("Not", auditRunning
                ? "SQL izlemesi (Audit) çalıştığı halde bu değişikliği bildirmedi: izleme bir süre kapatılmış / atlatılmış olabilir. Kimin yaptığı karşılaştırmada görünmez."
                : "SQL izlemesi çalışmadığı için değişiklik periyodik karşılaştırmayla bulundu; kimin yaptığı görünmez. Anlık ve kim yaptı bilgisi için SQL izlemesini kurun.");
            _events.Record(e);
        }

        // ================================================================ Kategoriler

        private static readonly string[] DangerousConfigs =
        {
            "xp_cmdshell", "Ole Automation Procedures", "clr enabled", "clr strict security", "Ad Hoc Distributed Queries",
            "cross db ownership chaining", "remote admin connections", "Database Mail XPs", "external scripts enabled"
        };

        private static readonly Category[] Categories =
        {
            new Category
            {
                Name = "Login'ler",
                // Sistem kataloğunun sabit metin kolonları (type_desc...) Latin1 harmanlamasındadır; sunucu Turkish_CI_AS ise
                // birleştirmede "collation conflict" verir: her kolon açıkça DATABASE_DEFAULT yapılır.
                Sql = @"
SELECT p.name,
       p.type_desc COLLATE DATABASE_DEFAULT + '|' + CAST(p.is_disabled AS varchar(1)) + '|' + CONVERT(varchar(200), p.sid, 1) + '|' + ISNULL(p.default_database_name COLLATE DATABASE_DEFAULT, '') + '|'
       + ISNULL(CONVERT(varchar(30), CAST(LOGINPROPERTY(p.name, 'PasswordLastSetTime') AS datetime), 126), '') + '|'
       + ISNULL(CAST(l.is_policy_checked AS varchar(1)), '') + '|' + ISNULL(CAST(l.is_expiration_checked AS varchar(1)), '')
FROM sys.server_principals p LEFT JOIN sys.sql_logins l ON l.principal_id = p.principal_id
WHERE p.type IN ('S', 'U', 'G', 'C', 'K') AND p.name NOT LIKE '##%'",
                Diff = DiffLogins
            },
            new Category
            {
                Name = "Sunucu rolü üyelikleri",
                Sql = @"
SELECT r.name COLLATE DATABASE_DEFAULT + ' > ' + m.name COLLATE DATABASE_DEFAULT, '1'
FROM sys.server_role_members rm
JOIN sys.server_principals r ON r.principal_id = rm.role_principal_id
JOIN sys.server_principals m ON m.principal_id = rm.member_principal_id",
                Diff = (b, a) => Simple(b, a, "Sql.ServerRoleChanged",
                    k => "Sunucu rolüne üye eklendi: " + Member(k) + " → " + Role(k),
                    k => "Sunucu rolünden üye çıkarıldı: " + Member(k) + " → " + Role(k), null, Member)
            },
            new Category
            {
                Name = "Sunucu yetkileri",
                Sql = @"
SELECT pr.name COLLATE DATABASE_DEFAULT + ' : ' + p.permission_name COLLATE DATABASE_DEFAULT
       + ISNULL(' ON ' + CASE p.class WHEN 101 THEN (SELECT x.name COLLATE DATABASE_DEFAULT FROM sys.server_principals x WHERE x.principal_id = p.major_id)
                                      WHEN 105 THEN (SELECT e.name COLLATE DATABASE_DEFAULT FROM sys.endpoints e WHERE e.endpoint_id = p.major_id) END, ''),
       p.state_desc COLLATE DATABASE_DEFAULT
FROM sys.server_permissions p JOIN sys.server_principals pr ON pr.principal_id = p.grantee_principal_id
WHERE p.class IN (100, 101, 105) AND p.permission_name <> 'CONNECT SQL' AND pr.name <> 'public'
  AND pr.name NOT LIKE '##%' AND pr.name NOT LIKE 'NT SERVICE\%' AND pr.name NOT LIKE 'NT AUTHORITY\%'",
                Diff = (b, a) => Simple(b, a, "Sql.PermissionChanged",
                    k => "Sunucu yetkisi verildi: " + k, k => "Sunucu yetkisi geri alındı: " + k, k => "Sunucu yetkisi değişti: " + k, k => k.Split(':')[0].Trim())
            },
            new Category
            {
                Name = "Credential'lar",
                Sql = "SELECT name, ISNULL(credential_identity, '') + '|' + CONVERT(varchar(30), modify_date, 126) FROM sys.credentials",
                Diff = (b, a) => Simple(b, a, "Sql.CredentialChanged",
                    k => "Credential oluşturuldu: " + k, k => "Credential silindi: " + k, k => "Credential değiştirildi: " + k, k => k)
            },
            new Category
            {
                Name = "Sertifika ve anahtarlar (master)",
                Sql = @"
SELECT 'Sertifika ' + name, CONVERT(varchar(100), thumbprint, 1) FROM master.sys.certificates WHERE name NOT LIKE '##%'
UNION ALL SELECT 'Asimetrik anahtar ' + name, CONVERT(varchar(100), thumbprint, 1) FROM master.sys.asymmetric_keys WHERE name NOT LIKE '##%'
UNION ALL SELECT 'Simetrik anahtar ' + name, CONVERT(varchar(40), key_guid) FROM master.sys.symmetric_keys WHERE name NOT LIKE '##%'
UNION ALL SELECT 'Database master key (master)', CONVERT(varchar(40), key_guid) FROM master.sys.symmetric_keys WHERE name = '##MS_DatabaseMasterKey##'",
                Diff = (b, a) => Simple(b, a, "Sql.KeyChanged",
                    k => k + " oluşturuldu", k => k + " silindi", k => k + " yeniden oluşturuldu / değişti", k => k.Substring(k.LastIndexOf(' ') + 1))
            },
            new Category
            {
                Name = "Veritabanları",
                Sql = @"
SELECT name, state_desc COLLATE DATABASE_DEFAULT + '|' + recovery_model_desc COLLATE DATABASE_DEFAULT + '|' + CAST(is_encrypted AS varchar(1)) + '|' + ISNULL(SUSER_SNAME(owner_sid), '?') + '|'
       + CAST(is_trustworthy_on AS varchar(1)) + '|' + CONVERT(varchar(30), create_date, 126) + '|' + CAST(is_read_only AS varchar(1))
FROM sys.databases WHERE name <> 'tempdb'",
                Diff = DiffDatabases
            },
            new Category
            {
                Name = "Sunucu trigger'ları",
                Sql = "SELECT name, CAST(is_disabled AS varchar(1)) + '|' + CONVERT(varchar(30), modify_date, 126) FROM sys.server_triggers",
                Diff = (b, a) => Simple(b, a, "Sql.ServerTriggerChanged",
                    k => "Sunucu trigger'ı oluşturuldu: " + k, k => "Sunucu trigger'ı silindi: " + k, k => "Sunucu trigger'ı değiştirildi / açıldı / kapatıldı: " + k, k => k)
            },
            new Category
            {
                Name = "Tehlikeli sunucu ayarları",
                Sql = "SELECT name, CAST(value_in_use AS varchar(30)) FROM sys.configurations WHERE name IN ('" + string.Join("', '", DangerousConfigs) + "')",
                Diff = (b, a) => b.Keys.Intersect(a.Keys).Where(k => b[k] != a[k]).Select(k => new Change
                {
                    Type = "Sql.DangerousConfigChanged",
                    Summary = k + (k == "clr strict security" ? (a[k] == "0" ? " KAPATILDI" : " açıldı") : (a[k] == "1" ? " AÇILDI" : " kapatıldı")),
                    Match = k,
                    Before = b[k],
                    After = a[k]
                }).ToList()
            },
            new Category
            {
                Name = "Linked server'lar",
                Sql = @"
SELECT s.name, ISNULL(s.product, '') + '|' + ISNULL(s.data_source, '') + '|'
       + ISNULL(STUFF((SELECT ',' + ISNULL(SUSER_NAME(l.local_principal_id), '*') + '>' + ISNULL(l.remote_name, '(kendi)')
                       FROM sys.linked_logins l WHERE l.server_id = s.server_id FOR XML PATH('')), 1, 1, ''), '')
FROM sys.servers s WHERE s.is_linked = 1",
                Diff = (b, a) => Simple(b, a, "Sql.LinkedServerChanged",
                    k => "Linked server eklendi: " + k, k => "Linked server silindi: " + k, k => "Linked server / login eşlemesi değişti: " + k, k => k)
            },
        };

        private static string Role(string key)
        {
            return key.Substring(0, key.IndexOf(" > ", StringComparison.Ordinal));
        }

        private static string Member(string key)
        {
            return key.Substring(key.IndexOf(" > ", StringComparison.Ordinal) + 3);
        }

        /// <summary>Eklenen / silinen / değişen anahtarlar için tek tip olay.</summary>
        private static List<Change> Simple(Dictionary<string, string> before, Dictionary<string, string> after, string type,
            Func<string, string> added, Func<string, string> removed, Func<string, string> changed, Func<string, string> match)
        {
            var list = new List<Change>();
            foreach (var k in after.Keys.Where(k => !before.ContainsKey(k)))
                list.Add(new Change { Type = type, Summary = added(k), Match = match(k), After = after[k] == "1" ? null : after[k] });
            foreach (var k in before.Keys.Where(k => !after.ContainsKey(k)))
                list.Add(new Change { Type = type, Summary = removed(k), Match = match(k), Before = before[k] == "1" ? null : before[k] });
            if (changed != null)
                foreach (var k in after.Keys.Where(k => before.ContainsKey(k) && before[k] != after[k]))
                    list.Add(new Change { Type = type, Summary = changed(k), Match = match(k), Before = before[k], After = after[k] });
            return list;
        }

        private static List<Change> DiffLogins(Dictionary<string, string> before, Dictionary<string, string> after)
        {
            // Değer: tip|devre dışı|sid|varsayılan DB|şifre son değişiklik|politika|süre
            Func<string, string[]> parts = v => (v ?? "").Split('|');
            var list = new List<Change>();
            var added = after.Keys.Where(k => !before.ContainsKey(k)).ToList();
            var removed = before.Keys.Where(k => !after.ContainsKey(k)).ToList();

            // Aynı SID farklı ad = adı değiştirilmiş
            foreach (var r in removed.ToList())
            {
                string sid = parts(before[r])[2];
                var renamed = added.FirstOrDefault(a => parts(after[a])[2] == sid);
                if (renamed == null) continue;
                list.Add(new Change { Type = sid == "0x01" ? "Sql.SaStateChanged" : "Sql.LoginChanged", Summary = "SQL login'in adı değiştirildi: " + r + " → " + renamed, Match = renamed });
                removed.Remove(r);
                added.Remove(renamed);
            }
            foreach (var a in added)
                list.Add(new Change { Type = "Sql.LoginChanged", Summary = "SQL login oluşturuldu: " + a + " (" + parts(after[a])[0] + ")", Match = a });
            foreach (var r in removed)
                list.Add(new Change { Type = "Sql.LoginChanged", Summary = "SQL login silindi: " + r, Match = r });

            foreach (var k in after.Keys.Where(k => before.ContainsKey(k) && before[k] != after[k]))
            {
                var b = parts(before[k]);
                var n = parts(after[k]);
                if (b.Length < 7 || n.Length < 7) continue;
                bool sa = n[2] == "0x01";
                if (b[1] != n[1])
                    list.Add(new Change { Type = sa ? "Sql.SaStateChanged" : "Sql.LoginChanged", Summary = (sa ? "sa hesabı " : "SQL login ") + (n[1] == "1" ? "devre dışı bırakıldı: " : "ETKİNLEŞTİRİLDİ: ") + k, Match = k });
                if (b[4] != n[4])
                    list.Add(new Change { Type = "Sql.PasswordChanged", Summary = "SQL login şifresi değişti: " + k, Match = k, Before = b[4], After = n[4] });
                if (b[5] != n[5] && n[5] == "0")
                    list.Add(new Change { Type = "Sql.LoginChanged", Summary = "SQL login'de şifre politikası (CHECK_POLICY) KAPATILDI: " + k, Match = k });
                if (b[3] != n[3] || (b[5] != n[5] && n[5] == "1") || b[6] != n[6])
                    list.Add(new Change { Type = "Sql.LoginChanged", Summary = "SQL login değiştirildi: " + k, Match = k, Before = before[k], After = after[k] });
            }
            return list;
        }

        private static List<Change> DiffDatabases(Dictionary<string, string> before, Dictionary<string, string> after)
        {
            // Değer: durum|recovery|şifreli|sahip|trustworthy|oluşturma|salt okunur
            Func<string, string[]> parts = v => (v ?? "").Split('|');
            var list = new List<Change>();
            foreach (var k in after.Keys.Where(k => !before.ContainsKey(k)))
                list.Add(new Change { Type = "Sql.DatabaseChanged", Summary = "Veritabanı oluşturuldu / geri yüklendi: " + k, Match = k });
            foreach (var k in before.Keys.Where(k => !after.ContainsKey(k)))
                list.Add(new Change { Type = "Sql.DatabaseDropped", Summary = "Veritabanı silindi: " + k, Match = k });
            foreach (var k in after.Keys.Where(k => before.ContainsKey(k) && before[k] != after[k]))
            {
                var b = parts(before[k]);
                var n = parts(after[k]);
                if (b.Length < 7 || n.Length < 7) continue;
                if (b[2] != n[2] && n[2] == "1")
                    list.Add(new Change { Type = "Sql.TdeEnabled", Summary = "Veritabanı şifrelemesi (TDE) AÇILDI: " + k, Match = k });
                var notes = new List<string>();
                if (b[0] != n[0]) notes.Add("durum " + b[0] + " → " + n[0]);
                if (b[1] != n[1]) notes.Add("recovery " + b[1] + " → " + n[1]);
                if (b[3] != n[3]) notes.Add("sahip " + b[3] + " → " + n[3]);
                if (b[4] != n[4]) notes.Add("TRUSTWORTHY " + (n[4] == "1" ? "AÇILDI" : "kapatıldı"));
                if (b[5] != n[5]) notes.Add("üzerine geri yüklendi (oluşturma zamanı değişti)");
                if (b[6] != n[6]) notes.Add(n[6] == "1" ? "salt okunur yapıldı" : "yazılabilir yapıldı");
                if (b[2] != n[2] && n[2] == "0") notes.Add("TDE kapatıldı");
                if (notes.Count > 0)
                    list.Add(new Change { Type = "Sql.DatabaseChanged", Summary = "Veritabanı değiştirildi: " + k + " (" + string.Join(", ", notes) + ")", Match = k });
            }
            return list;
        }
    }
}
