using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using SecurityGuard.Shared.Contracts;
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
        private const int MaxPayload = 60000; // hub 65536 sınırı; altında kalalım

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
                payload = PipeProtocol.ToJson(Dashboard.Build(_g));
                if (payload.Length > MaxPayload) payload = null; // çok büyükse sadece özet alanları gider
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
}
