using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using SecurityGuardService.Core.Security;

namespace SecurityGuardService.Core.Data
{
    /// <summary>
    /// Yöneticinin SQLite Sorgu ekranından EventLog'da yaptığı silme / değiştirmelerin imzalı kaydı.
    /// Bütünlük kontrolü bunları "yönetici tarafından" olarak ayırır, oynama alarmı vermez.
    /// Kayıtlar HMAC ile imzalanır: DB dosyasına dışarıdan eklenen sahte kayıt kabul edilmez,
    /// o silme / değişiklik yine oynama olarak görünür.
    /// </summary>
    internal static class EventLogAdminChanges
    {
        public const string Deleted = "deleted";
        public const string Modified = "modified";

        /// <summary>"Mevcut durumu kabul et" ile onaylanan sorunlu satır (değişmiş içerik / kopuk bağ).</summary>
        public const string Accepted = "accepted";

        /// <summary>"Mevcut durumu kabul et" ile onaylanan silinmiş kayıt (SQLite ekranı dışında, ör. eski sürümde silinmiş).</summary>
        public const string AcceptedDeleted = "accepted-deleted";

        /// <summary>EventLog'un anlık içeriği: Id → tüm alanların birleşimi (tablo yoksa boş).</summary>
        public static Dictionary<long, string> Snapshot(SQLiteConnection conn)
        {
            var map = new Dictionary<long, string>();
            if (conn.Scalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'EventLog'") == 0) return map;
            using (var cmd = conn.Create("SELECT Id, TimeUtc, Source, EventType, Severity, UserName, IpAddress, Summary, Detail, PrevHmac, Hmac FROM EventLog"))
            using (var r = cmd.ExecuteReader())
            {
                var values = new object[11];
                while (r.Read())
                {
                    r.GetValues(values);
                    var parts = new string[10];
                    for (int i = 1; i < 11; i++) parts[i - 1] = values[i] is DBNull ? "\u0002" : Convert.ToString(values[i], CultureInfo.InvariantCulture);
                    map[Convert.ToInt64(values[0], CultureInfo.InvariantCulture)] = string.Join("\u0001", parts);
                }
            }
            return map;
        }

        /// <summary>İki anlık görüntü arasındaki silinen / değişen kayıtları imzalayıp yazar. (silinen, değişen) sayısı döner.</summary>
        public static Tuple<int, int> Record(SQLiteConnection conn, HmacChain chain, Dictionary<long, string> before, Dictionary<long, string> after, string userName)
        {
            int deleted = 0, modified = 0;
            string now = DbTime.Now();
            foreach (var pair in before)
            {
                string current;
                string type;
                if (!after.TryGetValue(pair.Key, out current)) type = Deleted;
                else if (current != pair.Value) type = Modified;
                else continue;

                if (type == Deleted) deleted++;
                else modified++;
                Write(conn, chain, pair.Key, type, now, userName);
            }
            return Tuple.Create(deleted, modified);
        }

        public static void Write(SQLiteConnection conn, HmacChain chain, long id, string type, string time, string userName)
        {
            conn.Execute(@"INSERT OR REPLACE INTO EventLogAdminChanges (EventLogId, ChangeType, TimeUtc, UserName, Hmac)
                           VALUES (@id, @t, @time, @u, @h)",
                ("@id", id), ("@t", type), ("@time", time), ("@u", userName), ("@h", Sign(chain, id, type, time, userName)));
        }

        /// <summary>İmzası doğru olan kayıtlar (tip → Id kümesi).</summary>
        public static Dictionary<string, HashSet<long>> LoadVerified(SQLiteConnection conn, HmacChain chain)
        {
            var map = new Dictionary<string, HashSet<long>> { { Deleted, new HashSet<long>() }, { Modified, new HashSet<long>() }, { Accepted, new HashSet<long>() }, { AcceptedDeleted, new HashSet<long>() } };
            if (chain == null) return map;
            using (var cmd = conn.Create("SELECT EventLogId, ChangeType, TimeUtc, UserName, Hmac FROM EventLogAdminChanges"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    long id = r.GetInt64(0);
                    string type = r.GetString(1);
                    string user = r.IsDBNull(3) ? null : r.GetString(3);
                    string hmac = r.IsDBNull(4) ? null : r.GetString(4);
                    HashSet<long> set;
                    if (hmac != null && map.TryGetValue(type, out set) && hmac == Sign(chain, id, type, r.GetString(2), user))
                        set.Add(id);
                }
            }
            return map;
        }

        private static string Sign(HmacChain chain, long id, string type, string time, string user)
        {
            return chain == null ? null : chain.Compute(null, "eventlog-admin-change", id.ToString(CultureInfo.InvariantCulture), type, time, user);
        }
    }
}
