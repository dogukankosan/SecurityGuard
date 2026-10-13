using System;
using System.Security.Cryptography;

namespace SecurityGuardService.Core.Security
{
    /// <summary>
    /// Yönetici şifresi için PBKDF2 (HMAC-SHA256). Şifre hiçbir yerde düz metin tutulmaz;
    /// sadece salt + hash + iterasyon sayısı saklanır.
    /// </summary>
    internal static class PasswordHasher
    {
        /// <summary>Yeni hash'lerde kullanılan iterasyon. Artırılırsa eski hash'ler girişte otomatik yenilenir.</summary>
        public const int CurrentIterations = 600000;

        private const int SaltSize = 16;
        private const int HashSize = 32;

        public static HashedPassword Hash(string password)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Şifre boş olamaz.", "password");

            byte[] salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);

            byte[] hash = Derive(password, salt, CurrentIterations);
            return new HashedPassword(Convert.ToBase64String(hash), Convert.ToBase64String(salt), CurrentIterations);
        }

        public static bool Verify(string password, HashedPassword stored)
        {
            if (string.IsNullOrEmpty(password) || stored == null) return false;

            byte[] expected = Convert.FromBase64String(stored.Hash);
            byte[] actual = Derive(password, Convert.FromBase64String(stored.Salt), stored.Iterations);
            return FixedTimeEquals(expected, actual);
        }

        /// <summary>Hash eski (daha az iterasyonlu) ise true: başarılı girişte yeniden hash'lenmeli.</summary>
        public static bool NeedsRehash(HashedPassword stored)
        {
            return stored.Iterations < CurrentIterations;
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(HashSize);
        }

        // Karşılaştırma süresi eşleşen bayt sayısına göre değişmez (zamanlama saldırısına kapalı)
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    internal sealed class HashedPassword
    {
        public readonly string Hash;
        public readonly string Salt;
        public readonly int Iterations;

        public HashedPassword(string hash, string salt, int iterations)
        {
            Hash = hash;
            Salt = salt;
            Iterations = iterations;
        }
    }
}
