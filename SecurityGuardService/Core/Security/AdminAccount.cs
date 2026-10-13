using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;

namespace SecurityGuardService.Core.Security
{
    /// <summary>
    /// Tek yönetici hesabı: oluşturma ve giriş.
    /// - Şifre PBKDF2 ile saklanır, düz metin hiçbir yerde tutulmaz ve loglanmaz.
    /// - Art arda hatalı girişte hesap geçici olarak kilitlenir (varsayılan 5 deneme / 15 dk).
    ///   Deneme sayısı ve kilit DB'de tutulur: servis yeniden başlasa da sıfırlanmaz.
    /// - Girişler sırayla işlenir (aynı anda iki deneme sayacı bozmasın).
    /// </summary>
    internal sealed class AdminAccount
    {
        private const string Module = "Yönetici";
        public const int MinPasswordLength = 8;

        private readonly GuardDatabase _db;
        private readonly SettingsStore _settings;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public AdminAccount(GuardDatabase db, SettingsStore settings)
        {
            _db = db;
            _settings = settings;
        }

        public bool Exists()
        {
            return _db.Read(c => c.Scalar<long>("SELECT COUNT(*) FROM AdminUser")) > 0;
        }

        public async Task CreateAsync(string userName, string password)
        {
            userName = (userName ?? "").Trim();
            ValidateUserName(userName);
            ValidatePassword(userName, password);

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Exists())
                    throw new GuardException(ErrorCodes.AdminExists, "Yönetici hesabı zaten oluşturulmuş.");

                var hash = PasswordHasher.Hash(password);
                await _db.Writer.EnqueueAsync(c => c.Execute(
                    @"INSERT INTO AdminUser (Id, UserName, PasswordHash, PasswordSalt, Iterations, FailedCount, CreatedUtc)
                      VALUES (1, @u, @h, @s, @i, 0, @t)",
                    ("@u", userName), ("@h", hash.Hash), ("@s", hash.Salt), ("@i", hash.Iterations), ("@t", DbTime.Now()))).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            AppLog.Info(Module, "Yönetici hesabı oluşturuldu: " + userName);
            AuditTrail.Record(_db, userName, "Yönetici hesabı oluşturuldu");
        }

        /// <returns>Kayıtlı kullanıcı adı (girişte yazılan büyük/küçük harf farkından bağımsız).</returns>
        public async Task<string> LoginAsync(string userName, string password)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var row = ReadRow();
                if (row == null)
                    throw new GuardException(ErrorCodes.NoAdmin, "Henüz yönetici hesabı oluşturulmamış.");

                DateTime now = DateTime.UtcNow;
                if (row.LockedUntilUtc.HasValue && row.LockedUntilUtc.Value > now)
                    throw new GuardException(ErrorCodes.LockedOut, LockedMessage(row.LockedUntilUtc.Value, now));

                // Kullanıcı adı yanlış olsa da hash hesaplanır: cevap süresinden adın doğru olup olmadığı anlaşılmaz
                bool passwordOk = PasswordHasher.Verify(password, row.Password);
                bool userOk = string.Equals((userName ?? "").Trim(), row.UserName, StringComparison.OrdinalIgnoreCase);

                if (userOk && passwordOk)
                {
                    await OnSuccessAsync(row, password).ConfigureAwait(false);
                    return row.UserName;
                }

                await OnFailureAsync(row, userName, now).ConfigureAwait(false);
                return null; // OnFailureAsync her zaman fırlatır
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task OnSuccessAsync(Row row, string password)
        {
            HashedPassword rehash = PasswordHasher.NeedsRehash(row.Password) ? PasswordHasher.Hash(password) : null;

            await _db.Writer.EnqueueAsync(c =>
            {
                c.Execute("UPDATE AdminUser SET FailedCount = 0, LockedUntilUtc = NULL, LastLoginUtc = @t WHERE Id = 1",
                    ("@t", DbTime.Now()));
                if (rehash != null)
                    c.Execute("UPDATE AdminUser SET PasswordHash = @h, PasswordSalt = @s, Iterations = @i WHERE Id = 1",
                        ("@h", rehash.Hash), ("@s", rehash.Salt), ("@i", rehash.Iterations));
            }).ConfigureAwait(false);

            AppLog.Info(Module, "Giriş yapıldı: " + row.UserName);
            AuditTrail.Record(_db, row.UserName, "Giriş yapıldı");
        }

        /// <summary>Hesap kilitlendi: (son denenen kullanıcı adı, hatalı deneme sayısı, kilit dakikası).</summary>
        public event Action<string, int, int> LockedOut;

        private async Task OnFailureAsync(Row row, string attemptedUser, DateTime now)
        {
            int maxFailed = GeneralSettings.GetInt(_settings, SettingKeys.AuthMaxFailedLogins);
            int lockMinutes = GeneralSettings.GetInt(_settings, SettingKeys.AuthLockMinutes);
            int failed = row.FailedCount + 1;
            string who = string.IsNullOrWhiteSpace(attemptedUser) ? "(boş)" : attemptedUser.Trim();

            if (failed >= maxFailed)
            {
                DateTime lockedUntil = now.AddMinutes(lockMinutes);
                await _db.Writer.EnqueueAsync(c => c.Execute(
                    "UPDATE AdminUser SET FailedCount = 0, LockedUntilUtc = @l WHERE Id = 1",
                    ("@l", DbTime.ToDb(lockedUntil)))).ConfigureAwait(false);

                AppLog.Warn(Module, maxFailed + " hatalı giriş, hesap " + lockMinutes + " dakika kilitlendi. Son denenen kullanıcı adı: " + who);
                AuditTrail.Record(_db, who, "Hesap kilitlendi", maxFailed + " hatalı giriş, " + lockMinutes + " dakika");
                var handler = LockedOut;
                if (handler != null)
                {
                    try { handler(who, maxFailed, lockMinutes); }
                    catch (Exception ex) { AppLog.Error(Module, "Kilitlenme bildirimi işlenemedi.", ex); }
                }
                throw new GuardException(ErrorCodes.LockedOut, LockedMessage(lockedUntil, now));
            }

            await _db.Writer.EnqueueAsync(c => c.Execute(
                "UPDATE AdminUser SET FailedCount = @f WHERE Id = 1", ("@f", failed))).ConfigureAwait(false);

            AppLog.Warn(Module, "Hatalı giriş denemesi (" + failed + "/" + maxFailed + "). Denenen kullanıcı adı: " + who);
            AuditTrail.Record(_db, who, "Hatalı giriş", failed + "/" + maxFailed);
            throw new GuardException(ErrorCodes.LoginFailed,
                "Kullanıcı adı veya şifre hatalı. Kalan deneme hakkı: " + (maxFailed - failed) + ".");
        }

        private static string LockedMessage(DateTime lockedUntil, DateTime now)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling((lockedUntil - now).TotalMinutes));
            return "Çok fazla hatalı giriş. Hesap " + minutes + " dakika kilitli.";
        }

        private static void ValidateUserName(string userName)
        {
            if (userName.Length < 3 || userName.Length > 50)
                throw new GuardException(ErrorCodes.Validation, "Kullanıcı adı 3-50 karakter olmalı.");
            if (!userName.All(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-'))
                throw new GuardException(ErrorCodes.Validation, "Kullanıcı adında sadece harf, rakam ve . _ - kullanılabilir.");
        }

        private static void ValidatePassword(string userName, string password)
        {
            if (password == null || password.Length < MinPasswordLength)
                throw new GuardException(ErrorCodes.Validation, "Şifre en az " + MinPasswordLength + " karakter olmalı.");
            if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
                throw new GuardException(ErrorCodes.Validation, "Şifre en az bir harf ve bir rakam içermeli.");
            if (password.IndexOf(userName, StringComparison.OrdinalIgnoreCase) >= 0)
                throw new GuardException(ErrorCodes.Validation, "Şifre kullanıcı adını içeremez.");
        }

        private Row ReadRow()
        {
            return _db.Read(c =>
            {
                using (var cmd = c.Create("SELECT UserName, PasswordHash, PasswordSalt, Iterations, FailedCount, LockedUntilUtc FROM AdminUser WHERE Id = 1"))
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return new Row
                    {
                        UserName = r.GetString(0),
                        Password = new HashedPassword(r.GetString(1), r.GetString(2), r.GetInt32(3)),
                        FailedCount = r.GetInt32(4),
                        LockedUntilUtc = r.IsDBNull(5) ? (DateTime?)null : DbTime.FromDb(r.GetString(5))
                    };
                }
            });
        }

        private sealed class Row
        {
            public string UserName;
            public HashedPassword Password;
            public int FailedCount;
            public DateTime? LockedUntilUtc;
        }
    }
}
