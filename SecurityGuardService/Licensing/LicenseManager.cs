using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Licensing;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Security;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Events;

namespace SecurityGuardService.Licensing
{
    /// <summary>
    /// Lisans doğrulama: 5 dakikada bir lisans sunucusuna "bu donanım kimliğinin lisansı geçerli mi" diye sorar.
    ///
    /// - Cevap sunucunun RSA anahtarıyla imzalıdır ve isteğin rastgele değerini (nonce) taşır: sahte sunucu / eski cevap kabul edilmez.
    /// - Son imzalı cevap DPAPI ile şifreli olarak License tablosunda saklanır; açılışta imzası tekrar doğrulanır.
    /// - Sunucuya ulaşılamazsa son geçerli cevaptan itibaren 7 gün çalışmaya devam eder (3. günden itibaren günde bir uyarı maili).
    ///   Saat geri alınarak süre uzatılamaz: görülen en ileri zaman saklanır.
    /// - Süre dolar / iptal edilir / lisans yoksa <see cref="LicenseGate"/> kapanır: izleme durur, mevcut banlar kalır;
    ///   tekrar geçerli olunca yeniden başlatma gerekmeden devam eder.
    /// </summary>
    internal sealed class LicenseManager : IDisposable
    {
        private const string Module = "Lisans";
        private const string Purpose = "license.document";
        private static readonly int[] ExpiryWarnDays = { 1, 3, 7 }; // küçükten büyüğe: geçerli en dar eşik

        private readonly GuardDatabase _db;
        private readonly SettingsStore _settings;
        private readonly EventRecorder _events;
        private readonly SemaphoreSlim _checkLock = new SemaphoreSlim(1, 1);
        private readonly object _stateLock = new object();
        private State _state = new State();
        private Timer _timer;

        public LicenseManager(GuardDatabase db, SettingsStore settings, EventRecorder events)
        {
            _db = db;
            _settings = settings;
            _events = events;
        }

        public string ApiUrl
        {
            get
            {
                string url = _settings.Get(SettingKeys.LicenseApiUrl);
                return string.IsNullOrWhiteSpace(url) ? GuardConstants.LicenseDefaultApiUrl : url;
            }
        }

        /// <summary>Kayıtlı durumu okur, kapıyı hemen ayarlar (sunucuyu beklemeden), sonra periyodik doğrulamayı başlatır.</summary>
        public void Start()
        {
            lock (_stateLock)
            {
                _state = Load();
                Apply(DateTime.UtcNow, false);
            }
            AppLog.Info(Module, "Kayıtlı lisans durumu: " + Describe(_state, DateTime.UtcNow).StatusText + (LicenseGate.Active ? " (koruma açık)." : " (koruma kapalı)."));
            var interval = TimeSpan.FromMinutes(GuardConstants.LicenseValidateIntervalMinutes);
            _timer = new Timer(_ =>
            {
                try
                {
                    CheckAsync("periyodik").Wait();
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Periyodik lisans doğrulaması hata verdi.", ex);
                }
            }, null, TimeSpan.FromSeconds(2), interval);
        }

        public LicenseInfoDto Info()
        {
            lock (_stateLock)
                return Describe(_state, DateTime.UtcNow);
        }

        public async Task SetApiUrlAsync(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length > 0)
            {
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new GuardException(ErrorCodes.Validation, "Adres http:// veya https:// ile başlamalı. Örnek: https://lisans.firmaniz.com/");
                if (!url.EndsWith("/", StringComparison.Ordinal)) url += "/";
            }
            await _settings.SetAsync(SettingKeys.LicenseApiUrl, url).ConfigureAwait(false);
            AppLog.Info(Module, "Lisans sunucusu adresi: " + ApiUrl);
        }

        /// <summary>Sunucuya sorar, durumu kaydeder, kapıyı günceller. Aynı anda tek doğrulama çalışır.</summary>
        public async Task<LicenseInfoDto> CheckAsync(string trigger)
        {
            await _checkLock.WaitAsync().ConfigureAwait(false);
            try
            {
                DateTime now = DateTime.UtcNow;
                LicenseValidateResponse response = null;
                string error = null;
                try
                {
                    response = await Task.Run(() => Validate(HardwareId.Value)).ConfigureAwait(false);
                }
                catch (LicenseCallException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    error = "Beklenmeyen hata: " + ex.Message;
                    AppLog.Error(Module, "Lisans doğrulanamadı (" + trigger + ").", ex);
                }

                lock (_stateLock)
                {
                    var s = _state.Copy();
                    s.LastAttemptUtc = now;
                    if (response != null)
                    {
                        s.Document = response;
                        s.LastContactUtc = now;
                        s.LastError = null;
                        if (response.Status == LicenseProtocol.StatusValid) s.LastValidUtc = now;
                        s.MaxSeenUtc = now; // sunucuyla konuşuldu: yerel saatin geri alınması / düzeltilmesi sıfırlanır
                    }
                    else
                    {
                        if (s.LastError != error) AppLog.Warn(Module, "Lisans sunucusuna ulaşılamadı (" + trigger + "): " + error);
                        s.LastError = error;
                    }
                    _state = s;
                    Apply(now, true);
                    return Describe(_state, now);
                }
            }
            finally
            {
                _checkLock.Release();
            }
        }

        // ================================================================ Sunucu

        private LicenseValidateResponse Validate(string hwId)
        {
            byte[] nonceBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonceBytes);
            var request = new LicenseValidateRequest
            {
                HwId = hwId,
                Nonce = Convert.ToBase64String(nonceBytes),
                ClientTimeUtc = LicenseProtocol.FormatTime(DateTime.UtcNow),
                Version = typeof(LicenseManager).Assembly.GetName().Version.ToString(),
                MachineName = Environment.MachineName
            };

            string url = ApiUrl + LicenseProtocol.ValidatePath;
            byte[] body = Encoding.UTF8.GetBytes(PipeProtocol.ToJson(request));
            string json;
            try
            {
                try
                {
                    json = Post(url, body, request.Version);
                }
                catch (WebException ex) when (ex.Response == null)
                {
                    // Bağlantı düzeyinde anlık hata (kopan bağlantı, kısa ağ kesintisi): bir kez daha denenir,
                    // yoksa tek bir kopma 5 dakika boyunca "çevrimdışı" gösterir
                    AppLog.Debug(Module, "Lisans sunucusu bağlantısı koptu, tekrar deneniyor: " + ex.Message);
                    Thread.Sleep(2000);
                    json = Post(url, body, request.Version);
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null)
                    using (resp)
                        throw new LicenseCallException("Lisans sunucusu hata döndü: HTTP " + (int)resp.StatusCode + " " + resp.StatusDescription);
                throw new LicenseCallException("Lisans sunucusuna bağlanılamadı (" + url + "): " + ex.Message);
            }
            catch (UriFormatException)
            {
                throw new LicenseCallException("Lisans sunucusu adresi hatalı: " + url);
            }

            LicenseValidateResponse result;
            try
            {
                result = PipeProtocol.FromJson<LicenseValidateResponse>(json);
            }
            catch (Exception)
            {
                result = null;
            }
            if (result == null) throw new LicenseCallException("Lisans sunucusunun cevabı okunamadı.");
            if (!LicenseProtocol.Verify(result)) throw new LicenseCallException("Lisans sunucusunun imzası doğrulanamadı (sunucu SecurityGuard'ın lisans sunucusu değil veya cevap değiştirilmiş).");
            if (result.Nonce != request.Nonce || result.HwId != hwId) throw new LicenseCallException("Lisans sunucusunun cevabı bu isteğe ait değil.");
            return result;
        }

        private static string Post(string url, byte[] body, string version)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "POST";
            http.ContentType = "application/json; charset=utf-8";
            http.Accept = "application/json";
            http.UserAgent = "SecurityGuard/" + version;
            http.Timeout = 20000;
            http.ReadWriteTimeout = 20000;
            // 5 dakikada bir istek: bağlantı açık tutulmaz (sunucunun kapattığı eski bağlantıya yazmaya çalışıp hata almasın)
            http.KeepAlive = false;
            using (var stream = http.GetRequestStream()) stream.Write(body, 0, body.Length);
            using (var response = (HttpWebResponse)http.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        // ================================================================ Durum

        /// <summary>Geçerlilik hesabı (saat geri alınmışsa görülen en ileri zaman kullanılır).</summary>
        private static Verdict Evaluate(State s, DateTime nowUtc)
        {
            DateTime now = nowUtc > s.MaxSeenUtc ? nowUtc : s.MaxSeenUtc;
            var d = s.Document;
            if (d == null) return new Verdict(false, "Lisans yok", s.LastError == null ? "Bu makine için lisans tanımlanmamış. Donanım kimliğini lisans sağlayıcınıza iletin." : "Lisans sunucusuna ulaşılamadı: " + s.LastError);
            if (d.Status == LicenseProtocol.StatusUnknown) return new Verdict(false, "Bu makine için lisans yok", "Lisans sunucusunda bu donanım kimliğine ait lisans bulunamadı. Donanım kimliğini lisans sağlayıcınıza iletin.");
            if (d.Status == LicenseProtocol.StatusRevoked) return new Verdict(false, "İptal edildi", "Lisans iptal edildi. Lisans sağlayıcınızla görüşün.");

            DateTime expires;
            bool hasExpiry = TryParse(d.ExpiresUtc, out expires);
            if (d.Status == LicenseProtocol.StatusExpired || (hasExpiry && now >= expires))
                return new Verdict(false, "Süresi doldu", "Lisans süresi " + (hasExpiry ? Local(expires) + " tarihinde " : "") + "doldu. Süre uzatıldığında koruma kendiliğinden devam eder.");
            if (d.Status != LicenseProtocol.StatusValid) return new Verdict(false, "Geçersiz", "Lisans sunucusu tanınmayan bir durum döndü: " + d.Status);

            if (s.LastValidUtc == DateTime.MinValue || now - s.LastValidUtc > TimeSpan.FromDays(GuardConstants.LicenseOfflineToleranceDays))
                return new Verdict(false, "Sunucuya ulaşılamıyor", "Lisans sunucusuna " + GuardConstants.LicenseOfflineToleranceDays + " günden uzun süredir ulaşılamadı. İnternet bağlantısını ve lisans sunucusu adresini kontrol edin.");

            if (s.LastError != null && s.LastAttemptUtc > s.LastContactUtc)
                return new Verdict(true, "Geçerli (çevrimdışı)", "Lisans sunucusuna ulaşılamıyor: " + s.LastError + ". Koruma " + Local(s.LastValidUtc.AddDays(GuardConstants.LicenseOfflineToleranceDays)) + " tarihine kadar devam eder.");
            return new Verdict(true, "Geçerli", null);
        }

        /// <summary>Kapıyı ayarlar; değişiklikleri kaydeder ve gerekirse uyarı / durum olaylarını yazar.</summary>
        private void Apply(DateTime nowUtc, bool persist)
        {
            var s = _state;
            var v = Evaluate(s, nowUtc);
            bool was = s.WasActive;
            LicenseGate.Active = v.Active;
            if (nowUtc > s.MaxSeenUtc) s.MaxSeenUtc = nowUtc;

            if (persist || was != v.Active)
            {
                if (was && !v.Active)
                {
                    AppLog.Warn(Module, "Koruma durdu: " + v.StatusText + ". " + v.Detail);
                    Record(new GuardEvent("Guard.LicenseExpired", "SecurityGuard lisansı geçersiz (" + v.StatusText.ToLowerInvariant() + "): sunucu izleme ve koruma durdu. Mevcut ban kuralları yerinde kalır.")
                        .With("Durum", v.StatusText)
                        .With("Açıklama", v.Detail)
                        .With("Müşteri", s.Document == null ? null : s.Document.Customer)
                        .With("Donanım kimliği", HardwareId.Value));
                }
                else if (!was && v.Active && s.EverActive)
                {
                    AppLog.Info(Module, "Lisans geçerli: koruma yeniden başladı.");
                    Record(new GuardEvent("Guard.LicenseRestored", "SecurityGuard lisansı tekrar geçerli: izleme ve koruma devam ediyor.")
                        .With("Müşteri", s.Document.Customer)
                        .With("Bitiş", Expires(s)));
                }
                else if (!was && v.Active)
                {
                    AppLog.Info(Module, "Lisans etkinleştirildi: " + s.Document.Customer + ", bitiş " + Expires(s) + ".");
                }
                s.WasActive = v.Active;
                if (v.Active) s.EverActive = true;
            }

            if (v.Active) Warnings(s, nowUtc);
            if (persist || s.Dirty) Save(s);
        }

        /// <summary>Bitişe 7 / 3 / 1 gün kala ve 3 günden uzun çevrimdışı kalınca (günde bir) uyarı.</summary>
        private void Warnings(State s, DateTime nowUtc)
        {
            DateTime expires;
            if (TryParse(s.Document.ExpiresUtc, out expires))
            {
                double daysLeft = (expires - nowUtc).TotalDays;
                foreach (int days in ExpiryWarnDays)
                {
                    if (daysLeft > days) continue;
                    if (!s.Notice("exp:" + s.Document.ExpiresUtc + ":" + days)) break;
                    Record(new GuardEvent("Guard.LicenseExpiring", "SecurityGuard lisansının bitmesine " + Math.Max(1, (int)Math.Ceiling(daysLeft)) + " gün kaldı (" + Local(expires) + ").")
                        .With("Müşteri", s.Document.Customer)
                        .With("Bitiş", Local(expires))
                        .With("Öneri", "Süre dolunca sunucu izleme ve koruma durur. Uzatmak için lisans sağlayıcınızla görüşün."));
                    break;
                }
            }

            TimeSpan offline = nowUtc - s.LastValidUtc;
            if (s.LastError != null && offline >= TimeSpan.FromDays(GuardConstants.LicenseOfflineWarnDays))
            {
                string key = "off:" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (s.Notice(key))
                {
                    var until = s.LastValidUtc.AddDays(GuardConstants.LicenseOfflineToleranceDays);
                    Record(new GuardEvent("Guard.LicenseOffline", "Lisans sunucusuna " + (int)offline.TotalDays + " gündür ulaşılamıyor; " + Local(until) + " tarihinden sonra koruma durur.")
                        .With("Son hata", s.LastError)
                        .With("Lisans sunucusu", ApiUrl)
                        .With("Son başarılı doğrulama", Local(s.LastValidUtc))
                        .With("Öneri", "Sunucunun internet bağlantısını ve güvenlik duvarını kontrol edin."));
                }
            }
        }

        private void Record(GuardEvent e)
        {
            e.Source = "Lisans doğrulama";
            _events.Record(e);
        }

        private LicenseInfoDto Describe(State s, DateTime nowUtc)
        {
            var v = Evaluate(s, nowUtc);
            var d = s.Document;
            DateTime expires;
            bool hasExpiry = TryParse(d == null ? null : d.ExpiresUtc, out expires);
            bool offline = v.Active && s.LastError != null && s.LastAttemptUtc > s.LastContactUtc;
            return new LicenseInfoDto
            {
                HwId = HardwareId.Value,
                Active = v.Active,
                StatusText = v.StatusText,
                Detail = v.Detail,
                Customer = d == null || d.Status == LicenseProtocol.StatusUnknown ? null : d.Customer,
                Expires = hasExpiry ? Local(expires) : null,
                DaysLeft = hasExpiry && v.Active ? Math.Max(0, (int)Math.Ceiling((expires - nowUtc).TotalDays)) : 0,
                LastCheck = s.LastContactUtc == DateTime.MinValue ? null : s.LastContactUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
                LastError = s.LastAttemptUtc > s.LastContactUtc ? s.LastError : null,
                OfflineUntil = offline ? Local(s.LastValidUtc.AddDays(GuardConstants.LicenseOfflineToleranceDays)) + " " + s.LastValidUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) : null,
                HardwareChanged = d != null && d.Status == LicenseProtocol.StatusValid && d.Matched == 3,
                ApiUrl = ApiUrl
            };
        }

        private static string Expires(State s)
        {
            DateTime e;
            return s.Document != null && TryParse(s.Document.ExpiresUtc, out e) ? Local(e) : "-";
        }

        private static string Local(DateTime utc)
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        private static bool TryParse(string text, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                utc = LicenseProtocol.ParseTime(text);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // ================================================================ Kayıt

        private State Load()
        {
            var s = new State();
            _db.Read(c =>
            {
                using (var cmd = c.Create("SELECT DocumentEnc, LastValidateUtc, LastValidUtc, LastAttemptUtc, MaxSeenUtc, LastError, Notices, Active, EverActive FROM License WHERE Id = 1"))
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return 0;
                    s.Document = Decode(r.IsDBNull(0) ? null : r.GetString(0));
                    s.LastContactUtc = Time(r, 1);
                    s.LastValidUtc = Time(r, 2);
                    s.LastAttemptUtc = Time(r, 3);
                    s.MaxSeenUtc = Time(r, 4);
                    s.LastError = r.IsDBNull(5) ? null : r.GetString(5);
                    s.Notices = r.IsDBNull(6) ? new List<string>() : r.GetString(6).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    s.WasActive = !r.IsDBNull(7) && r.GetInt64(7) == 1;
                    s.EverActive = !r.IsDBNull(8) && r.GetInt64(8) == 1;
                    return 0;
                }
            });
            if (s.Document == null) s.LastValidUtc = DateTime.MinValue;
            return s;
        }

        private static DateTime Time(SQLiteDataReader r, int i)
        {
            return r.IsDBNull(i) ? DateTime.MinValue : DbTime.FromDb(r.GetString(i));
        }

        /// <summary>Saklanan cevabın şifresi çözülür ve imzası / donanım kimliği yeniden doğrulanır (DB'de elle değiştirilemez).</summary>
        private static LicenseValidateResponse Decode(string stored)
        {
            if (stored == null) return null;
            try
            {
                var doc = PipeProtocol.FromJson<LicenseValidateResponse>(SecretProtector.Unprotect(stored, Purpose));
                if (doc != null && LicenseProtocol.Verify(doc) && doc.HwId == HardwareId.Value) return doc;
                AppLog.Warn(Module, "Kayıtlı lisans bu makineye ait değil veya imzası tutmuyor; yok sayıldı.");
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Kayıtlı lisans okunamadı; yok sayıldı: " + ex.Message);
            }
            return null;
        }

        private void Save(State s)
        {
            s.Dirty = false;
            string doc = s.Document == null ? null : SecretProtector.Protect(PipeProtocol.ToJson(s.Document), Purpose);
            int status = s.Document == null ? 0 : s.Document.Status == LicenseProtocol.StatusValid ? 1 : s.Document.Status == LicenseProtocol.StatusExpired ? 2 : s.Document.Status == LicenseProtocol.StatusRevoked ? 3 : 4;
            DateTime expires;
            string expiresDb = s.Document != null && TryParse(s.Document.ExpiresUtc, out expires) ? DbTime.ToDb(expires) : null;
            var task = _db.Writer.EnqueueAsync(c => c.Execute(
                @"INSERT INTO License (Id, DocumentEnc, Status, ExpiresUtc, LastValidateUtc, LastServerUtc, LastValidUtc, LastAttemptUtc, MaxSeenUtc, LastError, Notices, Active, EverActive)
                  VALUES (1, @doc, @st, @exp, @contact, @server, @valid, @attempt, @max, @err, @notices, @active, @ever)
                  ON CONFLICT(Id) DO UPDATE SET DocumentEnc = @doc, Status = @st, ExpiresUtc = @exp, LastValidateUtc = @contact, LastServerUtc = @server,
                      LastValidUtc = @valid, LastAttemptUtc = @attempt, MaxSeenUtc = @max, LastError = @err, Notices = @notices, Active = @active, EverActive = @ever",
                ("@doc", doc), ("@st", status), ("@exp", expiresDb),
                ("@contact", DbOrNull(s.LastContactUtc)), ("@server", s.Document == null ? null : s.Document.ServerTimeUtc),
                ("@valid", DbOrNull(s.LastValidUtc)), ("@attempt", DbOrNull(s.LastAttemptUtc)), ("@max", DbOrNull(s.MaxSeenUtc)),
                ("@err", s.LastError), ("@notices", string.Join("\n", s.Notices)), ("@active", s.WasActive ? 1 : 0), ("@ever", s.EverActive ? 1 : 0)));
            task.ContinueWith(t => AppLog.Error(Module, "Lisans durumu kaydedilemedi.", t.Exception), TaskContinuationOptions.OnlyOnFaulted);
        }

        private static string DbOrNull(DateTime utc)
        {
            return utc == DateTime.MinValue ? null : DbTime.ToDb(utc);
        }

        public void Dispose()
        {
            if (_timer != null) _timer.Dispose();
        }

        // ================================================================ Tipler

        private sealed class State
        {
            public LicenseValidateResponse Document;
            public DateTime LastContactUtc = DateTime.MinValue;
            public DateTime LastValidUtc = DateTime.MinValue;
            public DateTime LastAttemptUtc = DateTime.MinValue;
            public DateTime MaxSeenUtc = DateTime.MinValue;
            public string LastError;
            public List<string> Notices = new List<string>();
            public bool WasActive;
            public bool EverActive;
            public bool Dirty;

            public State Copy()
            {
                var s = (State)MemberwiseClone();
                s.Notices = new List<string>(Notices);
                return s;
            }

            /// <summary>Bu uyarı daha önce verilmediyse işaretler ve true döner.</summary>
            public bool Notice(string key)
            {
                if (Notices.Contains(key)) return false;
                Notices.Add(key);
                if (Notices.Count > 30) Notices.RemoveRange(0, Notices.Count - 30);
                Dirty = true;
                return true;
            }
        }

        private sealed class Verdict
        {
            public Verdict(bool active, string statusText, string detail)
            {
                Active = active;
                StatusText = statusText;
                Detail = detail;
            }

            public readonly bool Active;
            public readonly string StatusText;
            public readonly string Detail;
        }

        private sealed class LicenseCallException : Exception
        {
            public LicenseCallException(string message) : base(message)
            {
            }
        }
    }
}
