using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MimeKit;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;

namespace SecurityGuardService.Mail
{
    /// <summary>
    /// Mail: SMTP ayarları, alıcılar, HTML imza ve gönderim kuyruğu.
    ///
    /// Gönderim her zaman kuyruktan (MailQueue) yapılır: olayı üreten modül beklemez.
    /// Gönderilemeyen mail artan aralıklarla tekrar denenir (1, 2, 5, 10, 30, 60 dk...).
    /// Mail içeriği ve şifre loglanmaz; sadece konu ve sonuç loglanır.
    /// </summary>
    internal sealed class MailService : IDisposable
    {
        private const string Module = "Mail";
        private const int MaxSignatureLength = 200000;
        private const int MaxImageBytes = 512 * 1024;
        private const int MaxImages = 10;
        private const int MaxAttempts = 50;
        private const int BatchSize = 20;

        private const int StatusPending = 0;
        private const int StatusSent = 1;
        private const int StatusFailed = 2;

        /// <summary>24 saatten uzun gönderilemeyen mail artık gönderilmez (eski uyarı yağmuru olmasın); yerine tek özet gider.</summary>
        private const int StatusExpired = 3;
        internal static readonly TimeSpan MaxQueueAge = TimeSpan.FromHours(24);

        private static readonly int[] RetryMinutes = { 1, 2, 5, 10, 30, 60 };

        private readonly GuardDatabase _db;
        private readonly SettingsStore _settings;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private Thread _worker;
        private bool _notConfiguredLogged;

        /// <summary>Kayıt için şart: aynı bilgilerle bu süre içinde başarılı test maili gönderilmiş olmalı.</summary>
        private static readonly TimeSpan TestValidity = TimeSpan.FromMinutes(30);
        private readonly object _testLock = new object();
        private string _lastTestFingerprint; // sadece bellekte, şifrenin kendisi değil özeti
        private DateTime _lastTestUtc;

        public MailService(GuardDatabase db, SettingsStore settings)
        {
            _db = db;
            _settings = settings;
        }

        // ================================================================ SMTP ayarları

        public MailSettingsDto GetSettings()
        {
            return new MailSettingsDto
            {
                Host = _settings.Get(SettingKeys.MailHost, ""),
                Port = _settings.GetInt(SettingKeys.MailPort, 587),
                Security = _settings.GetInt(SettingKeys.MailSecurity, (int)MailSecurity.Auto),
                UserName = _settings.Get(SettingKeys.MailUser, ""),
                HasPassword = _settings.Contains(SettingKeys.MailPassword),
                FromAddress = _settings.Get(SettingKeys.MailFromAddress, ""),
                FromName = _settings.Get(SettingKeys.MailFromName, "SecurityGuard")
            };
        }

        /// <summary>SMTP host ve gönderen adresi tanımlı mı (mail gönderilebilir mi).</summary>
        public bool IsConfigured
        {
            get { return LoadConfig() != null; }
        }

        public async Task SaveSettingsAsync(MailSettingsDto dto, string userName)
        {
            var cfg = Validate(dto);
            if (string.IsNullOrEmpty(cfg.Password))
                cfg.Password = _settings.GetSecret(SettingKeys.MailPassword);

            // Çalışmayan ayar kaydedilmesin: aynı bilgilerle yakın zamanda başarılı test şart
            lock (_testLock)
            {
                if (_lastTestFingerprint == null || _lastTestFingerprint != Fingerprint(cfg)
                    || DateTime.UtcNow - _lastTestUtc > TestValidity)
                    throw new GuardException(ErrorCodes.Validation,
                        "Önce bu ayarlarla test maili gönderin. Test başarılı olmadan mail ayarları kaydedilemez.");
                _lastTestFingerprint = null; // bir test = bir kayıt
            }

            await _settings.SetAsync(SettingKeys.MailHost, cfg.Host).ConfigureAwait(false);
            await _settings.SetAsync(SettingKeys.MailPort, cfg.Port).ConfigureAwait(false);
            await _settings.SetAsync(SettingKeys.MailSecurity, (int)cfg.Security).ConfigureAwait(false);
            await _settings.SetAsync(SettingKeys.MailUser, cfg.UserName).ConfigureAwait(false);
            await _settings.SetAsync(SettingKeys.MailFromAddress, cfg.FromAddress).ConfigureAwait(false);
            await _settings.SetAsync(SettingKeys.MailFromName, cfg.FromName).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(dto.Password))
                await _settings.SetSecretAsync(SettingKeys.MailPassword, dto.Password).ConfigureAwait(false);

            _notConfiguredLogged = false;
            AuditTrail.Record(_db, userName, "Mail ayarları değiştirildi",
                cfg.Host + ":" + cfg.Port + " (" + cfg.Security + "), gönderen " + cfg.FromAddress
                + (string.IsNullOrEmpty(dto.Password) ? "" : ", şifre değişti"));
            _wake.Set(); // bekleyen mailler varsa hemen denensin
        }

        private static SmtpConfig Validate(MailSettingsDto dto)
        {
            if (dto == null) throw new GuardException(ErrorCodes.InvalidRequest, "Mail ayarları eksik.");

            var cfg = new SmtpConfig
            {
                Host = (dto.Host ?? "").Trim(),
                Port = dto.Port,
                Security = (MailSecurity)dto.Security,
                UserName = (dto.UserName ?? "").Trim(),
                Password = dto.Password,
                FromAddress = (dto.FromAddress ?? "").Trim(),
                FromName = (dto.FromName ?? "").Trim()
            };

            if (cfg.Host.Length == 0 || cfg.Host.Length > 255 || cfg.Host.Contains(" "))
                throw new GuardException(ErrorCodes.Validation, "SMTP sunucu adını girin (ör. smtp.firmaniz.com).");
            if (cfg.Port < 1 || cfg.Port > 65535)
                throw new GuardException(ErrorCodes.Validation, "Port 1-65535 arasında olmalı (genelde 587 veya 465).");
            if (!Enum.IsDefined(typeof(MailSecurity), cfg.Security))
                throw new GuardException(ErrorCodes.Validation, "Geçersiz güvenlik seçimi.");
            if (!IsValidEmail(cfg.FromAddress))
                throw new GuardException(ErrorCodes.Validation, "Gönderen mail adresi geçersiz.");
            if (cfg.FromName.Length > 100 || cfg.UserName.Length > 255)
                throw new GuardException(ErrorCodes.Validation, "Gönderen adı veya kullanıcı adı çok uzun.");
            return cfg;
        }

        /// <summary>Kayıtlı ayarlar (şifre çözülmüş). Eksikse null.</summary>
        private SmtpConfig LoadConfig()
        {
            var s = GetSettings();
            if (string.IsNullOrWhiteSpace(s.Host) || !IsValidEmail(s.FromAddress)) return null;
            return new SmtpConfig
            {
                Host = s.Host,
                Port = s.Port,
                Security = (MailSecurity)s.Security,
                UserName = s.UserName,
                Password = _settings.GetSecret(SettingKeys.MailPassword),
                FromAddress = s.FromAddress,
                FromName = s.FromName
            };
        }

        internal static bool IsValidEmail(string address)
        {
            MailboxAddress parsed;
            return !string.IsNullOrWhiteSpace(address) && address.Length <= 254
                && MailboxAddress.TryParse(address.Trim(), out parsed)
                && parsed.Address.Contains("@") && parsed.Address.IndexOf('.', parsed.Address.IndexOf('@')) > 0;
        }

        // ================================================================ Test maili

        /// <summary>Ekrandaki ayarlarla hemen gönderir (kuyruğa girmez). Şifre boşsa kayıtlı şifre kullanılır.</summary>
        public void SendTest(MailTestRequest request, string userName)
        {
            var cfg = Validate(request == null ? null : request.Settings);
            if (string.IsNullOrEmpty(cfg.Password))
                cfg.Password = _settings.GetSecret(SettingKeys.MailPassword);

            string to = (request.To ?? "").Trim();
            if (!IsValidEmail(to))
                throw new GuardException(ErrorCodes.Validation, "Test maili gönderilecek adres geçersiz.");

            string body =
                "<h2 style=\"margin:0 0 12px 0\">SecurityGuard test maili</h2>" +
                "<p>Bu mail, SecurityGuard mail ayarlarının doğru çalıştığını göstermek için gönderildi.</p>" +
                "<table style=\"border-collapse:collapse\">" +
                Row("Sunucu", Environment.MachineName) +
                Row("Zaman", DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + " (UTC " + DateTime.UtcNow.ToString("HH:mm") + ")") +
                Row("SMTP", cfg.Host + ":" + cfg.Port) +
                Row("Gönderen işlem", "Yönetici: " + userName) +
                "</table>";

            var message = MailComposer.Build(cfg, new[] { to }, "SecurityGuard test maili - " + Environment.MachineName,
                body, _settings.Get(SettingKeys.MailSignatureHtml, ""), LoadImages());

            SmtpClient client = null;
            try
            {
                client = SmtpSender.Connect(cfg);
                client.Send(message);
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Test maili gönderilemedi (" + cfg.Host + ":" + cfg.Port + " → " + to + "): " + ex.GetType().Name + ": " + ex.Message);
                throw new GuardException(ErrorCodes.MailFailed, SmtpSender.Explain(ex));
            }
            finally
            {
                if (client != null) SmtpSender.Disconnect(client);
            }

            lock (_testLock)
            {
                _lastTestFingerprint = Fingerprint(cfg);
                _lastTestUtc = DateTime.UtcNow;
            }

            AppLog.Info(Module, "Test maili gönderildi → " + to);
            AuditTrail.Record(_db, userName, "Test maili gönderildi", to);
        }

        /// <summary>Bağlantıyı etkileyen alanların özeti (gönderen adı hariç). Şifre açık halde tutulmaz.</summary>
        private static string Fingerprint(SmtpConfig cfg)
        {
            string raw = string.Join("\u0001", cfg.Host.ToLowerInvariant(), cfg.Port.ToString(), ((int)cfg.Security).ToString(),
                cfg.UserName, cfg.Password ?? "", cfg.FromAddress.ToLowerInvariant());
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw)));
        }

        private static string Row(string label, string value)
        {
            return "<tr><td style=\"padding:3px 16px 3px 0;color:#666666\">" + WebUtility.HtmlEncode(label) +
                   "</td><td style=\"padding:3px 0\">" + WebUtility.HtmlEncode(value) + "</td></tr>";
        }

        // ================================================================ Alıcılar

        public List<RecipientDto> ListRecipients()
        {
            return _db.Read(c =>
            {
                var list = new List<RecipientDto>();
                using (var cmd = c.Create("SELECT Id, Email, DisplayName, IsEnabled, CreatedUtc FROM Recipients ORDER BY Id"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new RecipientDto
                        {
                            Id = r.GetInt64(0),
                            Email = r.GetString(1),
                            DisplayName = r.IsDBNull(2) ? null : r.GetString(2),
                            IsEnabled = r.GetInt64(3) != 0,
                            CreatedUtc = r.GetString(4)
                        });
                }
                return list;
            });
        }

        public async Task AddRecipientAsync(string email, string displayName, string userName)
        {
            email = (email ?? "").Trim();
            displayName = (displayName ?? "").Trim();
            if (!IsValidEmail(email))
                throw new GuardException(ErrorCodes.Validation, "Geçersiz mail adresi.");
            if (displayName.Length > 100) displayName = displayName.Substring(0, 100);
            if (ListRecipients().Any(r => string.Equals(r.Email, email, StringComparison.OrdinalIgnoreCase)))
                throw new GuardException(ErrorCodes.Validation, email + " zaten alıcı listesinde.");

            await _db.Writer.EnqueueAsync(c => c.Execute(
                "INSERT INTO Recipients (Email, DisplayName, IsEnabled, CreatedUtc) VALUES (@e, @n, 1, @t)",
                ("@e", email), ("@n", displayName.Length == 0 ? null : displayName), ("@t", DbTime.Now()))).ConfigureAwait(false);

            AuditTrail.Record(_db, userName, "Mail alıcısı eklendi", email);
            _wake.Set();
        }

        public async Task DeleteRecipientsAsync(ICollection<long> ids, string userName)
        {
            if (ids == null || ids.Count == 0)
                throw new GuardException(ErrorCodes.Validation, "Silinecek alıcıyı seçin.");
            var rows = ListRecipients().Where(r => ids.Contains(r.Id)).ToList();
            if (rows.Count == 0) return;

            await _db.Writer.EnqueueAsync(c =>
            {
                foreach (var r in rows) c.Execute("DELETE FROM Recipients WHERE Id = @id", ("@id", r.Id));
            }).ConfigureAwait(false);

            foreach (var r in rows) AuditTrail.Record(_db, userName, "Mail alıcısı silindi", r.Email);

            // Silinen alıcı kural eşleşmelerinden DB'de otomatik düştü (ON DELETE CASCADE); önbellekler yenilensin
            var handler = RecipientsDeleted;
            if (handler != null)
            {
                try { handler(); }
                catch (Exception ex) { AppLog.Error(Module, "Alıcı silme bildirimi işlenemedi.", ex); }
            }
        }

        /// <summary>Alıcı silindiğinde (kurallardaki alıcı listeleri yenilensin diye).</summary>
        public event Action RecipientsDeleted;

        public async Task SetRecipientEnabledAsync(long id, bool enabled, string userName)
        {
            var row = ListRecipients().FirstOrDefault(r => r.Id == id);
            if (row == null) throw new GuardException(ErrorCodes.Validation, "Alıcı bulunamadı.");
            if (row.IsEnabled == enabled) return;

            await _db.Writer.EnqueueAsync(c => c.Execute("UPDATE Recipients SET IsEnabled = @e WHERE Id = @id",
                ("@e", enabled ? 1 : 0), ("@id", id))).ConfigureAwait(false);

            AuditTrail.Record(_db, userName, enabled ? "Mail alıcısı aktif edildi" : "Mail alıcısı pasif edildi", row.Email);
            if (enabled) _wake.Set();
        }

        private List<string> EnabledRecipientEmails()
        {
            return ListRecipients().Where(r => r.IsEnabled).Select(r => r.Email).ToList();
        }

        // ================================================================ İmza

        public SignatureDto GetSignature()
        {
            return new SignatureDto
            {
                Html = _settings.Get(SettingKeys.MailSignatureHtml, ""),
                Images = LoadImages().Select(ToDto).ToList()
            };
        }

        public async Task SaveSignatureAsync(string html, string userName)
        {
            html = html ?? "";
            if (html.Length > MaxSignatureLength)
                throw new GuardException(ErrorCodes.Validation, "İmza çok uzun (en fazla " + (MaxSignatureLength / 1000) + " bin karakter).");
            await _settings.SetAsync(SettingKeys.MailSignatureHtml, html).ConfigureAwait(false);
            AuditTrail.Record(_db, userName, "Mail imzası değiştirildi", html.Length + " karakter");
        }

        public async Task<SignatureImageDto> AddImageAsync(string fileName, string base64, string userName)
        {
            byte[] data;
            try
            {
                data = Convert.FromBase64String(base64 ?? "");
            }
            catch (FormatException)
            {
                throw new GuardException(ErrorCodes.InvalidRequest, "Resim verisi okunamadı.");
            }
            if (data.Length == 0) throw new GuardException(ErrorCodes.Validation, "Resim dosyası boş.");
            if (data.Length > MaxImageBytes)
                throw new GuardException(ErrorCodes.Validation, "Resim en fazla " + (MaxImageBytes / 1024) + " KB olabilir (mail boyutu şişmesin).");

            string mime = DetectImageType(data);
            if (mime == null)
                throw new GuardException(ErrorCodes.Validation, "Sadece PNG, JPG veya GIF resim eklenebilir.");
            if (LoadImages().Count >= MaxImages)
                throw new GuardException(ErrorCodes.Validation, "En fazla " + MaxImages + " resim eklenebilir.");

            fileName = System.IO.Path.GetFileName((fileName ?? "resim").Trim());
            if (fileName.Length == 0 || fileName.Length > 100) fileName = "resim";
            string contentId = "img" + RandomHex(6);

            await _db.Writer.EnqueueAsync(c => c.Execute(
                "INSERT INTO MailImages (ContentId, FileName, MimeType, Data, CreatedUtc) VALUES (@c, @f, @m, @d, @t)",
                ("@c", contentId), ("@f", fileName), ("@m", mime), ("@d", data), ("@t", DbTime.Now()))).ConfigureAwait(false);

            AuditTrail.Record(_db, userName, "İmzaya resim eklendi", fileName + " (" + (data.Length / 1024) + " KB)");
            return new SignatureImageDto { ContentId = contentId, FileName = fileName, MimeType = mime, Base64 = Convert.ToBase64String(data) };
        }

        public async Task DeleteImageAsync(string contentId, string userName)
        {
            var img = LoadImages().FirstOrDefault(i => i.ContentId == contentId);
            if (img == null) return;
            await _db.Writer.EnqueueAsync(c => c.Execute("DELETE FROM MailImages WHERE ContentId = @c", ("@c", contentId))).ConfigureAwait(false);
            AuditTrail.Record(_db, userName, "İmzadan resim silindi", img.FileName);
        }

        private List<MailImage> LoadImages()
        {
            return _db.Read(c =>
            {
                var list = new List<MailImage>();
                using (var cmd = c.Create("SELECT ContentId, FileName, MimeType, Data FROM MailImages ORDER BY CreatedUtc"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new MailImage { ContentId = r.GetString(0), FileName = r.GetString(1), MimeType = r.GetString(2), Data = (byte[])r.GetValue(3) });
                }
                return list;
            });
        }

        private static SignatureImageDto ToDto(MailImage i)
        {
            return new SignatureImageDto { ContentId = i.ContentId, FileName = i.FileName, MimeType = i.MimeType, Base64 = Convert.ToBase64String(i.Data) };
        }

        /// <summary>Dosya adına değil içeriğe bakarak resim türü (uzantısı değiştirilmiş dosya kabul edilmez).</summary>
        private static string DetectImageType(byte[] d)
        {
            if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "image/png";
            if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "image/jpeg";
            if (d.Length >= 6 && d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x38) return "image/gif";
            return null;
        }

        private static string RandomHex(int bytes)
        {
            var b = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return string.Concat(b.Select(x => x.ToString("x2")));
        }

        // ================================================================ Kuyruk

        /// <summary>
        /// Maili kuyruğa ekler (çağıran beklemez). Alıcı verilmezse gönderim anındaki aktif alıcılara gider.
        /// İmza gönderim anında eklenir.
        /// </summary>
        /// <param name="priority">Yüksek önce gönderilir (kritik olaylar).</param>
        public bool Enqueue(string subject, string bodyHtml, int priority = 0, IEnumerable<string> recipients = null)
        {
            string to = recipients == null ? null : string.Join(",", recipients);
            string now = DbTime.Now();
            bool queued = _db.Writer.Enqueue(c => c.Execute(
                @"INSERT INTO MailQueue (CreatedUtc, Subject, BodyHtml, Recipients, Priority, Status, Attempts, NextAttemptUtc)
                  VALUES (@c, @s, @b, @r, @p, 0, 0, @n)",
                ("@c", now), ("@s", subject), ("@b", bodyHtml), ("@r", to), ("@p", priority), ("@n", now)));

            if (!queued) AppLog.Error(Module, "Mail kuyruğa eklenemedi: " + subject);
            _wake.Set();
            return queued;
        }

        /// <summary>
        /// Olay maili: kuralda seçili alıcılara gider; seçim yoksa tüm aktif alıcılara.
        /// Seçilenlerin hepsi pasif / silinmişse uyarı kaybolmasın diye tüm aktif alıcılara gider.
        /// </summary>
        public bool EnqueueForEvent(string eventType, IList<long> recipientIds, string subject, string bodyHtml, int priority = 0)
        {
            List<string> to = null; // null = gönderim anındaki tüm aktif alıcılar
            if (recipientIds != null && recipientIds.Count > 0)
            {
                to = ListRecipients().Where(r => r.IsEnabled && recipientIds.Contains(r.Id)).Select(r => r.Email).ToList();
                if (to.Count == 0)
                {
                    AppLog.Warn(Module, eventType + " için seçilen alıcıların hiçbiri aktif değil; mail tüm aktif alıcılara gönderilecek.");
                    to = null;
                }
            }
            return Enqueue(subject, bodyHtml, priority, to);
        }

        /// <summary>Gönderilmeyi bekleyen mail sayısı.</summary>
        public int PendingCount()
        {
            return (int)_db.Read(c => c.Scalar<long>("SELECT COUNT(*) FROM MailQueue WHERE Status = 0"));
        }

        public void Start()
        {
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "MailSender" };
            _worker.Start();
        }

        private void WorkerLoop()
        {
            var handles = new WaitHandle[] { _stop, _wake };
            int failures = 0;
            while (true)
            {
                try
                {
                    ProcessQueue();
                    failures = 0;
                }
                catch (Exception ex)
                {
                    failures++;
                    AppLog.Error(Module, "Mail kuyruğu işlenirken hata (art arda " + failures + ").", ex);
                }

                // Yeni mail gelince hemen, yoksa 15 sn'de bir (tekrar zamanı gelenler için) bakılır
                int timeout = failures > 0 ? Math.Min(300000, failures * 15000) : 15000;
                if (WaitHandle.WaitAny(handles, timeout) == 0) return;
            }
        }

        private void ProcessQueue()
        {
            var due = ReadDue();
            if (due.Count == 0) return;

            var cfg = LoadConfig();
            if (cfg == null)
            {
                if (!_notConfiguredLogged)
                {
                    AppLog.Warn(Module, due.Count + "+ mail bekliyor ama SMTP ayarları yapılmamış.");
                    _notConfiguredLogged = true;
                }
                return; // ayar yapılınca (SaveSettings) kuyruk hemen denenir
            }

            // Gönderebilecek durumdayız: çok eski bekleyenler tek özete dönüşür, sonra kuyruk yeniden okunur
            if (ExpireStale()) due = ReadDue();

            var recipients = EnabledRecipientEmails();
            string signature = _settings.Get(SettingKeys.MailSignatureHtml, "");
            var images = LoadImages();

            SmtpClient client = null;
            try
            {
                foreach (var item in due)
                {
                    var to = item.Recipients != null
                        ? item.Recipients.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList()
                        : recipients;
                    if (to.Count == 0)
                    {
                        MarkFailed(item, "Aktif mail alıcısı yok.");
                        continue;
                    }

                    MimeMessage message;
                    try
                    {
                        message = MailComposer.Build(cfg, to, item.Subject, item.BodyHtml, signature, images);
                    }
                    catch (Exception ex)
                    {
                        MarkFailed(item, "Mail oluşturulamadı: " + ex.Message);
                        continue;
                    }

                    try
                    {
                        if (client == null) client = SmtpSender.Connect(cfg);
                        client.Send(message);
                        MarkSent(item);
                    }
                    catch (SmtpCommandException ex)
                    {
                        // Sadece bu mail reddedildi (ör. alıcı adresi); bağlantı kullanılabilir
                        MarkFailed(item, SmtpSender.Explain(ex));
                    }
                }
            }
            catch (Exception ex)
            {
                // Bağlantı / kimlik doğrulama hatası: bu turdaki kalan mailler sonra tekrar denenir
                string reason = SmtpSender.Explain(ex);
                AppLog.Warn(Module, "SMTP bağlantısı kurulamadı: " + reason);
                foreach (var item in due.Where(d => !d.Done)) MarkFailed(item, reason);
            }
            finally
            {
                if (client != null) SmtpSender.Disconnect(client);
            }
        }

        /// <summary>
        /// <see cref="MaxQueueAge"/>'den eski bekleyen mailleri "süresi geçti" yapar ve yerine tek özet mail kuyruğa ekler
        /// (aynı işlemde: yarım kalmaz). SMTP haftalar sonra ayarlanırsa yüzlerce eski uyarı bir anda gitmez.
        /// </summary>
        /// <returns>true = özet eklendi.</returns>
        private bool ExpireStale()
        {
            string cutoff = DbTime.ToDb(DateTime.UtcNow - MaxQueueAge);
            string now = DbTime.Now();
            int count = _db.Writer.EnqueueAsync(c =>
            {
                var rows = new List<KeyValuePair<string, string>>();
                using (var cmd = c.Create("SELECT CreatedUtc, Subject FROM MailQueue WHERE Status = @p AND CreatedUtc < @cut ORDER BY Id",
                    ("@p", StatusPending), ("@cut", cutoff)))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) rows.Add(new KeyValuePair<string, string>(r.GetString(0), r.GetString(1)));
                if (rows.Count == 0) return 0;

                c.Execute("UPDATE MailQueue SET Status = @e, LastError = @why WHERE Status = @p AND CreatedUtc < @cut",
                    ("@e", StatusExpired), ("@why", "Süresi geçti: " + (int)MaxQueueAge.TotalHours + " saatten uzun gönderilemedi, özet mail ile bildirildi."),
                    ("@p", StatusPending), ("@cut", cutoff));

                string subject = "[SecurityGuard] Gönderilemeyen " + rows.Count + " eski bildirim - " + Environment.MachineName;
                c.Execute(@"INSERT INTO MailQueue (CreatedUtc, Subject, BodyHtml, Recipients, Priority, Status, Attempts, NextAttemptUtc)
                            VALUES (@c, @s, @b, NULL, 1, @p, 0, @c)",
                    ("@c", now), ("@s", subject), ("@b", ExpiredSummaryHtml(rows)), ("@p", StatusPending));
                return rows.Count;
            }).Result;

            if (count == 0) return false;
            AppLog.Warn(Module, count + " mail " + (int)MaxQueueAge.TotalHours + " saatten uzun gönderilemediği için iptal edildi; yerine özet mail kuyruğa alındı.");
            return true;
        }

        private static string ExpiredSummaryHtml(List<KeyValuePair<string, string>> rows)
        {
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            Func<string, string> local = t => DbTime.FromDb(t).ToLocalTime().ToString("dd.MM.yyyy HH:mm", tr);
            var groups = rows.GroupBy(r => r.Value)
                .Select(g => new { Subject = g.Key, Count = g.Count(), First = g.Min(x => x.Key), Last = g.Max(x => x.Key) })
                .OrderBy(g => g.First)
                .ToList();

            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px\">");
            sb.Append("<h2 style=\"margin:0 0 4px 0;color:#ef6c00\">Gönderilemeyen ").Append(rows.Count).Append(" eski bildirim</h2>");
            sb.Append("<p style=\"margin:0 0 12px 0\">Aşağıdaki bildirimler ").Append((int)MaxQueueAge.TotalHours)
              .Append(" saatten uzun süre gönderilemediği için (SMTP ayarı yapılmamış veya mail sunucusuna ulaşılamamış) tek tek gönderilmedi. ")
              .Append("Olayların tamamı SecurityGuard → Olay Logu'nda duruyor.</p>");
            sb.Append("<table cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px\">");
            sb.Append("<tr style=\"background:#f2f2f2\"><th align=\"left\">Bildirim</th><th align=\"right\">Adet</th><th align=\"left\">İlk</th><th align=\"left\">Son</th></tr>");
            foreach (var g in groups.Take(50))
            {
                sb.Append("<tr><td style=\"border-bottom:1px solid #eee\">").Append(WebUtility.HtmlEncode(g.Subject.Replace("[SecurityGuard] ", "")))
                  .Append("</td><td align=\"right\" style=\"border-bottom:1px solid #eee\">").Append(g.Count)
                  .Append("</td><td style=\"border-bottom:1px solid #eee;white-space:nowrap\">").Append(local(g.First))
                  .Append("</td><td style=\"border-bottom:1px solid #eee;white-space:nowrap\">").Append(local(g.Last)).Append("</td></tr>");
            }
            sb.Append("</table>");
            if (groups.Count > 50) sb.Append("<p>… ve ").Append(groups.Count - 50).Append(" farklı bildirim daha.</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        private List<QueueItem> ReadDue()
        {
            string now = DbTime.Now();
            return _db.Read(c =>
            {
                var list = new List<QueueItem>();
                using (var cmd = c.Create(
                    @"SELECT Id, Subject, BodyHtml, Recipients, Attempts FROM MailQueue
                      WHERE Status = 0 AND NextAttemptUtc <= @now ORDER BY Priority DESC, Id LIMIT @n",
                    ("@now", now), ("@n", BatchSize)))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new QueueItem
                        {
                            Id = r.GetInt64(0),
                            Subject = r.GetString(1),
                            BodyHtml = r.GetString(2),
                            Recipients = r.IsDBNull(3) ? null : r.GetString(3),
                            Attempts = r.GetInt32(4)
                        });
                }
                return list;
            });
        }

        private void MarkSent(QueueItem item)
        {
            item.Done = true;
            // Yazılması beklenir: kuyruk tekrar okunduğunda aynı mail ikinci kez gönderilmesin
            _db.Writer.EnqueueAsync(c => c.Execute("UPDATE MailQueue SET Status = @s, SentUtc = @t, LastError = NULL WHERE Id = @id",
                ("@s", StatusSent), ("@t", DbTime.Now()), ("@id", item.Id))).Wait(TimeSpan.FromSeconds(30));
            AppLog.Info(Module, "Gönderildi: " + item.Subject);
        }

        private void MarkFailed(QueueItem item, string reason)
        {
            item.Done = true;
            int attempts = item.Attempts + 1;
            bool giveUp = attempts >= MaxAttempts;
            DateTime next = DateTime.UtcNow.AddMinutes(RetryMinutes[Math.Min(attempts - 1, RetryMinutes.Length - 1)]);

            _db.Writer.EnqueueAsync(c => c.Execute(
                "UPDATE MailQueue SET Status = @s, Attempts = @a, NextAttemptUtc = @n, LastError = @e WHERE Id = @id",
                ("@s", giveUp ? StatusFailed : StatusPending), ("@a", attempts), ("@n", DbTime.ToDb(next)), ("@e", reason), ("@id", item.Id)))
                .Wait(TimeSpan.FromSeconds(30));

            if (giveUp)
                AppLog.Error(Module, "Mail " + attempts + " denemeden sonra gönderilemedi, vazgeçildi: " + item.Subject + " (" + reason + ")");
            else
                AppLog.Warn(Module, "Gönderilemedi (" + attempts + ". deneme), " + next.ToLocalTime().ToString("HH:mm") + "'de tekrar denenecek: " + item.Subject + " (" + reason + ")");
        }

        /// <summary>Kapanıştan önce bekleyen mailleri göndermeyi dener (kapanış / kritik mailleri dışarı çıksın).</summary>
        public void FlushBeforeStop(TimeSpan max)
        {
            if (!IsConfigured) return;
            var end = DateTime.UtcNow + max;
            _wake.Set();
            while (DateTime.UtcNow < end && PendingCount() > 0) Thread.Sleep(250);
        }

        public void Dispose()
        {
            _stop.Set();
            if (_worker != null) _worker.Join(TimeSpan.FromSeconds(10));
        }

        private sealed class QueueItem
        {
            public long Id;
            public string Subject;
            public string BodyHtml;
            public string Recipients;
            public int Attempts;
            public bool Done;
        }
    }
}
