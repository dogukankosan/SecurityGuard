using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using SecurityGuard.Shared.Contracts;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Ban;

namespace SecurityGuardService.Engine.Collectors
{
    /// <summary>
    /// IIS log dosyalarından başarısız girişler (Windows Security loguna IP'siyle düşmeyenler):
    /// - FTP (IIS FTP, FTPSVCn): "PASS" komutu 530 → <see cref="BanManager.ProtocolFtp"/>
    /// - Web (W3SVCn): 401.1 (kimlik doğrulama başarısız: Basic, OWA, WebDAV) → <see cref="BanManager.ProtocolWeb"/>
    /// - SMTP (IIS SMTP, SMTPSVCn): "AUTH" 535 → <see cref="BanManager.ProtocolSmtp"/>
    /// Klasörler IIS yapılandırmasından (applicationHost.config) bulunur; IIS / FTP / SMTP kurulu değilse hiçbir şey yapmaz.
    /// Dosyalar sadece okunur. Kaldığı yer ayarlarda tutulur (servis yeniden başlasa da aynı satır iki kez sayılmaz);
    /// ilk kez görülen eski dosya baştan değil sondan okunur (geçmiş saldırılar sonradan ban üretmesin).
    /// IIS logu ~1 dk tamponlayarak yazar: tespit gecikmesi bu kadardır.
    /// </summary>
    internal sealed class IisLogCollector : IDisposable
    {
        private const string Module = "IISLog";
        private const string PositionsKey = "iislog.positions";
        public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(15);

        internal sealed class LogDir
        {
            public string Path;
            public string Protocol;
            public string Name;
        }

        /// <summary>(protokol, ip, kullanıcı, zaman, kaynak, özet, ayrıntılar)</summary>
        private readonly Action<string, string, string, DateTime, string, string, string[]> _sink;
        private readonly SettingsStore _settings;
        private readonly Dictionary<string, long> _positions;
        private readonly Dictionary<string, string[]> _fields = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _knownDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Timer _timer;
        private int _scanning;
        private bool _dirty;

        /// <summary>Klasör listesi (test için değiştirilebilir).</summary>
        internal Func<List<LogDir>> DirSource = Discover;

        public IisLogCollector(SettingsStore settings, Action<string, string, string, DateTime, string, string, string[]> sink)
        {
            _settings = settings;
            _sink = sink;
            _positions = Load(settings);
        }

        /// <summary>Son taramada izlenen klasörler (Açık Portlar / durum ekranı için).</summary>
        public List<LogDir> Watched { get; private set; } = new List<LogDir>();

        public void Start()
        {
            _timer = new Timer(_ => Scan(), null, TimeSpan.FromSeconds(20), ScanInterval);
        }

        public void Dispose()
        {
            var t = _timer;
            _timer = null;
            if (t != null) t.Dispose();
            Save();
        }

        internal void Scan()
        {
            if (Interlocked.Exchange(ref _scanning, 1) == 1) return;
            try
            {
                var dirs = DirSource().Where(d => Directory.Exists(d.Path)).ToList();
                foreach (var d in dirs.Where(d => !_knownDirs.Contains(d.Path)))
                    AppLog.Info(Module, d.Name + " logu izleniyor: " + d.Path);
                Watched = dirs;
                foreach (var d in dirs)
                {
                    bool firstLook = !_knownDirs.Contains(d.Path);
                    _knownDirs.Add(d.Path);
                    try
                    {
                        ScanDir(d, firstLook);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn(Module, d.Path + " okunamadı.", ex);
                    }
                }
                if (_dirty) Save();
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "IIS logları taranamadı.", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
            }
        }

        private void ScanDir(LogDir dir, bool firstLook)
        {
            // Sadece son 2 gün içinde yazılan dosyalar (gün dönümünde eski dosyanın son satırları da okunur)
            var files = new DirectoryInfo(dir.Path).GetFiles("*.log")
                .Where(f => f.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-2)).OrderBy(f => f.LastWriteTimeUtc).ToList();
            foreach (var f in files)
            {
                long pos;
                if (!_positions.TryGetValue(f.FullName, out pos))
                {
                    // İlk açılışta var olan dosya: geçmiş sayılmaz (sondan başla). Çalışırken yeni oluşan dosya: baştan.
                    pos = firstLook ? f.Length : 0;
                    _positions[f.FullName] = pos;
                    _dirty = true;
                }
                if (f.Length < pos) pos = 0; // dosya kesilmiş / yeniden oluşturulmuş
                if (f.Length == pos) continue;
                _positions[f.FullName] = ReadFrom(dir, f.FullName, pos);
                _dirty = true;
            }
            // Silinen / eskiyen dosyaların konumları unutulur
            foreach (var old in _positions.Keys.Where(k => k.StartsWith(dir.Path, StringComparison.OrdinalIgnoreCase) && !files.Any(f => string.Equals(f.FullName, k, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                _positions.Remove(old);
                _dirty = true;
            }
        }

        /// <summary>Tamamlanmış satırları okur, yeni konumu döner (yarım satır bir sonraki taramaya kalır).</summary>
        internal long ReadFrom(LogDir dir, string file, long pos)
        {
            string[] fields;
            if (!_fields.TryGetValue(file, out fields)) fields = HeaderFields(file);

            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(pos, SeekOrigin.Begin);
                var buffer = new byte[Math.Min(fs.Length - pos, 32 * 1024 * 1024)]; // tek seferde en fazla 32 MB
                int read = 0, n;
                while (read < buffer.Length && (n = fs.Read(buffer, read, buffer.Length - read)) > 0) read += n;
                int lastNewLine = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
                if (lastNewLine < 0) return pos;
                string text = Encoding.UTF8.GetString(buffer, 0, lastNewLine + 1);
                foreach (var raw in text.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Length == 0) continue;
                    if (line[0] == '#')
                    {
                        if (line.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
                            fields = line.Substring(8).Trim().Split(' ');
                        continue;
                    }
                    if (fields != null) Line(dir, fields, line);
                }
                _fields[file] = fields;
                return pos + lastNewLine + 1;
            }
        }

        /// <summary>Dosyanın başındaki #Fields satırı (ortadan okumaya başlarken alan sırasını bilmek için).</summary>
        private static string[] HeaderFields(string file)
        {
            string[] fields = null;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs))
            {
                for (int i = 0; i < 20; i++)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    if (line.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase)) fields = line.Substring(8).Trim().Split(' ');
                }
            }
            return fields;
        }

        private void Line(LogDir dir, string[] fields, string line)
        {
            var values = line.Split(' ');
            Func<string, string> get = name =>
            {
                int i = Array.FindIndex(fields, f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i < values.Length ? values[i] : null;
            };
            string status = get("sc-status");
            string method = get("cs-method");
            string ip = get("c-ip");
            if (status == null || ip == null) return;

            string summary, detail;
            if (dir.Protocol == BanManager.ProtocolFtp)
            {
                if (status != "530" || !string.Equals(method, "PASS", StringComparison.OrdinalIgnoreCase)) return;
                summary = "FTP girişi başarısız";
                detail = "FTP sitesi: " + (get("s-sitename") ?? dir.Name);
            }
            else if (dir.Protocol == BanManager.ProtocolWeb)
            {
                if (status != "401" || get("sc-substatus") != "1") return;
                summary = "Web / IIS girişi başarısız";
                detail = method + " " + get("cs-uri-stem") + " (site: " + (get("s-sitename") ?? dir.Name) + ")";
            }
            else
            {
                if (!status.StartsWith("535", StringComparison.Ordinal) || !string.Equals(method, "AUTH", StringComparison.OrdinalIgnoreCase)) return;
                summary = "SMTP girişi başarısız";
                detail = "SMTP sanal sunucusu: " + dir.Name;
            }

            string user = get("cs-username");
            if (user != null) user = user.Replace('+', ' ');
            DateTime time;
            if (!DateTime.TryParse(get("date") + " " + get("time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time))
                time = DateTime.UtcNow;
            string shown = string.IsNullOrEmpty(user) || user == "-" ? "(boş kullanıcı)" : user;
            _sink(dir.Protocol, ip, user, time, "IIS logu (" + dir.Name + ")", summary + ": " + shown + " (" + ip + ")",
                new[] { "Ayrıntı", detail, "Log dosyası", dir.Path });
        }

        // ================================================================ Klasörlerin bulunması

        /// <summary>IIS web / FTP site log klasörleri (applicationHost.config) ve IIS SMTP log klasörleri.</summary>
        internal static List<LogDir> Discover()
        {
            var list = new List<LogDir>();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string config = Path.Combine(windows, @"System32\inetsrv\config\applicationHost.config");
            if (File.Exists(config))
            {
                var doc = XDocument.Load(config);
                var sites = doc.Root == null ? null : doc.Root.Element("system.applicationHost") == null ? null : doc.Root.Element("system.applicationHost").Element("sites");
                if (sites != null)
                {
                    var defaults = sites.Element("siteDefaults");
                    string webDefault = Attr(defaults, "logFile", "directory") ?? @"%SystemDrive%\inetpub\logs\LogFiles";
                    string ftpDefault = Attr(defaults == null ? null : defaults.Element("ftpServer"), "logFile", "directory") ?? @"%SystemDrive%\inetpub\logs\LogFiles";
                    foreach (var site in sites.Elements("site"))
                    {
                        string id = (string)site.Attribute("id");
                        string name = (string)site.Attribute("name") ?? id;
                        var protocols = site.Descendants("binding").Select(b => ((string)b.Attribute("protocol") ?? "").ToLowerInvariant()).ToList();
                        if (protocols.Any(p => p == "http" || p == "https"))
                            list.Add(new LogDir { Protocol = BanManager.ProtocolWeb, Name = "Web: " + name, Path = Path.Combine(Expand(Attr(site, "logFile", "directory") ?? webDefault), "W3SVC" + id) });
                        if (protocols.Contains("ftp"))
                            list.Add(new LogDir { Protocol = BanManager.ProtocolFtp, Name = "FTP: " + name, Path = Path.Combine(Expand(Attr(site.Element("ftpServer"), "logFile", "directory") ?? ftpDefault), "FTPSVC" + id) });
                    }
                }
            }
            string smtpRoot = Path.Combine(windows, @"System32\LogFiles");
            if (Directory.Exists(smtpRoot))
                foreach (var d in Directory.GetDirectories(smtpRoot, "SMTPSVC*"))
                    list.Add(new LogDir { Protocol = BanManager.ProtocolSmtp, Name = "SMTP: " + Path.GetFileName(d), Path = d });
            return list;
        }

        private static string Attr(XElement parent, string element, string attribute)
        {
            var e = parent == null ? null : parent.Element(element);
            return e == null ? null : (string)e.Attribute(attribute);
        }

        private static string Expand(string path)
        {
            return Environment.ExpandEnvironmentVariables(path);
        }

        // ================================================================ Konumlar

        private static Dictionary<string, long> Load(SettingsStore settings)
        {
            try
            {
                var stored = PipeProtocol.FromJson<Dictionary<string, long>>(settings.Get(PositionsKey));
                if (stored != null) return new Dictionary<string, long>(stored, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Log konumları okunamadı (dosyalar sondan okunacak).", ex);
            }
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        private void Save()
        {
            if (!_dirty || _settings == null) return;
            _dirty = false;
            try
            {
                _settings.SetAsync(PositionsKey, PipeProtocol.ToJson(new Dictionary<string, long>(_positions))).Wait(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Log konumları kaydedilemedi.", ex);
            }
        }
    }
}
