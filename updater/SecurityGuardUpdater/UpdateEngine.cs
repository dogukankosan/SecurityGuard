using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Licensing;

namespace SecurityGuardUpdater
{
    /// <summary>
    /// Güncelleme mantığı (UI'dan ayrı): kurulum kökünü bulur, hub'tan imzalı manifesti alır/doğrular,
    /// paketi indirir + SHA-256 doğrular, kimlikli servis durdurup dosyaları değiştirir, başlatır, doğrular,
    /// açılmazsa rollback. İlerleme ve adımlar <see cref="Log"/> / <see cref="Percent"/> ile bildirilir.
    /// </summary>
    internal sealed class UpdateEngine : IDisposable
    {
        private const string ServiceName = "SecurityGuard";

        private PipeClient _pipe;
        private string _password;

        public string Root { get; private set; }
        public string ServiceDir { get; private set; }
        public string UiDir { get; private set; }
        public string ServiceExe { get; private set; }
        public string CurrentVersion { get; private set; }
        public string MachineName { get; private set; }
        public string ApiUrl { get; private set; }
        public UpdateManifest Pending { get; private set; }

        /// <summary>Adım/satır mesajı.</summary>
        public Action<string> Log = _ => { };
        /// <summary>İlerleme yüzdesi 0-100; -1 = belirsiz (marquee).</summary>
        public Action<int> Percent = _ => { };

        public void Init()
        {
            Root = ResolveRoot();
            ServiceDir = Path.Combine(Root, "Service");
            UiDir = Path.Combine(Root, "UI");
            ServiceExe = Path.Combine(ServiceDir, "SecurityGuardService.exe");
            if (!File.Exists(ServiceExe))
                throw new UpdateException("SecurityGuard kurulumu bulunamadı. Bu program kurulum kökünde ya da kök altındaki Updater klasöründe olmalı.\r\nAranan: " + ServiceExe);
            _pipe = new PipeClient(ServiceExe);
            var st = _pipe.Status();
            CurrentVersion = st != null ? st.ServiceVersion : null;
            MachineName = st != null && st.MachineName != null ? st.MachineName : Environment.MachineName;
        }

        /// <summary>Giriş yapar, lisans adresini alır, hub'tan manifesti sorar. Pending = kurulabilir güncelleme (yoksa null).</summary>
        public void LoginAndCheck(string user, string pass)
        {
            _pipe.Login(user, pass);
            _password = pass;
            var lic = _pipe.License();
            ApiUrl = lic != null ? lic.ApiUrl : null;
            if (string.IsNullOrWhiteSpace(ApiUrl)) throw new UpdateException("Lisans sunucusu adresi alınamadı.");

            var m = FetchManifest(ApiUrl, CurrentVersion);
            Pending = null;
            if (m == null) return;
            if (!LicenseProtocol.Verify(m)) throw new UpdateException("Güncelleme manifesti imzası DOĞRULANAMADI (sahte sunucu / bozuk cevap).");
            if (!LicenseProtocol.IsNewerVersion(CurrentVersion, m.Version)) return;
            if (!LicenseProtocol.MeetsMinVersion(CurrentVersion, m.MinVersion))
                throw new UpdateException("Bu sürüme (" + m.Version + ") çıkmak için önce " + m.MinVersion + " sürümüne geçmelisiniz.");
            Pending = m;
        }

        public void Apply()
        {
            var m = Pending;
            if (m == null) throw new UpdateException("Uygulanacak güncelleme yok.");

            string work = Path.Combine(Root, "Temp", "Update", Safe(m.Version));
            string zip = Path.Combine(work, "package.zip");
            string staged = Path.Combine(work, "staged");
            string backup = Path.Combine(work, "backup");
            Directory.CreateDirectory(work);

            Log("Paket indiriliyor...");
            Download(m.Url, zip, m.SizeBytes);
            Log("Bütünlük doğrulanıyor (SHA-256)...");
            Percent(-1);
            string sha = Sha256(zip);
            if (!string.Equals(sha, m.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("İndirilen paketin SHA-256'sı manifestle UYUŞMUYOR: paket reddedildi.");
            Log("İmza ve bütünlük doğrulandı. ✓");

            if (Directory.Exists(staged)) Directory.Delete(staged, true);
            Directory.CreateDirectory(staged);
            ZipFile.ExtractToDirectory(zip, staged);
            string stagedService = Path.Combine(staged, "Service");
            string stagedUi = Path.Combine(staged, "UI");
            // Kısmi paket destekli: Service\ veya UI\'dan EN AZ BİRİ (içinde dosyayla) olsun. Değişmeyen dosyalar
            // olduğu gibi kalır (kopyalama üzerine yazar, silmez). Not: yeni sürümde silinen dosya varsa tam klasör gerekir.
            bool hasService = HasFiles(stagedService);
            bool hasUi = HasFiles(stagedUi);
            if (!hasService && !hasUi)
                throw new UpdateException("Paket beklenen yapıda değil: içinde Service\\ veya UI\\ klasörü (ve dosyaları) olmalı.");
            Log("Paket içeriği: " + (hasService ? "Service " : "") + (hasUi ? "UI" : "") + " güncellenecek.");

            Log("Servis durduruluyor...");
            _pipe.StopService(_password);
            WaitServiceState(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(90));
            KillUi(UiDir);
            Log("Servis durdu.");

            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            Log("Mevcut sürüm yedekleniyor...");
            if (Directory.Exists(ServiceDir)) CopyDir(ServiceDir, Path.Combine(backup, "Service"));
            if (Directory.Exists(UiDir)) CopyDir(UiDir, Path.Combine(backup, "UI"));

            try
            {
                Log("Yeni dosyalar kopyalanıyor...");
                if (hasService) CopyDir(stagedService, ServiceDir);
                if (hasUi) CopyDir(stagedUi, UiDir);
            }
            catch (Exception ex)
            {
                Log("Kopyalama başarısız (" + ex.Message + "), eski sürüm geri yükleniyor...");
                Rollback(backup);
                TryStart();
                throw new UpdateException("Güncelleme uygulanamadı, eski sürüm geri yüklendi.");
            }

            Log("Servis başlatılıyor...");
            TryStart();
            Log("Açılış doğrulanıyor...");
            string newVer = VerifyRunning(TimeSpan.FromSeconds(90));
            if (newVer == null)
            {
                Log("Yeni sürüm açılışta doğrulanamadı → GERİ ALINIYOR...");
                StopForRollback();
                Rollback(backup);
                TryStart();
                throw new UpdateException("Yeni sürüm düzgün başlamadı; eski sürüm geri yüklendi. Logları inceleyin (kök\\Logs).");
            }

            Log("Güncelleme tamam: " + CurrentVersion + " → " + newVer + ". Servis çalışıyor. ✓");
            CurrentVersion = newVer;
            Pending = null;
            try { Directory.Delete(work, true); } catch { }
            Percent(100);
        }

        // ---------------------------------------------------------------- Hub

        private static UpdateManifest FetchManifest(string apiUrl, string version)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string url = (apiUrl.EndsWith("/") ? apiUrl : apiUrl + "/") + LicenseProtocol.UpdatePath;
            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "GET";
            http.Timeout = 30000;
            http.UserAgent = "SecurityGuardUpdater/" + (version ?? "");
            try
            {
                using (var resp = (HttpWebResponse)http.GetResponse())
                {
                    if (resp.StatusCode == HttpStatusCode.NoContent) return null;
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        return PipeProtocol.FromJson<UpdateManifest>(sr.ReadToEnd());
                }
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r != null) { using (r) { } throw new UpdateException("Güncelleme sunucusu hata döndü: HTTP " + (int)r.StatusCode + " " + r.StatusDescription); }
                throw new UpdateException("İnternet bağlantısı yok ya da güncelleme sunucusuna ulaşılamıyor:\r\n" + url + "\r\n(" + ex.Message + ")");
            }
        }

        private void Download(string url, string file, long expectedSize)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "GET";
            http.Timeout = 30000;
            http.ReadWriteTimeout = 600000;
            try
            {
                using (var resp = (HttpWebResponse)http.GetResponse())
                using (var rs = resp.GetResponseStream())
                using (var fs = File.Create(file))
                {
                    long total = resp.ContentLength > 0 ? resp.ContentLength : expectedSize;
                    var buf = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = rs.Read(buf, 0, buf.Length)) > 0)
                    {
                        fs.Write(buf, 0, n);
                        done += n;
                        if (total > 0) Percent((int)Math.Min(100, done * 100 / total));
                    }
                }
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r != null) { using (r) { } throw new UpdateException("Paket indirilemedi: HTTP " + (int)r.StatusCode + " " + r.StatusDescription); }
                throw new UpdateException("Paket indirilemedi — internet/sunucu erişimi yok:\r\n" + url + "\r\n(" + ex.Message + ")");
            }
        }

        private static string Sha256(string file)
        {
            using (var s = File.OpenRead(file))
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
        }

        // ---------------------------------------------------------------- Servis

        private static void WaitServiceState(ServiceControllerStatus target, TimeSpan timeout)
        {
            using (var sc = new ServiceController(ServiceName))
            {
                var deadline = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < deadline)
                {
                    try { sc.Refresh(); if (sc.Status == target) return; } catch { }
                    Thread.Sleep(1000);
                }
                throw new UpdateException("Servis " + (int)timeout.TotalSeconds + " sn içinde '" + target + "' durumuna gelmedi.");
            }
        }

        private static void TryStart()
        {
            using (var sc = new ServiceController(ServiceName))
            {
                try { sc.Refresh(); if (sc.Status == ServiceControllerStatus.Running) return; } catch { }
                try { sc.Start(); } catch (Exception ex) { throw new UpdateException("Servis başlatılamadı: " + ex.Message); }
            }
            WaitServiceState(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
        }

        private string VerifyRunning(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using (var pipe = new PipeClient(ServiceExe))
                    {
                        var st = pipe.Status();
                        if (st != null && st.Ready) return st.ServiceVersion ?? "?";
                    }
                }
                catch { }
                Thread.Sleep(2000);
            }
            return null;
        }

        private static void StopForRollback()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    sc.Refresh();
                    if (sc.CanStop) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); }
                }
            }
            catch { }
            foreach (var p in SafeProcesses("SecurityGuardService"))
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            Thread.Sleep(2000);
        }

        private static void KillUi(string uiDir)
        {
            foreach (var p in SafeProcesses("SecurityGuardUI"))
            {
                try
                {
                    if (p.MainModule != null && p.MainModule.FileName.StartsWith(uiDir, StringComparison.OrdinalIgnoreCase))
                    { p.Kill(); p.WaitForExit(5000); }
                }
                catch { }
            }
        }

        private static Process[] SafeProcesses(string name)
        {
            try { return Process.GetProcessesByName(name); } catch { return new Process[0]; }
        }

        // ---------------------------------------------------------------- Dosya

        private static bool HasFiles(string dir)
        {
            try { return Directory.Exists(dir) && Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length > 0; }
            catch { return false; }
        }

        private static void CopyDir(string src, string dst)
        {
            src = Path.GetFullPath(src);
            dst = Path.GetFullPath(dst);
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(src.Length).TrimStart('\\');
                string target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                CopyWithRetry(f, target);
            }
        }

        private static void CopyWithRetry(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try { try { if (File.Exists(to)) File.SetAttributes(to, FileAttributes.Normal); } catch { } File.Copy(from, to, true); return; }
                catch (IOException) when (i < 10) { Thread.Sleep(500); }
                catch (UnauthorizedAccessException) when (i < 10) { Thread.Sleep(500); }
            }
        }

        private void Rollback(string backup)
        {
            string bs = Path.Combine(backup, "Service"), bu = Path.Combine(backup, "UI");
            if (Directory.Exists(bs)) CopyDir(bs, ServiceDir);
            if (Directory.Exists(bu)) CopyDir(bu, UiDir);
        }

        private static string Safe(string v)
        {
            return new string((v ?? "").Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_').ToArray());
        }

        private static string ResolveRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            if (File.Exists(Path.Combine(dir, "Service", "SecurityGuardService.exe"))) return dir;
            string parent = Path.GetDirectoryName(dir);
            if (parent != null && File.Exists(Path.Combine(parent, "Service", "SecurityGuardService.exe"))) return parent;
            return dir;
        }

        public void Dispose()
        {
            if (_pipe != null) _pipe.Dispose();
        }
    }
}
