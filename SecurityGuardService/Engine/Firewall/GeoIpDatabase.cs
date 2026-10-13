using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Engine.Firewall
{
    /// <summary>
    /// IP → ülke veritabanı (db-ip.com "IP to Country Lite", CC BY 4.0: "IP Geolocation by DB-IP").
    /// - Programla bir kopya gelir (Service\GeoIP); servis ayda bir yenisini indirir (Kök\GeoIP, kurulum güncellemesinde silinmez).
    ///   İndirilen dosya tamamen okunup doğrulanmadan kullanılmaz. En yeni ay hangisindeyse o kullanılır.
    /// - Dosya .csv.gz olarak durur (~4,5 MB, ~700 bin satır); sadece kısıtlarda seçilen ülkelerin aralıkları bellekte tutulur.
    /// </summary>
    internal sealed class GeoIpDatabase
    {
        private const string Module = "ÜlkeDB";
        public const string Attribution = "IP Geolocation by DB-IP (db-ip.com), CC BY 4.0";
        private const string UrlFormat = "https://download.db-ip.com/free/dbip-country-lite-{0}.csv.gz";
        private static readonly Regex FileName = new Regex(@"^dbip-country-lite-(\d{4})-(\d{2})\.csv\.gz$", RegexOptions.IgnoreCase);
        private const int MinRows = 300000;

        private readonly string _bundledFolder;
        private readonly string _dataFolder;
        private readonly object _lock = new object();
        private string _file;
        private Dictionary<string, List<IpInterval>> _cache = new Dictionary<string, List<IpInterval>>(StringComparer.OrdinalIgnoreCase);
        private List<string> _countries;
        private uint[] _v4Start, _v4End;
        private string[] _v4Country;

        public GeoIpDatabase()
            : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeoIP"), Path.Combine(AppPaths.RootFolder, "GeoIP"))
        {
        }

        public GeoIpDatabase(string bundledFolder, string dataFolder)
        {
            _bundledFolder = bundledFolder;
            _dataFolder = dataFolder;
        }

        /// <summary>Veritabanı değişti (yeni ay indirildi): kısıtlar yeniden yazılmalı.</summary>
        public event Action Updated;

        /// <summary>Kullanılan dosyanın ayı (yoksa null).</summary>
        public DateTime? Month
        {
            get
            {
                string f = CurrentFile();
                return f == null ? (DateTime?)null : MonthOf(f);
            }
        }

        public bool Available { get { return CurrentFile() != null; } }

        /// <summary>Arayüz için kısa durum metni (ay + kaynak) ve varsa uyarı.</summary>
        public string Describe(out string warning)
        {
            warning = null;
            var m = Month;
            if (m == null)
            {
                warning = "Ülke veritabanı bulunamadı: ülke seçimi kullanılamaz.";
                return "Ülke veritabanı yok";
            }
            if (m.Value < new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-3))
                warning = "Ülke veritabanı eski (" + m.Value.ToString("MM.yyyy") + "). Sunucunun internete çıkıp db-ip.com'dan indirebildiğini kontrol edin.";
            return "Ülke veritabanı: " + m.Value.ToString("MM.yyyy") + " (" + Attribution + ")";
        }

        /// <summary>Veritabanındaki ülke kodları (ZZ = ayrılmış hariç).</summary>
        public List<string> Countries()
        {
            lock (_lock)
            {
                if (_countries == null) Load(new string[0]);
                return _countries;
            }
        }

        /// <summary>Ülkelerin birleştirilmiş IP aralıkları (IPv4 + IPv6). Bilinmeyen kod boş liste döner.</summary>
        public Dictionary<string, List<IpInterval>> Ranges(IEnumerable<string> codes)
        {
            var wanted = codes.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length == 2).Distinct().ToList();
            lock (_lock)
            {
                var missing = wanted.Where(c => !_cache.ContainsKey(c)).ToList();
                if (missing.Count > 0) Load(missing);
                return wanted.ToDictionary(c => c, c => _cache.ContainsKey(c) ? _cache[c] : new List<IpInterval>(), StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>IP'nin ülke kodu (IPv4; IPv6 ve bulunamayan için null). İlk çağrıda tüm IPv4 aralıkları sıralı diziye yüklenir (~5 MB).</summary>
        public string CountryOf(IPAddress address)
        {
            if (address == null) return null;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return null;
            uint x = ToUInt(address.GetAddressBytes());
            lock (_lock)
            {
                if (_v4Start == null)
                {
                    string file = CurrentFile();
                    if (file == null) return null;
                    var starts = new List<uint>(); var ends = new List<uint>(); var ccs = new List<string>();
                    var intern = new Dictionary<string, string>();
                    Read(file, (s, e, cc) =>
                    {
                        if (s.IndexOf(':') >= 0) return;
                        string c;
                        if (!intern.TryGetValue(cc, out c)) intern[cc] = c = cc;
                        starts.Add(ToUInt(IPAddress.Parse(s).GetAddressBytes()));
                        ends.Add(ToUInt(IPAddress.Parse(e).GetAddressBytes()));
                        ccs.Add(c);
                    });
                    _v4Start = starts.ToArray(); _v4End = ends.ToArray(); _v4Country = ccs.ToArray(); // dosya başlangıca göre sıralı
                }
                int i = Array.BinarySearch(_v4Start, x);
                if (i < 0) i = ~i - 1;
                if (i < 0 || x > _v4End[i]) return null;
                string code = _v4Country[i];
                return code == "ZZ" ? null : code;
            }
        }

        private static uint ToUInt(byte[] b)
        {
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        /// <summary>Dosyayı bir kez okur: ülke listesi + istenen ülkelerin aralıkları. Çağıran kilidi tutar.</summary>
        private void Load(ICollection<string> codes)
        {
            string file = CurrentFile();
            if (file == null) throw new InvalidOperationException("Ülke veritabanı bulunamadı.");
            var set = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
            var found = new Dictionary<string, List<IpInterval>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in set) found[c] = new List<IpInterval>();
            var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int rows = Read(file, (start, end, cc) =>
            {
                countries.Add(cc);
                List<IpInterval> list;
                if (found.TryGetValue(cc, out list)) list.Add(new IpInterval(IPAddress.Parse(start).GetAddressBytes(), IPAddress.Parse(end).GetAddressBytes()));
            });
            foreach (var f in found) _cache[f.Key] = IpIntervals.Merge(f.Value);
            countries.Remove("ZZ");
            _countries = countries.OrderBy(c => c).ToList();
            _file = file;
            AppLog.Debug(Module, Path.GetFileName(file) + " okundu (" + rows + " satır, " + string.Join(", ", set) + ").");
        }

        private static int Read(string file, Action<string, string, string> row)
        {
            int n = 0;
            using (var fs = File.OpenRead(file))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var reader = new StreamReader(gz))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int a = line.IndexOf(','), b = a < 0 ? -1 : line.IndexOf(',', a + 1);
                    if (b < 0) continue;
                    row(line.Substring(0, a), line.Substring(a + 1, b - a - 1), line.Substring(b + 1).Trim());
                    n++;
                }
            }
            return n;
        }

        // ================================================================ Dosya seçimi / güncelleme

        private string CurrentFile()
        {
            var files = new List<string>();
            foreach (var dir in new[] { _dataFolder, _bundledFolder })
                if (Directory.Exists(dir))
                    files.AddRange(Directory.GetFiles(dir, "dbip-country-lite-*.csv.gz").Where(f => FileName.IsMatch(Path.GetFileName(f))));
            return files.OrderByDescending(f => MonthOf(f)).FirstOrDefault();
        }

        private static DateTime MonthOf(string file)
        {
            var m = FileName.Match(Path.GetFileName(file));
            return new DateTime(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), 1);
        }

        /// <summary>
        /// Bu ayın dosyası yoksa indirmeyi dener (ayın ilk günlerinde yayınlanmamış olabilir: hata değil, sonra tekrar denenir).
        /// İndirilen dosya okunup doğrulanır (satır sayısı, TR kaydı) ve ancak sonra kullanılmaya başlanır.
        /// </summary>
        /// <returns>Yeni dosya devreye alındıysa true.</returns>
        public bool TryUpdate()
        {
            var now = DateTime.UtcNow;
            var thisMonth = new DateTime(now.Year, now.Month, 1);
            var current = Month;
            if (current != null && current.Value >= thisMonth) return false;

            string name = "dbip-country-lite-" + thisMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".csv.gz";
            Directory.CreateDirectory(_dataFolder);
            string temp = Path.Combine(_dataFolder, name + ".indiriliyor");
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(string.Format(UrlFormat, thisMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture)));
                request.Timeout = 60000;
                request.ReadWriteTimeout = 120000;
                request.UserAgent = "SecurityGuard";
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var body = response.GetResponseStream())
                using (var file = File.Create(temp))
                    body.CopyTo(file);

                bool tr = false;
                int rows = Read(temp, (s, e, cc) => { if (cc == "TR") tr = true; });
                if (rows < MinRows || !tr) throw new InvalidDataException("İndirilen dosya eksik görünüyor (" + rows + " satır).");

                string target = Path.Combine(_dataFolder, name);
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                foreach (var old in Directory.GetFiles(_dataFolder, "dbip-country-lite-*.csv.gz").Where(f => !string.Equals(f, target, StringComparison.OrdinalIgnoreCase)))
                    File.Delete(old);
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse && ((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.NotFound)
            {
                AppLog.Debug(Module, name + " henüz yayınlanmamış.");
                return false;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }

            lock (_lock)
            {
                _cache = new Dictionary<string, List<IpInterval>>(StringComparer.OrdinalIgnoreCase);
                _countries = null;
                _v4Start = null; _v4End = null; _v4Country = null;
            }
            AppLog.Info(Module, "Ülke veritabanı güncellendi: " + name);
            var h = Updated;
            if (h != null) h();
            return true;
        }

        // ================================================================ Türkçe ülke adları

        private static readonly Dictionary<string, string> TurkishNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "TR", "Türkiye" }, { "DE", "Almanya" }, { "AT", "Avusturya" }, { "NL", "Hollanda" }, { "BE", "Belçika" }, { "FR", "Fransa" },
            { "GB", "Birleşik Krallık" }, { "IE", "İrlanda" }, { "IT", "İtalya" }, { "ES", "İspanya" }, { "PT", "Portekiz" }, { "CH", "İsviçre" },
            { "SE", "İsveç" }, { "NO", "Norveç" }, { "DK", "Danimarka" }, { "FI", "Finlandiya" }, { "PL", "Polonya" }, { "CZ", "Çekya" },
            { "SK", "Slovakya" }, { "HU", "Macaristan" }, { "RO", "Romanya" }, { "BG", "Bulgaristan" }, { "GR", "Yunanistan" }, { "CY", "Kıbrıs" },
            { "HR", "Hırvatistan" }, { "SI", "Slovenya" }, { "RS", "Sırbistan" }, { "BA", "Bosna-Hersek" }, { "MK", "Kuzey Makedonya" }, { "AL", "Arnavutluk" },
            { "XK", "Kosova" }, { "ME", "Karadağ" }, { "UA", "Ukrayna" }, { "RU", "Rusya" }, { "BY", "Belarus" }, { "MD", "Moldova" },
            { "LT", "Litvanya" }, { "LV", "Letonya" }, { "EE", "Estonya" }, { "LU", "Lüksemburg" }, { "MT", "Malta" }, { "IS", "İzlanda" },
            { "AZ", "Azerbaycan" }, { "GE", "Gürcistan" }, { "AM", "Ermenistan" }, { "KZ", "Kazakistan" }, { "UZ", "Özbekistan" }, { "TM", "Türkmenistan" },
            { "KG", "Kırgızistan" }, { "TJ", "Tacikistan" }, { "IR", "İran" }, { "IQ", "Irak" }, { "SY", "Suriye" }, { "LB", "Lübnan" },
            { "IL", "İsrail" }, { "PS", "Filistin" }, { "JO", "Ürdün" }, { "SA", "Suudi Arabistan" }, { "AE", "Birleşik Arap Emirlikleri" }, { "QA", "Katar" },
            { "KW", "Kuveyt" }, { "BH", "Bahreyn" }, { "OM", "Umman" }, { "YE", "Yemen" }, { "EG", "Mısır" }, { "LY", "Libya" },
            { "TN", "Tunus" }, { "DZ", "Cezayir" }, { "MA", "Fas" }, { "US", "Amerika Birleşik Devletleri" }, { "CA", "Kanada" }, { "MX", "Meksika" },
            { "BR", "Brezilya" }, { "AR", "Arjantin" }, { "CL", "Şili" }, { "CO", "Kolombiya" }, { "CN", "Çin" }, { "JP", "Japonya" },
            { "KR", "Güney Kore" }, { "KP", "Kuzey Kore" }, { "IN", "Hindistan" }, { "PK", "Pakistan" }, { "AF", "Afganistan" }, { "BD", "Bangladeş" },
            { "ID", "Endonezya" }, { "MY", "Malezya" }, { "SG", "Singapur" }, { "TH", "Tayland" }, { "VN", "Vietnam" }, { "PH", "Filipinler" },
            { "HK", "Hong Kong" }, { "TW", "Tayvan" }, { "AU", "Avustralya" }, { "NZ", "Yeni Zelanda" }, { "ZA", "Güney Afrika" }, { "NG", "Nijerya" },
            { "KE", "Kenya" }, { "ET", "Etiyopya" }, { "SD", "Sudan" }, { "SO", "Somali" }, { "MN", "Moğolistan" }
        };

        public static string CountryName(string code)
        {
            string name;
            if (TurkishNames.TryGetValue(code, out name)) return name;
            try
            {
                return new RegionInfo(code).EnglishName;
            }
            catch (ArgumentException)
            {
                return code;
            }
        }
    }
}
