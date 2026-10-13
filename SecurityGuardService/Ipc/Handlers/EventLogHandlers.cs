using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Engine.Events;
using SecurityGuardService.Engine.Rules;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class EventLogHandlers
    {
        private const string Module = "Olay";
        private const int DefaultMaxRows = 1000;
        private const int MaxMaxRows = 20000;
        
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.EventLogQuery, true, true, r => Task.Run(() => Query(r)));
            router.Register(Commands.EventLogVerify, true, true, r => Task.Run(() => Verify(r)));

            // Eski sürümden / bilinen müdahaleden kalan sorunları kabul: yönetici şifresi tekrar istenir
            router.Register(Commands.EventLogAcceptIntegrity, true, true, async r =>
            {
                var req = r.Payload<PasswordConfirmRequest>();
                if (req == null || string.IsNullOrEmpty(req.Password))
                    throw new GuardException(ErrorCodes.Validation, "Onay için yönetici şifresini girin.");
                await r.Guard.Admin.LoginAsync(r.Session.UserName, req.Password).ConfigureAwait(false);
                await r.Guard.Events.AcceptCurrentStateAsync(r.Session.UserName).ConfigureAwait(false);
                return await Task.Run(() => PipeResponse.Ok(r.Guard.Events.Verify())).ConfigureAwait(false);
            });
        }

        private static PipeResponse Query(RequestContext r)
        {
            var q = r.Payload<EventLogQuery>() ?? new EventLogQuery { MinSeverity = -1 };
            string from = ParseTime(q.FromUtc, DateTime.UtcNow.AddDays(-7));
            string to = ParseTime(q.ToUtc, DateTime.UtcNow.AddMinutes(1));
            int max = q.MaxRows <= 0 ? DefaultMaxRows : Math.Min(q.MaxRows, MaxMaxRows);
            string search = string.IsNullOrWhiteSpace(q.Search) ? null : "%" + AuditTrailHandlers.EscapeLike(q.Search.Trim()) + "%";

            // Grup filtresi katalogdaki olay tiplerine çevrilir (EventLog'da grup kolonu yok)
            List<string> types = null;
            if (!string.IsNullOrEmpty(q.Category))
                types = EventCatalog.All.Where(d => d.Category == q.Category).Select(d => d.Type).ToList();

            var byType = EventCatalog.All.ToDictionary(d => d.Type);
            var rows = r.Guard.Database.Read(c =>
            {
                string typeFilter = types == null ? "" :
                    " AND EventType IN (" + string.Join(",", types.Select((t, i) => "@t" + i)) + ")";
                var args = new List<(string, object)> { ("@from", from), ("@to", to), ("@sev", q.MinSeverity), ("@s", search), ("@max", max + 1) };
                if (types != null) args.AddRange(types.Select((t, i) => ("@t" + i, (object)t)));

                var list = new List<EventLogRow>();
                using (var cmd = c.Create(
                    @"SELECT Id, TimeUtc, EventType, Severity, Source, UserName, IpAddress, Summary, Detail, MailSent FROM EventLog
                      WHERE TimeUtc >= @from AND TimeUtc <= @to AND Severity >= @sev" + typeFilter + @"
                        AND (@s IS NULL OR Summary LIKE @s ESCAPE '\' OR Detail LIKE @s ESCAPE '\' OR UserName LIKE @s ESCAPE '\'
                             OR IpAddress LIKE @s ESCAPE '\' OR EventType LIKE @s ESCAPE '\')
                      ORDER BY Id DESC LIMIT @max", args.ToArray()))
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        string type = rd.GetString(2);
                        EventDefinition def;
                        byType.TryGetValue(type, out def);
                        list.Add(new EventLogRow
                        {
                            Id = rd.GetInt64(0),
                            TimeUtc = rd.GetString(1),
                            EventType = type,
                            EventName = def == null ? type : def.Name,
                            Category = def == null ? "" : def.Category,
                            Severity = rd.GetInt32(3),
                            Source = rd.GetString(4),
                            UserName = rd.IsDBNull(5) ? null : rd.GetString(5),
                            IpAddress = rd.IsDBNull(6) ? null : rd.GetString(6),
                            Summary = rd.GetString(7),
                            Detail = rd.IsDBNull(8) ? null : rd.GetString(8),
                            MailSent = rd.GetInt64(9) != 0
                        });
                    }
                }
                return list;
            });

            bool truncated = rows.Count > max;
            if (truncated) rows.RemoveAt(rows.Count - 1);
            return PipeResponse.Ok(new EventLogResult
            {
                Rows = rows,
                Truncated = truncated,
                Categories = EventCatalog.All.Select(d => d.Category).Distinct().ToList()
            });
        }

        private static PipeResponse Verify(RequestContext r)
        {
            var result = r.Guard.Events.Verify();
            AppLog.Info(Module, "Bütünlük kontrolü: " + result.CheckedRows + " satır, " + result.ModifiedRows + " değişmiş, " + result.Gaps + " kopukluk.");
            AuditTrail.Record(r.Guard.Database, r.Session.UserName, "Olay logu bütünlük kontrolü",
                result.IsIntact ? "Sağlam (" + result.CheckedRows + " kayıt)" : result.ModifiedRows + " değişmiş satır, " + result.Gaps + " kopukluk");
            return PipeResponse.Ok(result);
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
    }
}
