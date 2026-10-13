using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Rules;

namespace SecurityGuardService.Engine.Reports
{
    /// <summary>
    /// Panel (ana sayfa) verisi: her bölüm ayrı hesaplanır, biri hata verirse o kutucuk "okunamadı" olur, panel yine açılır.
    /// Sadece okur; ağır işlem yapmaz (bütünlük için son kontrolün sonucu gösterilir, yeniden doğrulanmaz).
    /// </summary>
    internal static class Dashboard
    {
        private const string Module = "Panel";
        private const int Ok = 0, Info = 1, Warn = 2, Crit = 3;
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        public static DashboardDto Build(GuardContext g)
        {
            var dto = new DashboardDto
            {
                MachineName = Environment.MachineName,
                Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                ServiceStartedUtc = DbTime.ToDb(Process.GetCurrentProcess().StartTime.ToUniversalTime()),
                Tiles = new List<DashboardTileDto>(),
                RecentEvents = new List<DashboardEventDto>(),
                Todos = new List<DashboardTodoDto>()
            };
            string since = DbTime.ToDb(DateTime.UtcNow.AddDays(-1));

            Tile(dto, "Olaylar (24 saat)", DashboardPages.EventLog, () =>
            {
                var counts = g.Database.Read(c =>
                {
                    var map = new Dictionary<long, long>();
                    using (var cmd = c.Create("SELECT Severity, COUNT(*) FROM EventLog WHERE TimeUtc >= @s GROUP BY Severity", ("@s", since)))
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) map[r.GetInt64(0)] = r.GetInt64(1);
                    return map;
                });
                long crit = counts.ContainsKey(2) ? counts[2] : 0, warn = counts.ContainsKey(1) ? counts[1] : 0;
                return T(crit == 0 ? "Kritik yok" : crit + " kritik", warn + " uyarı, " + (counts.ContainsKey(0) ? counts[0] : 0) + " bilgi olayı", crit > 0 ? Crit : warn > 0 ? Info : Ok);
            });

            Tile(dto, "Banlar", DashboardPages.BanList, () =>
            {
                var row = g.Database.Read(c => new[]
                {
                    c.Scalar<long>("SELECT COUNT(*) FROM BannedIps"),
                    c.Scalar<long>("SELECT COUNT(*) FROM BannedIps WHERE BannedUtc >= @s", ("@s", since)),
                    c.Scalar<long>("SELECT COUNT(*) FROM EventLog WHERE TimeUtc >= @s AND EventType IN (@a, @b)", ("@s", since), ("@a", EventCatalog.RdpLoginFailed), ("@b", EventCatalog.SqlLoginFailed))
                });
                return T(row[0] + " banlı IP", "24 saatte " + row[1] + " yeni ban, " + row[2] + " başarısız giriş", row[1] > 0 ? Warn : Ok);
            });

            Tile(dto, "Güvenlik önerileri", DashboardPages.Security, () => Checks(g, CheckScopes.Security, dto, DashboardPages.Security, "Güvenlik"));
            Tile(dto, "Performans önerileri", DashboardPages.Performance, () => Checks(g, CheckScopes.Performance, dto, DashboardPages.Performance, "Performans"));

            Tile(dto, "SQL yedekleri", DashboardPages.Backup, () =>
            {
                var cfg = g.Backup.LoadConfig();
                var last = g.Database.Read(c =>
                {
                    using (var cmd = c.Create("SELECT StartedUtc, Status, Summary FROM BackupRuns ORDER BY StartedUtc DESC LIMIT 1"))
                    using (var r = cmd.ExecuteReader())
                        return r.Read() ? new[] { r.GetString(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2) } : null;
                });
                if (!cfg.Enabled && last == null)
                {
                    Todo(dto, Warn, "SecurityGuard yedeklemesi kapalı. Sunucuda başka bir yedek yoksa açın (Güvenlik Önerileri → Yedekler maddesine bakın).", DashboardPages.Backup);
                    return T("Kapalı", "SecurityGuard yedek almıyor", Info);
                }
                if (last == null) return T("Henüz yok", "İlk yedek planlanan saatte alınacak", Info);
                string status = last[1];
                int level = status == "Tamamlandı" ? Ok : status == "Sürüyor" ? Info : Crit;
                if (level == Crit) Todo(dto, Crit, "Son SQL yedeği başarısız (" + status + "): " + last[2], DashboardPages.Backup);
                var age = DateTime.UtcNow - DbTime.FromDb(last[0]);
                if (cfg.Enabled && level == Ok && age > TimeSpan.FromDays(2))
                {
                    level = Warn;
                    Todo(dto, Warn, "Son başarılı SQL yedeği " + (int)age.TotalDays + " gün önce.", DashboardPages.Backup);
                }
                return T(status, Local(last[0]) + ": " + last[2], level);
            });

            Tile(dto, "Olay logu bütünlüğü", DashboardPages.EventLog, () =>
            {
                var v = g.Events.LastVerify;
                if (v == null) return T("Kontrol edilmedi", "Her akşam rapor saatinde otomatik kontrol edilir", Info);
                if (!v.ChainEnabled) return T("Kontrol edilemiyor", "İmza anahtarı okunamıyor", Warn);
                if (!v.IsIntact)
                {
                    Todo(dto, Crit, "Olay logunda dışarıdan oynama tespit edildi (" + v.ModifiedRows + " değişmiş kayıt, " + v.Gaps + " kopukluk). Olay Logu → Bütünlük Kontrolü.", DashboardPages.EventLog);
                    return T("OYNAMA VAR", v.ModifiedRows + " değişmiş kayıt, " + v.Gaps + " kopukluk (" + Local(DbTime.ToDb(g.Events.LastVerifyUtc.Value)) + ")", Crit);
                }
                return T("Sağlam", v.CheckedRows + " kayıt, son kontrol " + Local(DbTime.ToDb(g.Events.LastVerifyUtc.Value)), Ok);
            });

            Tile(dto, "SQL Server izleme", DashboardPages.SqlConnection, () =>
            {
                var all = g.Sql.List();
                var configured = all.Where(i => i.IsConfigured && i.IsEnabled).ToList();
                if (configured.Count == 0)
                {
                    if (all.Any(i => i.Detected))
                    {
                        Todo(dto, Warn, "Sunucuda SQL Server var ama SecurityGuard'a bağlantısı tanımlı değil: SQL saldırıları izlenmiyor.", DashboardPages.SqlConnection);
                        return T("Bağlantı yok", "SQL Server izlenmiyor", Warn);
                    }
                    return T("SQL Server yok", "Sunucuda SQL Server bulunamadı", Ok);
                }
                var notRunning = configured.Where(i => !g.SqlAudit.GetState(i.InstanceName).Running).Select(i => i.InstanceName).ToList();
                if (notRunning.Count > 0)
                {
                    Todo(dto, Warn, "SQL izlemesi çalışmıyor: " + string.Join(", ", notRunning) + ". SQL Bağlantısı sayfasından kurun.", DashboardPages.SqlConnection);
                    return T("Eksik", "İzleme çalışmıyor: " + string.Join(", ", notRunning), Warn);
                }
                return T("Çalışıyor", string.Join(", ", configured.Select(i => i.InstanceName)), Ok);
            });

            Tile(dto, "Mail", DashboardPages.Mail, () =>
            {
                if (!g.Mail.IsConfigured)
                {
                    Todo(dto, Crit, "Mail ayarı yapılmamış: kritik olaylarda kimseye haber gidemez.", DashboardPages.Mail);
                    return T("Ayarlanmadı", "Kritik olaylar mail ile bildirilemiyor", Crit);
                }
                if (!g.Mail.ListRecipients().Any(r => r.IsEnabled))
                {
                    Todo(dto, Crit, "Aktif mail alıcısı yok: kritik olaylar kimseye gitmiyor.", DashboardPages.Mail);
                    return T("Alıcı yok", "Aktif alıcı tanımlı değil", Crit);
                }
                var counts = g.Database.Read(c => new[]
                {
                    c.Scalar<long>("SELECT COUNT(*) FROM MailQueue WHERE Status = 0"),
                    c.Scalar<long>("SELECT COUNT(*) FROM MailQueue WHERE Status = 2 AND CreatedUtc >= @s", ("@s", since)),
                    c.Scalar<long>("SELECT COUNT(*) FROM MailQueue WHERE Status = 1 AND CreatedUtc >= @s", ("@s", since))
                });
                if (counts[1] > 0) Todo(dto, Warn, "Son 24 saatte " + counts[1] + " mail gönderilemedi. Mail sayfasından SMTP ayarlarını test edin.", DashboardPages.Mail);
                return T(counts[1] > 0 ? counts[1] + " gönderilemedi" : "Hazır", "24 saatte " + counts[2] + " gönderildi" + (counts[0] > 0 ? ", " + counts[0] + " bekliyor" : ""),
                    counts[1] > 0 ? Warn : counts[0] > 10 ? Info : Ok);
            });

            var samples = Safe(() => g.Database.Read(c =>
            {
                var list = new List<Tuple<DateTime, double, double>>();
                using (var cmd = c.Create("SELECT TimeUtc, Cpu, Ram FROM ResourceSamples WHERE TimeUtc >= @s ORDER BY TimeUtc", ("@s", since)))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(Tuple.Create(DbTime.FromDb(r.GetString(0)), r.GetDouble(1), r.GetDouble(2)));
                return list;
            })) ?? new List<Tuple<DateTime, double, double>>();
            Tile(dto, "İşlemci", DashboardPages.Performance, () => Resource(g.Watch.Resources.CurrentCpu, samples.Select(s => Tuple.Create(s.Item1, s.Item2)).ToList(),
                GeneralSettings.GetInt(g.Settings, SettingKeys.CpuAlarmPercent)));
            Tile(dto, "Bellek", DashboardPages.Performance, () => Resource(g.Watch.Resources.CurrentRam, samples.Select(s => Tuple.Create(s.Item1, s.Item3)).ToList(),
                GeneralSettings.GetInt(g.Settings, SettingKeys.RamAlarmPercent)));

            try
            {
                dto.RecentEvents = g.Database.Read(c =>
                {
                    var list = new List<DashboardEventDto>();
                    using (var cmd = c.Create("SELECT TimeUtc, Severity, EventType, Summary FROM EventLog WHERE Severity >= 1 AND TimeUtc >= @s ORDER BY Id DESC LIMIT 20",
                        ("@s", DbTime.ToDb(DateTime.UtcNow.AddDays(-7)))))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var def = EventCatalog.Find(r.GetString(2));
                            list.Add(new DashboardEventDto { TimeUtc = r.GetString(0), Severity = r.GetInt32(1), Name = def == null ? r.GetString(2) : def.Name, Summary = r.GetString(3) });
                        }
                    }
                    return list;
                });
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Son olaylar okunamadı.", ex);
            }

            dto.Todos = dto.Todos.OrderByDescending(t => t.Level).Take(15).ToList();
            return dto;
        }

        /// <summary>Tarama özeti; kritik bulgular "yapılacaklar"a eklenir.</summary>
        private static DashboardTileDto Checks(GuardContext g, string scope, DashboardDto dto, string page, string kind)
        {
            var run = g.Checks.Latest(scope);
            if (run.RunId == null) return T("Taranmadı", run.IsRunning ? "Tarama sürüyor" : "İlk tarama rapor saatinde yapılır (veya Şimdi Tara)", Info);
            var items = run.Items.Where(x => !x.IsIgnored).ToList();
            int crit = items.Count(x => x.Status == CheckStatus.Kritik), warn = items.Count(x => x.Status == CheckStatus.Uyari);
            foreach (var x in items.Where(x => x.Status == CheckStatus.Kritik).Take(5))
                Todo(dto, Crit, kind + ": " + x.Title + ": " + x.Summary, page);
            // Fidye yazılımı erken uyarısı kapalıysa (uyarı da olsa) öne çıkar
            foreach (var x in items.Where(x => x.CheckId == "sec.win.ransomware" && x.Status == CheckStatus.Uyari))
                Todo(dto, Warn, kind + ": " + x.Title + ": " + x.Summary, page);
            return T(crit == 0 && warn == 0 ? "Sorun yok" : (crit > 0 ? crit + " kritik" : "") + (crit > 0 && warn > 0 ? ", " : "") + (warn > 0 ? warn + " uyarı" : ""),
                "Son tarama " + Local(run.StartedUtc) + (run.IsRunning ? " (şu an tarama sürüyor)" : "") + (items.Any(x => x.IsNew) ? ", " + items.Count(x => x.IsNew) + " yeni bulgu" : ""),
                crit > 0 ? Crit : warn > 0 ? Warn : Ok);
        }

        /// <summary>Anlık değer + son 24 saat (15 dk'lık ortalamalarla eğilim grafiği).</summary>
        private static DashboardTileDto Resource(double? current, List<Tuple<DateTime, double>> samples, int threshold)
        {
            if (current == null && samples.Count == 0) return T("Ölçülüyor", "İlk ölçüm birkaç saniye içinde", Info);
            var tile = T("%" + (int)Math.Round(current ?? samples.Last().Item2),
                samples.Count == 0 ? "Geçmiş birikiyor" : "24 saat: ort. %" + (int)Math.Round(samples.Average(s => s.Item2)) + ", en yüksek %" + (int)Math.Round(samples.Max(s => s.Item2))
                    + (samples.Any(s => s.Item2 >= threshold) ? ", eşik üstü " + samples.Count(s => s.Item2 >= threshold) + " dk" : ""),
                (current ?? 0) >= threshold ? Warn : Ok);
            tile.Trend = samples.GroupBy(s => new DateTime(s.Item1.Year, s.Item1.Month, s.Item1.Day, s.Item1.Hour, s.Item1.Minute / 15 * 15, 0))
                .Select(g => Math.Round(g.Average(s => s.Item2), 1)).ToList();
            if (tile.Trend.Count < 2) tile.Trend = null;
            return tile;
        }

        private static TResult Safe<TResult>(Func<TResult> work) where TResult : class
        {
            try
            {
                return work();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Panel verisi okunamadı.", ex);
                return null;
            }
        }

        private static void Tile(DashboardDto dto, string title, string page, Func<DashboardTileDto> build)
        {
            DashboardTileDto tile;
            try
            {
                tile = build();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Panel kutucuğu hazırlanamadı: " + title, ex);
                tile = T("Okunamadı", ex.Message, Warn);
            }
            tile.Title = title;
            tile.Page = page;
            dto.Tiles.Add(tile);
        }

        private static DashboardTileDto T(string value, string detail, int level)
        {
            return new DashboardTileDto { Value = value, Detail = detail, Level = level };
        }

        private static void Todo(DashboardDto dto, int level, string text, string page)
        {
            dto.Todos.Add(new DashboardTodoDto { Level = level, Text = text, Page = page });
        }

        private static string Local(string utc)
        {
            try
            {
                var t = DbTime.FromDb(utc).ToLocalTime();
                return t.Date == DateTime.Today ? "bugün " + t.ToString("HH:mm", Tr) : t.Date == DateTime.Today.AddDays(-1) ? "dün " + t.ToString("HH:mm", Tr) : t.ToString("dd.MM.yyyy HH:mm", Tr);
            }
            catch (Exception)
            {
                return utc;
            }
        }
    }
}
