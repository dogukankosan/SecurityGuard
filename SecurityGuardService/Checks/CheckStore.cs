using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core.Data;

namespace SecurityGuardService.Checks
{
    /// <summary>Tarama sonuçlarının kaydı (CheckRuns + CheckResults) ve görmezden gelinen maddeler (CheckIgnores).</summary>
    internal sealed class CheckStore
    {
        /// <summary>Tür başına saklanan tarama sayısı (eskiler silinir).</summary>
        private const int KeepRuns = 30;

        private readonly GuardDatabase _db;

        public CheckStore(GuardDatabase db)
        {
            _db = db;
        }

        public Task SaveRunAsync(string scope, string runId, string trigger, DateTime startedUtc, DateTime finishedUtc, IList<CheckItemDto> items)
        {
            return _db.Writer.EnqueueAsync(c =>
            {
                c.Execute(@"INSERT INTO CheckRuns (RunId, Scope, TriggerText, StartedUtc, FinishedUtc, Passed, Info, Warning, Critical, Errors)
                            VALUES (@r, @s, @t, @st, @f, @p, @i, @w, @c, @e)",
                    ("@r", runId), ("@s", scope), ("@t", trigger), ("@st", DbTime.ToDb(startedUtc)), ("@f", DbTime.ToDb(finishedUtc)),
                    ("@p", items.Count(x => x.Status == CheckStatus.Gecti)), ("@i", items.Count(x => x.Status == CheckStatus.Bilgi)),
                    ("@w", items.Count(x => x.Status == CheckStatus.Uyari)), ("@c", items.Count(x => x.Status == CheckStatus.Kritik)),
                    ("@e", items.Count(x => x.Status == CheckStatus.Hata)));

                string now = DbTime.Now();
                for (int i = 0; i < items.Count; i++)
                {
                    var x = items[i];
                    c.Execute(@"INSERT INTO CheckResults (RunId, CheckId, Category, Status, Title, Detail, HowToFix, CreatedUtc, Summary, Script, ScriptLanguage, SortOrder)
                                VALUES (@r, @id, @a, @st, @t, @d, @h, @now, @su, @sc, @sl, @o)",
                        ("@r", runId), ("@id", x.CheckId), ("@a", x.Area), ("@st", (int)x.Status), ("@t", x.Title), ("@d", x.Detail),
                        ("@h", x.HowToFix), ("@now", now), ("@su", x.Summary), ("@sc", x.Script), ("@sl", x.ScriptLanguage), ("@o", i));
                }

                // Eski taramalar
                c.Execute(@"DELETE FROM CheckResults WHERE RunId IN (
                                SELECT RunId FROM CheckRuns WHERE Scope = @s ORDER BY StartedUtc DESC LIMIT -1 OFFSET @k)",
                    ("@s", scope), ("@k", KeepRuns));
                c.Execute(@"DELETE FROM CheckRuns WHERE RunId IN (
                                SELECT RunId FROM CheckRuns WHERE Scope = @s ORDER BY StartedUtc DESC LIMIT -1 OFFSET @k)",
                    ("@s", scope), ("@k", KeepRuns));
            });
        }

        /// <summary>Son tarama (IsNew: bir önceki taramaya göre yeni çıkan / kötüleşen). Hiç yoksa RunId null.</summary>
        public CheckRunDto LoadLatest(string scope)
        {
            return _db.Read(c =>
            {
                var runs = new List<string[]>();
                using (var cmd = c.Create("SELECT RunId, TriggerText, StartedUtc, FinishedUtc FROM CheckRuns WHERE Scope = @s ORDER BY StartedUtc DESC LIMIT 2", ("@s", scope)))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        runs.Add(new[] { r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetString(3) });
                }

                var dto = new CheckRunDto { Scope = scope, Items = new List<CheckItemDto>() };
                if (runs.Count == 0) return dto;

                dto.RunId = runs[0][0];
                dto.Trigger = runs[0][1];
                dto.StartedUtc = runs[0][2];
                dto.FinishedUtc = runs[0][3];

                var previous = runs.Count > 1 ? LoadStatuses(c, runs[1][0]) : null;
                var ignores = LoadIgnores(c);

                using (var cmd = c.Create(@"SELECT CheckId, Category, Status, Title, Summary, Detail, HowToFix, Script, ScriptLanguage
                                            FROM CheckResults WHERE RunId = @r ORDER BY SortOrder", ("@r", dto.RunId)))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var item = new CheckItemDto
                        {
                            CheckId = r.GetString(0),
                            Area = r.IsDBNull(1) ? null : r.GetString(1),
                            Status = (CheckStatus)r.GetInt64(2),
                            Title = r.GetString(3),
                            Summary = r.IsDBNull(4) ? null : r.GetString(4),
                            Detail = r.IsDBNull(5) ? null : r.GetString(5),
                            HowToFix = r.IsDBNull(6) ? null : r.GetString(6),
                            Script = r.IsDBNull(7) ? null : r.GetString(7),
                            ScriptLanguage = r.IsDBNull(8) ? null : r.GetString(8)
                        };
                        string note;
                        if (ignores.TryGetValue(item.CheckId, out note))
                        {
                            item.IsIgnored = true;
                            item.IgnoreNote = note;
                        }
                        item.IsNew = IsWorse(item, previous);
                        dto.Items.Add(item);
                    }
                }
                return dto;
            });
        }

        /// <summary>Bir önceki taramada yoktu veya daha iyi durumdaydı (sadece Uyarı / Kritik için).</summary>
        internal static bool IsWorse(CheckItemDto item, IDictionary<string, CheckStatus> previous)
        {
            if (previous == null) return false; // ilk tarama: kıyaslanacak bir şey yok
            if (item.Status != CheckStatus.Uyari && item.Status != CheckStatus.Kritik) return false;
            CheckStatus before;
            if (!previous.TryGetValue(item.CheckId, out before)) return true;
            return Rank(item.Status) > Rank(before);
        }

        private static int Rank(CheckStatus s)
        {
            switch (s)
            {
                case CheckStatus.Kritik: return 3;
                case CheckStatus.Uyari: return 2;
                case CheckStatus.Hata: return 1; // ölçülemedi: kötüleşme sayılmasın diye uyarının altında
                default: return 0;
            }
        }

        /// <summary>Son taramadaki maddelerin durumu (yeni kritik bulgu tespiti için).</summary>
        public Dictionary<string, CheckStatus> LoadLatestStatuses(string scope)
        {
            return _db.Read(c =>
            {
                string runId;
                using (var cmd = c.Create("SELECT RunId FROM CheckRuns WHERE Scope = @s ORDER BY StartedUtc DESC LIMIT 1", ("@s", scope)))
                    runId = cmd.ExecuteScalar() as string;
                return runId == null ? null : LoadStatuses(c, runId);
            });
        }

        private static Dictionary<string, CheckStatus> LoadStatuses(System.Data.SQLite.SQLiteConnection c, string runId)
        {
            var map = new Dictionary<string, CheckStatus>(StringComparer.Ordinal);
            using (var cmd = c.Create("SELECT CheckId, Status FROM CheckResults WHERE RunId = @r", ("@r", runId)))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read()) map[r.GetString(0)] = (CheckStatus)r.GetInt64(1);
            }
            return map;
        }

        public Dictionary<string, string> LoadIgnores()
        {
            return _db.Read(LoadIgnores);
        }

        private static Dictionary<string, string> LoadIgnores(System.Data.SQLite.SQLiteConnection c)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var cmd = c.Create("SELECT CheckId, Note FROM CheckIgnores"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read()) map[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
            }
            return map;
        }

        public Task SetIgnoreAsync(string checkId, bool ignore, string note)
        {
            return _db.Writer.EnqueueAsync(c =>
            {
                if (ignore)
                    c.Execute(@"INSERT INTO CheckIgnores (CheckId, Note, CreatedUtc) VALUES (@id, @n, @t)
                                ON CONFLICT(CheckId) DO UPDATE SET Note = excluded.Note, CreatedUtc = excluded.CreatedUtc",
                        ("@id", checkId), ("@n", note), ("@t", DbTime.Now()));
                else
                    c.Execute("DELETE FROM CheckIgnores WHERE CheckId = @id", ("@id", checkId));
            });
        }
    }
}
