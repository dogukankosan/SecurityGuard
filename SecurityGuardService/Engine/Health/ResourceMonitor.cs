using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using SecurityGuardService.Checks;
using SecurityGuardService.Checks.Performance;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Events;

namespace SecurityGuardService.Engine.Health
{
    /// <summary>
    /// İşlemci ve bellek takibi (sistem nöbeti 30 sn'de bir çağırır):
    /// - Dakikalık ortalama ResourceSamples tablosuna yazılır (30 gün saklanır): Panel grafiği ve günlük rapor buradan okur.
    /// - Eşik (Genel Ayarlar) belirlenen süre boyunca aralıksız aşılırsa olay: en çok kullanan programlar ve kullanıcı oturumları ile.
    ///   Eşiğin altına inince sıfırlanır; aynı durum sürerse 6 saatte bir tekrar bildirilir.
    /// </summary>
    internal sealed class ResourceMonitor
    {
        private const string Module = "Kaynak";
        private static readonly TimeSpan Repeat = TimeSpan.FromHours(6);
        private static readonly TimeSpan Keep = TimeSpan.FromDays(30);

        private readonly GuardDatabase _db;
        private readonly SettingsStore _settings;
        private readonly EventRecorder _events;
        private (long Idle, long Total)? _lastCpu;
        private readonly List<double> _minuteCpu = new List<double>();
        private readonly List<double> _minuteRam = new List<double>();
        private DateTime _minute = DateTime.MinValue;
        private DateTime _lastCleanup = DateTime.MinValue;
        private readonly Alarm _cpuAlarm = new Alarm();
        private readonly Alarm _ramAlarm = new Alarm();

        private sealed class Alarm
        {
            public DateTime? AboveSince;
            public DateTime? LastAlarm;
            public double Peak;
        }

        public ResourceMonitor(GuardDatabase db, SettingsStore settings, EventRecorder events)
        {
            _db = db;
            _settings = settings;
            _events = events;
        }

        /// <summary>Son ölçülen değerler (yüzde; ilk ölçümden önce null).</summary>
        public double? CurrentCpu { get; private set; }
        public double? CurrentRam { get; private set; }

        public void Sample()
        {
            var now = DateTime.UtcNow;
            var cpu = WindowsNative.CpuTimes();
            double? cpuPct = null;
            if (_lastCpu != null)
            {
                long total = cpu.Total - _lastCpu.Value.Total, idle = cpu.Idle - _lastCpu.Value.Idle;
                if (total > 0) cpuPct = Math.Max(0, Math.Min(100, 100.0 * (total - idle) / total));
            }
            _lastCpu = cpu;
            double ramPct = WindowsNative.Memory().LoadPercent;
            CurrentRam = ramPct;
            if (cpuPct == null) return; // ilk ölçüm: fark için bir önceki gerekli
            CurrentCpu = cpuPct;

            // Dakikalık ortalama
            var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
            if (minute != _minute && _minuteCpu.Count > 0) Flush();
            _minute = minute;
            _minuteCpu.Add(cpuPct.Value);
            _minuteRam.Add(ramPct);

            int minutes = GeneralSettings.GetInt(_settings, SettingKeys.ResourceAlarmMinutes);
            Check(_cpuAlarm, cpuPct.Value, GeneralSettings.GetInt(_settings, SettingKeys.CpuAlarmPercent), minutes, now, true);
            Check(_ramAlarm, ramPct, GeneralSettings.GetInt(_settings, SettingKeys.RamAlarmPercent), minutes, now, false);

            if (now - _lastCleanup > TimeSpan.FromHours(6))
            {
                _lastCleanup = now;
                _db.Writer.Enqueue(c => c.Execute("DELETE FROM ResourceSamples WHERE TimeUtc < @t", ("@t", DbTime.ToDb(now - Keep))));
            }
        }

        private void Flush()
        {
            double cpu = _minuteCpu.Average(), ram = _minuteRam.Average();
            string time = DbTime.ToDb(_minute);
            _minuteCpu.Clear();
            _minuteRam.Clear();
            _db.Writer.Enqueue(c => c.Execute("INSERT OR REPLACE INTO ResourceSamples (TimeUtc, Cpu, Ram) VALUES (@t, @c, @r)",
                ("@t", time), ("@c", Math.Round(cpu, 1)), ("@r", Math.Round(ram, 1))));
        }

        private void Check(Alarm alarm, double value, int threshold, int minutes, DateTime now, bool cpu)
        {
            if (value < threshold)
            {
                if (alarm.AboveSince != null && alarm.LastAlarm != null)
                    AppLog.Info(Module, (cpu ? "İşlemci" : "Bellek") + " kullanımı normale döndü (%" + (int)value + ").");
                alarm.AboveSince = null;
                alarm.LastAlarm = null;
                alarm.Peak = 0;
                return;
            }
            if (alarm.AboveSince == null) alarm.AboveSince = now;
            alarm.Peak = Math.Max(alarm.Peak, value);
            if (now - alarm.AboveSince.Value < TimeSpan.FromMinutes(minutes)) return;
            if (alarm.LastAlarm != null && now - alarm.LastAlarm.Value < Repeat) return;
            alarm.LastAlarm = now;
            try
            {
                Raise(cpu, value, threshold, (int)(now - alarm.AboveSince.Value).TotalMinutes, alarm.Peak);
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Kaynak uyarısı hazırlanamadı: " + ex.Message);
            }
        }

        private void Raise(bool cpu, double value, int threshold, int minutes, double peak)
        {
            var processes = TopProcesses(cpu);
            var mem = WindowsNative.Memory();
            string what = cpu ? "İşlemci" : "Bellek";
            var e = new GuardEvent(cpu ? "Win.HighCpu" : "Win.HighMemory",
                    what + " kullanımı " + minutes + " dakikadır %" + threshold + " üstünde (şu an %" + (int)value + ")"
                    + (processes.Count > 0 ? "; en çok: " + processes[0].Item1 : "") + ".")
                { Source = "İşlemci / bellek izleme" };
            e.With("Şu an", "%" + (int)value + " (en yüksek %" + (int)peak + ")");
            e.With("Uyarı eşiği", "%" + threshold + ", " + minutes + " dakika (Genel Ayarlar → İşlemci ve bellek izleme)");
            if (!cpu) e.With("Bellek", Fmt.Bytes((long)(mem.Total - mem.Available)) + " / " + Fmt.Bytes((long)mem.Total) + " kullanımda");
            int i = 0;
            foreach (var p in processes.Take(8)) e.With((++i) + ". " + (cpu ? "işlemci" : "bellek"), p.Item1 + ": " + p.Item2);
            var sessions = TopSessions();
            if (sessions.Count > 0) e.With("Kullanıcı oturumları", string.Join(", ", sessions.Take(6)));
            e.With("Ne yapmalı", cpu
                ? "Listedeki program bir kullanıcının oturumundaysa o kullanıcıyla görüşün. SQL Server ise Performans Önerileri → Şimdi Tara: en yavaş sorgular, eksik index ve kilitlenme maddelerine bakın."
                : "SQL Server en üstteyse bellek sınırını ayarlayın (Performans Önerileri → Bellek sınırı). Kopmuş RDP oturumları bellek tutuyorsa oturum süre sınırı koyun. Sürekli doluysa bellek eklenmeli.");
            _events.Record(e);
        }

        /// <summary>(program, açıklama) en çok kullananlar; aynı adlı işlemler toplanır, oturum sahibi kullanıcılar eklenir.</summary>
        private static List<Tuple<string, string>> TopProcesses(bool cpu)
        {
            var users = new Dictionary<int, string>();
            try
            {
                foreach (var s in WindowsNative.Sessions()) users[s.Id] = s.User.Contains("\\") ? s.User.Substring(s.User.IndexOf('\\') + 1) : s.User;
            }
            catch { }

            var first = new Dictionary<int, TimeSpan>();
            if (cpu)
            {
                foreach (var p in Process.GetProcesses())
                    using (p)
                        try { first[p.Id] = p.TotalProcessorTime; } catch { }
                Thread.Sleep(3000);
            }
            var list = new List<Tuple<string, int, double, long>>(); // ad, oturum, cpu %, bellek
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        double c = 0;
                        TimeSpan t0;
                        if (cpu && first.TryGetValue(p.Id, out t0)) c = 100.0 * (p.TotalProcessorTime - t0).TotalMilliseconds / (3000.0 * Environment.ProcessorCount);
                        list.Add(Tuple.Create(p.ProcessName, p.SessionId, c, p.WorkingSet64));
                    }
                    catch { }
                }
            }
            return list.GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Name = g.Key,
                    Cpu = g.Sum(x => x.Item3),
                    Ram = g.Sum(x => x.Item4),
                    Count = g.Count(),
                    Users = g.Select(x => x.Item2).Distinct().Where(users.ContainsKey).Select(id => users[id]).Distinct().ToList()
                })
                .OrderByDescending(g => cpu ? g.Cpu : g.Ram)
                .Take(8)
                .Select(g => Tuple.Create(g.Name, (cpu ? Fmt.Pct(g.Cpu) + " işlemci" : Fmt.Bytes(g.Ram)) + (g.Count > 1 ? ", " + g.Count + " kopya" : "")
                                                  + (g.Users.Count > 0 ? ", kullanıcı: " + string.Join(", ", g.Users.Take(4)) + (g.Users.Count > 4 ? "..." : "") : "")))
                .ToList();
        }

        /// <summary>Oturum başına bellek: "kullanıcı 1,2 GB" (en çok kullanan önce).</summary>
        private static List<string> TopSessions()
        {
            var users = new Dictionary<int, string>();
            try
            {
                foreach (var s in WindowsNative.Sessions())
                    users[s.Id] = (s.User.Contains("\\") ? s.User.Substring(s.User.IndexOf('\\') + 1) : s.User) + (s.Disconnected ? " (kopuk)" : "");
            }
            catch
            {
                return new List<string>();
            }
            var bySession = new Dictionary<int, long>();
            foreach (var p in Process.GetProcesses())
                using (p)
                    try
                    {
                        if (!users.ContainsKey(p.SessionId)) continue;
                        long v;
                        bySession.TryGetValue(p.SessionId, out v);
                        bySession[p.SessionId] = v + p.WorkingSet64;
                    }
                    catch { }
            return bySession.OrderByDescending(x => x.Value).Select(x => users[x.Key] + " " + Fmt.Bytes(x.Value)).ToList();
        }
    }
}
