using System;
using System.Security.Cryptography;
using System.Text;

namespace SecurityGuardService.Core.Security
{
    /// <summary>
    /// Hassas verileri (SMTP şifresi, SQL bağlantısı, lisans, HMAC anahtarı) Windows DPAPI ile şifreler.
    ///
    /// - Kapsam: CurrentUser. Servis SYSTEM hesabıyla çalıştığı için şifreli veriyi sadece SYSTEM çözebilir;
    ///   DB dosyası başka bir makineye / kullanıcıya kopyalansa bile açılamaz.
    /// - Her değer kendi "amacı" (ör. ayar anahtarı) ile şifrelenir: bir alanın şifreli değeri
    ///   başka bir alana kopyalanırsa çözülemez.
    /// - Saklanan biçim: "dpapi1:" + base64. Ön ek, ileride yöntem değişirse eski verinin tanınması için.
    /// </summary>
    internal static class SecretProtector
    {
        private const string Prefix = "dpapi1:";
        private static readonly byte[] AppEntropy = Encoding.UTF8.GetBytes("SecurityGuard|8f3c2a71-5b0e-4d9a-b6e4-2f7c9d1a0e53");

        public static string Protect(string plainText, string purpose)
        {
            if (plainText == null) return null;
            byte[] data = Encoding.UTF8.GetBytes(plainText);
            try
            {
                return ProtectBytes(data, purpose);
            }
            finally
            {
                Array.Clear(data, 0, data.Length);
            }
        }

        public static string Unprotect(string stored, string purpose)
        {
            if (stored == null) return null;
            byte[] data = UnprotectBytes(stored, purpose);
            try
            {
                return Encoding.UTF8.GetString(data);
            }
            finally
            {
                Array.Clear(data, 0, data.Length);
            }
        }

        public static string ProtectBytes(byte[] data, string purpose)
        {
            byte[] encrypted = ProtectedData.Protect(data, Entropy(purpose), DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(encrypted);
        }

        /// <exception cref="FormatException">Değer bu yöntemle şifrelenmemiş.</exception>
        /// <exception cref="CryptographicException">Farklı kullanıcı / makine / amaç ile şifrelenmiş veya bozuk.</exception>
        public static byte[] UnprotectBytes(string stored, string purpose)
        {
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
                throw new FormatException("Şifreli değer tanınmayan biçimde.");

            byte[] encrypted = Convert.FromBase64String(stored.Substring(Prefix.Length));
            return ProtectedData.Unprotect(encrypted, Entropy(purpose), DataProtectionScope.CurrentUser);
        }

        private static byte[] Entropy(string purpose)
        {
            using (var sha = SHA256.Create())
            {
                byte[] p = Encoding.UTF8.GetBytes(purpose ?? "");
                byte[] all = new byte[AppEntropy.Length + 1 + p.Length];
                Buffer.BlockCopy(AppEntropy, 0, all, 0, AppEntropy.Length);
                all[AppEntropy.Length] = (byte)'|';
                Buffer.BlockCopy(p, 0, all, AppEntropy.Length + 1, p.Length);
                return sha.ComputeHash(all);
            }
        }
    }
}
