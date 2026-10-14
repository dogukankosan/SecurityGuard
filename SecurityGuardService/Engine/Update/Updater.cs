using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Licensing;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Engine.Update
{
    /// <summary>
    /// Merkezi versiyon güncelleme — FAZ A (sadece kontrol + indir + doğrula; UYGULAMA YOK).
    /// Belirli aralıkla hub'a sorar: en son sürüm manifesti (RSA imzalı). İmzayı doğrular, mevcut sürümden yeniyse
    /// paketi indirir ve SHA-256'sını doğrular, &lt;root&gt;\Temp\Update\&lt;sürüm&gt; altında bekletir.
    /// "Hazır" durumu heartbeat ile panele gider; uygulama (durdur/değiştir/rollback) Faz B'de, panel onayıyla.
    /// Güvenlik: paket imzasız/SHA tutmuyorsa reddedilir. Hub'a ulaşılamazsa sessizce atlar.
    /// </summary>
    internal sealed class Updater : IDisposable
    {
        private const string Module = "Güncelleme";
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

        private readonly GuardContext _g;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Thread _thread;
        private readonly object _lock = new object();
        private string _available;      // yeni sürüm (null = güncel)
        private bool _ready;            // indirildi + SHA doğrulandı, uygulamaya hazır
        private bool _mandatory;
        private string _lastError;
        private string _lastReadyLogged;

        public Updater(GuardContext g)
        {
            _g = g;
        }

        /// <summary>Mevcut sürümden yeni, kurulabilir sürüm (yoksa null).</summary>
        public string AvailableVersion { get { lock (_lock) return _available; } }

        /// <summary>Paket indirildi ve SHA-256 doğrulandı: panelden onay verilince uygulanabilir.</summary>
        public bool Ready { get { lock (_lock) return _ready; } }

        public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public void Start()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "Updater", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        private void Loop()
        {
            if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(60))) return;
            while (!_cts.IsCancellationRequested)
            {
                try { Check(); }
                catch (Exception ex) { if (_lastError != ex.Message) { AppLog.Debug(Module, "Güncelleme kontrolü başarısız: " + ex.Message); _lastError = ex.Message; } }
                if (_cts.Token.WaitHandle.WaitOne(Interval)) return;
            }
        }

        private void Check()
        {
            var m = Fetch();
            if (m == null) { Set(null, false, false); return; }                 // güncelleme tanımlı değil
            if (!LicenseProtocol.Verify(m)) { AppLog.Warn(Module, "Güncelleme manifesti imzası DOĞRULANAMADI: yok sayıldı (sahte sunucu / bozuk cevap)."); Set(null, false, false); return; }

            string cur = CurrentVersion;
            if (!LicenseProtocol.IsNewerVersion(cur, m.Version)) { Set(null, false, false); _lastError = null; return; } // güncel
            if (!LicenseProtocol.MeetsMinVersion(cur, m.MinVersion))
            {
                AppLog.Warn(Module, "Yeni sürüm " + m.Version + " var ama önce ara sürüme çıkmak gerekiyor (gereken en düşük sürüm " + m.MinVersion + ", mevcut " + cur + ").");
                Set(m.Version, false, m.Mandatory); return;
            }

            bool ok = DownloadAndVerify(m);
            Set(m.Version, ok, m.Mandatory);
            if (ok && _lastReadyLogged != m.Version)
            {
                AppLog.Info(Module, "Yeni sürüm indirildi ve doğrulandı: " + m.Version + " (panelden onay bekliyor; otomatik uygulanmaz).");
                _lastReadyLogged = m.Version;
            }
            _lastError = null;
        }

        private void Set(string ver, bool ready, bool mandatory)
        {
            lock (_lock) { _available = ver; _ready = ready; _mandatory = mandatory; }
        }

        private UpdateManifest Fetch()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string url = _g.License.ApiUrl + LicenseProtocol.UpdatePath;
            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "GET";
            http.Timeout = 20000;
            http.UserAgent = "SecurityGuard/" + CurrentVersion;
            http.KeepAlive = false;
            using (var resp = (HttpWebResponse)http.GetResponse())
            {
                if (resp.StatusCode == HttpStatusCode.NoContent) return null; // güncelleme tanımlı değil
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return PipeProtocol.FromJson<UpdateManifest>(sr.ReadToEnd());
            }
        }

        private bool DownloadAndVerify(UpdateManifest m)
        {
            string dir = Path.Combine(AppPaths.TempFolder, "Update", Safe(m.Version));
            string zip = Path.Combine(dir, "package.zip");
            try
            {
                // Zaten indirilmiş ve doğruysa tekrar indirme
                if (File.Exists(zip) && new FileInfo(zip).Length == m.SizeBytes && string.Equals(Sha256(zip), m.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SaveManifest(dir, m);
                    return true;
                }

                Directory.CreateDirectory(dir);
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var http = (HttpWebRequest)WebRequest.Create(m.Url);
                http.Method = "GET";
                http.Timeout = 20000;
                http.ReadWriteTimeout = 300000; // büyük paket
                http.UserAgent = "SecurityGuard/" + CurrentVersion;
                http.KeepAlive = false;
                using (var resp = (HttpWebResponse)http.GetResponse())
                using (var rs = resp.GetResponseStream())
                using (var fs = File.Create(zip))
                    rs.CopyTo(fs);

                string sha = Sha256(zip);
                if (!string.Equals(sha, m.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    AppLog.Warn(Module, "İndirilen paketin SHA-256'sı manifestle UYUŞMUYOR: paket reddedildi (" + m.Version + "). Beklenen " + m.Sha256 + ", gelen " + sha + ".");
                    try { File.Delete(zip); } catch { }
                    return false;
                }
                SaveManifest(dir, m);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Debug(Module, "Paket indirilemedi / doğrulanamadı: " + ex.Message);
                return false;
            }
        }

        private static void SaveManifest(string dir, UpdateManifest m)
        {
            try { File.WriteAllText(Path.Combine(dir, "manifest.json"), PipeProtocol.ToJson(m)); } catch { }
        }

        private static string Safe(string v)
        {
            return new string((v ?? "").Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_').ToArray());
        }

        private static string Sha256(string file)
        {
            using (var s = File.OpenRead(file))
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
        }

        public void Dispose()
        {
            _cts.Cancel();
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(3));
        }
    }
}
