using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Checks;
using SecurityGuardService.Checks.Security;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Rules;
using SecurityGuardService.Ipc.Handlers;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Engine.Reports
{
    /// <summary>
    /// Merkezi panel heartbeat: açıksa her birkaç dakikada bir ajanın ÖZET durumunu hub'a (lisans sunucusu)
    /// OUTBOUND gönderir — müşteride port açmaz, NAT arkasından çalışır (lisans doğrulaması gibi).
    /// Sadece okur; hub'a ulaşılamazsa sessizce atlar (servis hiç etkilenmez).
    /// Kimlik = HwId + aktif lisans (hub lisanstan tanır). Müşteri adı GÖNDERİLMEZ, hub'da lisanstan okunur.
    /// Gönderilen özet durumdur (ban/olay sayıları, sağlık); olay içeriği / hassas veri gitmez.
    /// </summary>
    internal sealed class CentralReporter : IDisposable
    {
        private const string Module = "MerkezPanel";
        private const string Path = "api/v1/agent/heartbeat";
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);
        private const int MaxPayload = 400000; // zengin anlık görüntü (olaylar + RDP + yedek + port + taramalar...); hub sınırı 512 KB

        private readonly GuardContext _g;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Thread _thread;
        private string _lastError;
        private bool _sentOnce;

        public CentralReporter(GuardContext g)
        {
            _g = g;
        }

        public void Start()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "CentralReporter", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        private void Loop()
        {
            bool enabled = _g.Settings.GetBool(SettingKeys.CentralReportEnabled, true);
            AppLog.Info(Module, enabled
                ? "Merkezi panel bildirimi AÇIK (hub: " + _g.License.ApiUrl + Path + ", her " + (int)Interval.TotalMinutes + " dk)."
                : "Merkezi panel bildirimi kapalı (central.reportEnabled = false).");

            // Açılıştan ~30 sn sonra ilk gönderim (servis otursun), sonra her Interval
            if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30))) return;
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    if (_g.Settings.GetBool(SettingKeys.CentralReportEnabled, true))
                        SendOnce();
                }
                catch (Exception ex)
                {
                    // Hata tekrarını spam'lemeden bir kez logla (teşhis için Warn)
                    if (_lastError != ex.Message) { AppLog.Warn(Module, "Hub'a durum gönderilemedi: " + ex.Message); _lastError = ex.Message; }
                }
                if (_cts.Token.WaitHandle.WaitOne(Interval)) return;
            }
        }

        private void SendOnce()
        {
            var dto = BuildSnapshot();
            string url = _g.License.ApiUrl + Path;
            byte[] body = Encoding.UTF8.GetBytes(PipeProtocol.ToJson(dto));
            Post(url, body, dto.AgentVersion);
            _lastError = null;
            if (!_sentOnce) { AppLog.Info(Module, "Hub'a ilk durum bildirimi başarıyla gönderildi."); _sentOnce = true; }
        }

        private HeartbeatDto BuildSnapshot()
        {
            string since = DbTime.ToDb(DateTime.UtcNow.AddDays(-1));
            long[] counts;
            try
            {
                counts = _g.Database.Read(c => new[]
                {
                    c.Scalar<long>("SELECT COUNT(*) FROM BannedIps"),
                    c.Scalar<long>("SELECT COUNT(*) FROM BannedIps WHERE BannedUtc >= @s", ("@s", since)),
                    c.Scalar<long>("SELECT COUNT(*) FROM EventLog WHERE TimeUtc >= @s AND EventType IN (@a, @b)", ("@s", since), ("@a", EventCatalog.RdpLoginFailed), ("@b", EventCatalog.SqlLoginFailed)),
                    c.Scalar<long>("SELECT COUNT(*) FROM EventLog WHERE TimeUtc >= @s AND Severity = 2", ("@s", since)),
                    c.Scalar<long>("SELECT COUNT(*) FROM MailQueue WHERE Status = 0")
                });
            }
            catch (Exception ex)
            {
                AppLog.Debug(Module, "Özet sayıları okunamadı: " + ex.Message);
                counts = new long[5];
            }

            bool sqlMon = false;
            try
            {
                foreach (var i in _g.Sql.List())
                    if (i.IsConfigured && i.IsEnabled && _g.SqlAudit.GetState(i.InstanceName).Running) { sqlMon = true; break; }
            }
            catch { }

            int memMb = 0;
            try { memMb = (int)(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024); } catch { }

            string prot = _g.Protection.Status ?? "";
            bool protectionOk = prot.IndexOf("Servis izinleri: korunuyor", StringComparison.Ordinal) >= 0;

            string payload = null;
            try
            {
                payload = BuildReport();
                if (payload.Length > MaxPayload) payload = null; // çok büyükse sadece özet grid alanları gider
            }
            catch (Exception ex)
            {
                AppLog.Debug(Module, "Panel anlık görüntüsü hazırlanamadı: " + ex.Message);
            }

            return new HeartbeatDto
            {
                HwId = HardwareId.Value,
                AgentVersion = typeof(CentralReporter).Assembly.GetName().Version.ToString(),
                MachineName = Environment.MachineName,
                ServiceReady = _g.Settings.GetBool(SetupHandlers.CompletedKey, false),
                LicenseValid = LicenseGate.Active,
                ProtectionOk = protectionOk,
                SqlMonitoring = sqlMon,
                BannedIps = (int)counts[0],
                NewBans24h = (int)counts[1],
                FailedLogins24h = (int)counts[2],
                CriticalEvents24h = (int)counts[3],
                MemoryMb = memMb,
                PendingMail = (int)counts[4],
                Payload = payload
            };
        }

        // ================================================================ Zengin anlık görüntü (panel detay sekmeleri)

        /// <summary>UI'daki bölümlerin aynısı: özet + olaylar, RDP, yedekler, açık portlar, güvenlik/performans, SQL, servisler.</summary>
        private string BuildReport()
        {
            var rep = new AgentReportDto();
            try { rep.Dashboard = Dashboard.Build(_g); } catch (Exception ex) { AppLog.Debug(Module, "dashboard: " + ex.Message); }
            AddSafe(rep, EventsSection);
            AddSafe(rep, RdpSection);
            AddSafe(rep, BackupSection);
            AddSafe(rep, PortsSection);
            AddSafe(rep, () => ChecksSection(CheckScopes.Security, "Güvenlik önerileri", "security"));
            AddSafe(rep, () => ChecksSection(CheckScopes.Performance, "Performans önerileri", "performance"));
            AddSafe(rep, SqlSection);
            AddSafe(rep, ServicesSection);
            return PipeProtocol.ToJson(rep);
        }

        private void AddSafe(AgentReportDto rep, Func<ReportSection> build)
        {
            try { var s = build(); if (s != null) rep.Sections.Add(s); }
            catch (Exception ex) { AppLog.Debug(Module, "bölüm hazırlanamadı: " + ex.Message); }
        }

        private ReportSection EventsSection()
        {
            var s = new ReportSection { Key = "events", Title = "Olaylar (son 30 gün)", Columns = { "Zaman", "Önem", "Olay", "Kullanıcı / IP", "Özet" } };
            string since = DbTime.ToDb(DateTime.UtcNow.AddDays(-30));
            _g.Database.Read(c =>
            {
                using (var cmd = c.Create("SELECT TimeUtc, Severity, EventType, UserName, IpAddress, Summary FROM EventLog WHERE Severity >= 1 AND TimeUtc >= @s ORDER BY Id DESC LIMIT 120", ("@s", since)))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        var def = EventCatalog.Find(r.GetString(2));
                        string who = string.Join(" ", new[] { r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4) }.Where(x => !string.IsNullOrEmpty(x)));
                        s.Rows.Add(new List<string> { Local(r.GetString(0)), Sev(r.GetInt64(1)), def == null ? r.GetString(2) : def.Name, who, r.IsDBNull(5) ? "" : r.GetString(5) });
                    }
                return 0;
            });
            return s;
        }

        private ReportSection RdpSection()
        {
            var res = _g.RdpSessions.List(new RdpSessionQuery { IncludeConsole = true });
            var s = new ReportSection { Key = "rdp", Title = "RDP / konsol oturumları", Note = res.WorkHours, Columns = { "Başlangıç", "Kullanıcı", "IP", "Ülke", "Durum", "Süre", "Mesai dışı" } };
            foreach (var x in res.Rows.Take(50))
                s.Rows.Add(new List<string> { Local(x.StartUtc), x.User, x.ClientIp, x.Country, x.State, Dur(x.DurationSeconds), x.OffHours ? "evet" : "" });
            return s;
        }

        private ReportSection BackupSection()
        {
            var st = _g.Backup.GetState();
            var s = new ReportSection { Key = "backup", Title = "SQL yedekleme geçmişi", Note = st.Config != null && st.Config.Enabled ? "açık" : "kapalı", Columns = { "Başlangıç", "Instance", "Durum", "Tetikleyen", "Özet" } };
            foreach (var x in (st.Runs ?? new List<BackupRunDto>()).Take(20))
                s.Rows.Add(new List<string> { Local(x.StartedUtc), x.Instance, x.Status, x.Trigger, x.Summary });
            return s;
        }

        private ReportSection PortsSection()
        {
            var st = OpenPorts.Build(_g);
            var s = new ReportSection { Key = "ports", Title = "Açık portlar", Note = st.FirewallWarning, Columns = { "Proto", "Port", "Hizmet", "Açıklık", "Risk", "24s hatalı giriş", "Süreç" } };
            foreach (var p in st.Ports ?? new List<OpenPortDto>())
                s.Rows.Add(new List<string> { p.Protocol, p.Port.ToString(), p.Label, p.Exposure, p.Risk, p.Failures24h >= 0 ? p.Failures24h.ToString() : "", p.Process });
            return s;
        }

        private ReportSection ChecksSection(string scope, string title, string key)
        {
            var run = _g.Checks.Latest(scope);
            var s = new ReportSection { Key = key, Title = title, Note = run.RunId == null ? "henüz tarama yok" : "son tarama " + Local(run.StartedUtc), Columns = { "Durum", "Nerede", "Konu", "Sonuç" } };
            foreach (var x in (run.Items ?? new List<CheckItemDto>()).Where(i => !i.IsIgnored).OrderByDescending(i => (int)i.Status))
                s.Rows.Add(new List<string> { SevC(x.Status), x.Area, x.Title, x.Summary });
            return s;
        }

        private ReportSection SqlSection()
        {
            var s = new ReportSection { Key = "sql", Title = "SQL Server izleme", Columns = { "Instance", "Servis", "Sürüm / sürüm", "İzleme", "Audit" } };
            foreach (var i in _g.Sql.List())
            {
                bool running = false;
                try { running = i.IsConfigured && i.IsEnabled && _g.SqlAudit.GetState(i.InstanceName).Running; } catch { }
                s.Rows.Add(new List<string> { i.InstanceName, i.ServiceState, (i.ProductVersion + " " + i.Edition).Trim(), i.IsConfigured && i.IsEnabled ? "açık" : "kapalı", running ? "çalışıyor" : (i.IsConfigured ? "durmuş" : "-") });
            }
            return s;
        }

        private ReportSection ServicesSection()
        {
            var st = _g.Services.GetState();
            var s = new ReportSection { Key = "services", Title = "Servis takibi", Columns = { "Servis", "Durum", "Başlangıç", "İzleniyor", "Oto-başlat" } };
            foreach (var x in (st.Services ?? new List<WatchedServiceDto>()).Where(w => w.Watched || w.Recommended))
                s.Rows.Add(new List<string> { x.DisplayName + " (" + x.Name + ")", x.Status, x.StartType, x.Watched ? "evet" : "", x.AutoRestart ? "evet" : "" });
            return s;
        }

        private static string Sev(long s) { return s >= 2 ? "Kritik" : s == 1 ? "Uyarı" : "Bilgi"; }
        private static string SevC(CheckStatus s) { return s == CheckStatus.Kritik ? "Kritik" : s == CheckStatus.Uyari ? "Uyarı" : s == CheckStatus.Bilgi ? "Bilgi" : "Geçti"; }

        private static string Local(string utc)
        {
            try { return DbTime.FromDb(utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture); }
            catch { return utc ?? ""; }
        }

        private static string Dur(long seconds)
        {
            if (seconds <= 0) return "";
            if (seconds < 3600) return (seconds / 60) + " dk";
            return (seconds / 3600) + " sa " + (seconds % 3600 / 60) + " dk";
        }

        /// <summary>Hub'a POST. 403 = makinenin aktif lisansı yok (panelde görünmez); ulaşılamazsa sessizce atlanır.</summary>
        private void Post(string url, byte[] body, string version)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "POST";
            http.ContentType = "application/json; charset=utf-8";
            http.Accept = "application/json";
            http.UserAgent = "SecurityGuard/" + version;
            http.Timeout = 20000;
            http.ReadWriteTimeout = 20000;
            http.KeepAlive = false;
            using (var stream = http.GetRequestStream()) stream.Write(body, 0, body.Length);
            try
            {
                using (var response = (HttpWebResponse)http.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    reader.ReadToEnd();
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse r && r.StatusCode == HttpStatusCode.Forbidden)
            {
                using (r) { }
                if (_lastError != "403") { AppLog.Warn(Module, "Hub bu makinenin aktif lisansını bulamadı (403): panelde görünmeyecek."); _lastError = "403"; }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>Ajan → hub heartbeat gövdesi (hub tarafındaki HeartbeatRequest ile aynı alan adları: PascalCase).
    /// public: DataContractJsonSerializer (PipeProtocol) attribute'suz POCO'yu yalnız public tipte serialize eder.</summary>
    public sealed class HeartbeatDto
    {
        public string HwId { get; set; }
        public string AgentVersion { get; set; }
        public string MachineName { get; set; }
        public bool ServiceReady { get; set; }
        public bool LicenseValid { get; set; }
        public bool ProtectionOk { get; set; }
        public bool SqlMonitoring { get; set; }
        public int BannedIps { get; set; }
        public int NewBans24h { get; set; }
        public int FailedLogins24h { get; set; }
        public int CriticalEvents24h { get; set; }
        public int MemoryMb { get; set; }
        public int PendingMail { get; set; }
        public string Payload { get; set; }
    }

    /// <summary>Panel detay sekmeleri için zengin anlık görüntü (Payload = bunun JSON'u). public: PipeProtocol serialize eder.</summary>
    public sealed class AgentReportDto
    {
        public DashboardDto Dashboard { get; set; }
        public List<ReportSection> Sections { get; set; } = new List<ReportSection>();
    }

    /// <summary>UI'daki bir sayfanın tablo hali: başlık + sütunlar + satırlar (her satır sütun sayısı kadar hücre).</summary>
    public sealed class ReportSection
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Note { get; set; }
        public List<string> Columns { get; set; } = new List<string>();
        public List<List<string>> Rows { get; set; } = new List<List<string>>();
    }
}
