using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class AuditTrailHandlers
    {
        private const int DefaultMaxRows = 1000;
        private const int MaxMaxRows = 20000;

        public static void Register(CommandRouter router)
        {
            router.Register(Commands.AuditTrailQuery, true, true, r => Task.FromResult(Query(r)));
        }

        private static PipeResponse Query(RequestContext r)
        {
            var q = r.Payload<AuditTrailQuery>();
            string from = ParseTime(q.FromUtc, DateTime.UtcNow.AddDays(-7));
            string to = ParseTime(q.ToUtc, DateTime.UtcNow.AddMinutes(1));
            int max = q.MaxRows <= 0 ? DefaultMaxRows : Math.Min(q.MaxRows, MaxMaxRows);
            string search = string.IsNullOrWhiteSpace(q.Search) ? null : "%" + EscapeLike(q.Search.Trim()) + "%";

            var rows = r.Guard.Database.Read(c =>
            {
                var list = new List<AuditTrailRow>();
                using (var cmd = c.Create(
                    @"SELECT Id, TimeUtc, UserName, Action, Detail FROM AuditTrail
                      WHERE TimeUtc >= @from AND TimeUtc <= @to
                        AND (@s IS NULL OR UserName LIKE @s ESCAPE '\' OR Action LIKE @s ESCAPE '\' OR Detail LIKE @s ESCAPE '\')
                      ORDER BY Id DESC LIMIT @max",
                    ("@from", from), ("@to", to), ("@s", search), ("@max", max + 1)))
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                        list.Add(new AuditTrailRow
                        {
                            Id = rd.GetInt64(0),
                            TimeUtc = rd.GetString(1),
                            UserName = rd.IsDBNull(2) ? null : rd.GetString(2),
                            Action = rd.GetString(3),
                            Detail = rd.IsDBNull(4) ? null : rd.GetString(4)
                        });
                }
                return list;
            });

            bool truncated = rows.Count > max;
            if (truncated) rows.RemoveAt(rows.Count - 1);
            return PipeResponse.Ok(new AuditTrailResult { Rows = rows, Truncated = truncated });
        }

        private static string ParseTime(string text, DateTime fallbackUtc)
        {
            if (string.IsNullOrEmpty(text)) return DbTime.ToDb(fallbackUtc);
            try
            {
                return DbTime.ToDb(UtcTime.Parse(text));
            }
            catch (FormatException)
            {
                throw new GuardException(ErrorCodes.Validation, "Geçersiz tarih.");
            }
        }

        /// <summary>LIKE içindeki % ve _ karakterleri joker değil, metin olarak aransın.</summary>
        internal static string EscapeLike(string text)
        {
            return text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }
    }
}
