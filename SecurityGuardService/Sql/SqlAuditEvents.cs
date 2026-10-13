using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SecurityGuard.Shared;

namespace SecurityGuardService.Sql
{
    /// <summary>SQL Server Audit kaydının SecurityGuard olayına çevrilmiş hali.</summary>
    internal sealed class SqlAuditMatch
    {
        public string EventType;
        public string Summary;
        public string UserName;
        public string IpAddress;
        public readonly List<KeyValuePair<string, string>> Details = new List<KeyValuePair<string, string>>();

        /// <summary>Bizim audit nesnemize dokunuldu: nöbetçi hemen kontrol etsin.</summary>
        public bool TouchesOurAudit;

        /// <summary>
        /// Sunucu ayarı değiştirildi (sp_configure / RECONFIGURE): yapan kişi. Kayıt hangi ayarın değiştiğini söylemeyebilir
        /// (EventType null olur); ayarı karşılaştırma bulur ve yapanı buradan alır (SqlConfigTrail).
        /// </summary>
        public SqlConfigActor Config;
    }

    /// <summary>
    /// SQL Server Audit → Application log (olay 33205) kaydını çözer ve olay tipine eşler.
    /// Kayıt metni "alan_adı:değer" satırlarından oluşur; statement çok satırlı olabilir.
    /// </summary>
    internal static class SqlAuditEvents
    {
        public const string AuditName = "SecurityGuard_Audit";
        public const string SpecName = "SecurityGuard_AuditSpec";

        // Audit kaydındaki alanlar (SQL 2012-2022). Bilinmeyen satır bir önceki alanın devamı sayılır (çok satırlı statement).
        private static readonly HashSet<string> KnownFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "event_time", "sequence_number", "action_id", "succeeded", "is_column_permission", "session_id", "server_principal_id",
            "database_principal_id", "target_server_principal_id", "target_database_principal_id", "object_id", "user_defined_event_id",
            "transaction_id", "class_type", "duration_milliseconds", "response_rows", "affected_rows", "client_ip", "permission_bitmask",
            "sequence_group_id", "session_server_principal_name", "server_principal_name", "server_principal_sid", "database_principal_name",
            "target_server_principal_name", "target_server_principal_sid", "target_database_principal_name", "server_instance_name",
            "database_name", "schema_name", "object_name", "statement", "additional_information", "user_defined_information",
            "application_name", "connection_id", "data_sensitivity_information", "host_name", "session_context", "client_tls_version",
            "database_transaction_id", "ledger_start_sequence_number", "is_local_secondary_replica", "external_policy_permissions_checked",
            "obo_middle_tier_app_id"
        };

        private static readonly Regex FieldLine = new Regex(@"^\s*([a-z_]+):(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex Dangerous = new Regex(@"xp_cmdshell|clr\s+enabled|ole\s+automation|ad\s+hoc\s+distributed|external\s+scripts",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static Dictionary<string, string> Parse(string text)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return fields;
            int start = text.IndexOf("event_time:", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return fields;

            string current = null;
            foreach (var raw in text.Substring(start).Replace("\r\n", "\n").Split('\n'))
            {
                var m = FieldLine.Match(raw);
                if (m.Success && KnownFields.Contains(m.Groups[1].Value))
                {
                    current = m.Groups[1].Value;
                    fields[current] = m.Groups[2].Value.Trim();
                }
                else if (current != null)
                {
                    fields[current] = (fields[current] + "\n" + raw).Trim();
                }
            }
            return fields;
        }

        /// <summary>Kaydı olay tipine eşler; izlenmeyen / bizim yaptığımız işlemse null.</summary>
        public static SqlAuditMatch Map(Dictionary<string, string> f, string instance)
        {
            string action = Get(f, "action_id").ToUpperInvariant();
            string cls = Get(f, "class_type").ToUpperInvariant();
            string obj = Get(f, "object_name");
            string statement = Get(f, "statement");
            string who = Get(f, "server_principal_name");
            if (who.Length == 0) who = Get(f, "session_server_principal_name");
            if (action.Length == 0) return null;
            if (Get(f, "succeeded").Equals("false", StringComparison.OrdinalIgnoreCase)) return null; // başarısız komut değişiklik yapmadı

            // SecurityGuard'ın kendi işlemleri (audit kurma / yeniden başlatma) alarm üretmez
            bool ours = who.Equals(GuardConstants.SqlLoginName, StringComparison.OrdinalIgnoreCase)
                        && Get(f, "application_name").Equals("SecurityGuard", StringComparison.OrdinalIgnoreCase);

            bool ourAudit = obj.Equals(AuditName, StringComparison.OrdinalIgnoreCase) || obj.Equals(SpecName, StringComparison.OrdinalIgnoreCase);
            string type = null, summary = null;
            string verb = Verb(action);

            if (ourAudit)
            {
                if (ours) return null;
                if (action == "AL" || action == "DR" || action == "CR")
                {
                    type = "Sql.AuditStopped";
                    summary = "SecurityGuard SQL izlemesi (" + obj + ") " + (action == "DR" ? "silindi" : "değiştirildi / durduruldu") + " (" + who + ")";
                }
            }
            else if (ours)
            {
                return null;
            }
            else if (IsPasswordAction(action))
            {
                type = "Sql.PasswordChanged";
                summary = "SQL login şifresi " + (action == "PWR" ? "sıfırlandı" : "değişti") + ": " + obj + " (" + who + ")";
            }
            else if (cls == "SL" || cls == "WL" || cls == "WG")
            {
                if (obj.Equals("sa", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(statement, @"\b(ENABLE|DISABLE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    type = "Sql.SaStateChanged";
                    summary = "sa hesabı " + (Regex.IsMatch(statement, @"\bDISABLE\b", RegexOptions.IgnoreCase) ? "devre dışı bırakıldı" : "etkinleştirildi") + " (" + who + ")";
                }
                else if (action == "CR" || action == "DR" || action == "AL")
                {
                    type = "Sql.LoginChanged";
                    summary = "SQL login " + verb + ": " + obj + " (" + who + ")";
                }
            }
            else if (action == "APRL" || action == "DPRL")
            {
                type = "Sql.ServerRoleChanged";
                string member = Get(f, "target_server_principal_name");
                summary = (action == "APRL" ? "Sunucu rolüne üye eklendi: " : "Sunucu rolünden üye çıkarıldı: ") + member + " → " + obj + " (" + who + ")";
            }
            else if ((action == "G" || action == "GWG" || action == "D" || action == "R") && (cls == "SV" || cls == "SL" || cls == "SG" || cls == "WL"))
            {
                type = "Sql.PermissionChanged";
                summary = "Sunucu yetkisi " + (action == "D" ? "engellendi (DENY)" : action == "R" ? "geri alındı (REVOKE)" : "verildi (GRANT)") +
                          (obj.Length > 0 ? ": " + obj : "") + " (" + who + ")";
            }
            else if (cls == "DB")
            {
                if (action == "DR")
                {
                    type = "Sql.DatabaseDropped";
                    summary = "Veritabanı silindi: " + DbObject(f, obj) + " (" + who + ")";
                }
                else if (action == "AL" && Regex.IsMatch(statement, @"ENCRYPTION\s*=?\s*ON|SET\s+ENCRYPTION\s+ON", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    type = "Sql.TdeEnabled";
                    summary = "Veritabanı şifrelemesi (TDE) açıldı: " + DbObject(f, obj) + " (" + who + ")";
                }
                else if (action == "CR" || action == "AL")
                {
                    type = "Sql.DatabaseChanged";
                    summary = "Veritabanı " + verb + ": " + DbObject(f, obj) + " (" + who + ")";
                }
            }
            else if (cls == "CR" || cls == "AK" || cls == "SK" || cls == "MK" || cls == "DK" || Regex.IsMatch(statement,
                         @"\b(CERTIFICATE|ASYMMETRIC\s+KEY|SYMMETRIC\s+KEY|MASTER\s+KEY|DATABASE\s+ENCRYPTION\s+KEY)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                if (action == "CR" || action == "DR" || action == "AL" || action == "BA" || action == "IMP" || action == "EXP")
                {
                    type = "Sql.KeyChanged";
                    summary = "Sertifika / anahtar işlemi (" + verb + "): " + (obj.Length > 0 ? obj : "?") + " [" + Db(f, "") + "] (" + who + ")";
                }
            }
            else if (Regex.IsMatch(statement, @"\bCREDENTIAL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                type = "Sql.CredentialChanged";
                summary = "Credential " + verb + ": " + obj + " (" + who + ")";
            }
            else if (Regex.IsMatch(statement, @"sp_addlinkedserver|sp_dropserver|sp_addserver|sp_addlinkedsrvlogin", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                type = "Sql.LinkedServerChanged";
                summary = "Linked server değişti: " + obj + " (" + who + ")";
            }
            else if (Regex.IsMatch(statement, @"\bTRIGGER\b[\s\S]*\bON\s+ALL\s+SERVER\b|\bTRIGGER\b[\s\S]*\bALL\s+SERVER\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                type = "Sql.ServerTriggerChanged";
                summary = "Sunucu trigger'ı " + verb + ": " + obj + " (" + who + ")";
            }
            else if (Regex.IsMatch(statement, @"sp_cycle_errorlog", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                type = "Sql.ErrorLogCleared";
                summary = "SQL error log döndürüldü (sp_cycle_errorlog) (" + who + ")";
            }
            else if (Regex.IsMatch(statement, @"sp_configure", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) && Dangerous.IsMatch(statement))
            {
                type = "Sql.DangerousConfigChanged";
                summary = "Tehlikeli SQL ayarı değişti: " + Dangerous.Match(statement).Value + " (" + who + ")";
            }

            // Ayar değişikliği: Audit "ALTER SETTINGS" (ALST) kaydında komut çoğu zaman sadece RECONFIGURE'dur
            bool configTouched = !ourAudit && (action == "ALST" || Regex.IsMatch(statement, @"\bRECONFIGURE\b|sp_configure", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            if (type == null && !configTouched) return null;

            var match = new SqlAuditMatch
            {
                Config = !configTouched ? null : new SqlConfigActor
                {
                    Who = who,
                    Ip = CleanIp(Get(f, "client_ip")),
                    Application = Get(f, "application_name"),
                    Host = Get(f, "host_name"),
                    Statement = Shorten(statement, 300)
                },
                EventType = type,
                Summary = summary,
                UserName = who.Length == 0 ? null : who,
                IpAddress = CleanIp(Get(f, "client_ip")),
                TouchesOurAudit = ourAudit
            };
            Add(match, "Instance", instance);
            Add(match, "Veritabanı", Get(f, "database_name"));
            Add(match, "Nesne", obj);
            Add(match, "Uygulama", Get(f, "application_name"));
            Add(match, "Bilgisayar", Get(f, "host_name"));
            Add(match, "Komut", Shorten(statement, 1000));
            Add(match, "Audit işlemi", action + " / " + cls);
            return match;
        }

        private static bool IsPasswordAction(string action)
        {
            return action == "PWC" || action == "PWCS" || action == "PWR" || action == "PWU" || action == "PWMC" || action == "PWPL";
        }

        private static string Verb(string action)
        {
            switch (action)
            {
                case "CR": return "oluşturuldu";
                case "DR": return "silindi";
                case "AL": return "değiştirildi";
                case "BA": return "yedeklendi";
                case "IMP": return "içe aktarıldı";
                case "EXP": return "dışa aktarıldı";
                default: return action;
            }
        }

        /// <summary>Veritabanı olayında hedef veritabanı: object_name (database_name komutun çalıştığı bağlamdır, ör. master).</summary>
        private static string DbObject(Dictionary<string, string> f, string obj)
        {
            return obj.Length > 0 ? obj : Get(f, "database_name");
        }

        private static string Db(Dictionary<string, string> f, string fallback)
        {
            string db = Get(f, "database_name");
            return db.Length > 0 ? db : fallback;
        }

        private static string Get(Dictionary<string, string> f, string key)
        {
            string v;
            return f.TryGetValue(key, out v) && v != null ? v.Trim() : "";
        }

        private static void Add(SqlAuditMatch m, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) m.Details.Add(new KeyValuePair<string, string>(name, value));
        }

        private static string CleanIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip) || ip.Equals("local machine", StringComparison.OrdinalIgnoreCase) || ip.StartsWith("<", StringComparison.Ordinal))
                return null;
            return ip.Trim();
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
