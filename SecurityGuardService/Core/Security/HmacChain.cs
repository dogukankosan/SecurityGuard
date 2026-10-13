using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SecurityGuardService.Core.Security
{
    /// <summary>
    /// Güvenlik olay logu (EventLog) bütünlük zinciri.
    /// Her satırın HMAC'i = HMAC(anahtar, önceki satırın HMAC'i + bu satırın alanları).
    /// Bir satır silinir, değiştirilir veya araya satır eklenirse zincir kırılır.
    /// Anahtar DPAPI ile şifreli saklanır: DB'ye yazabilen biri anahtarı bilmeden zinciri yeniden hesaplayamaz.
    /// </summary>
    internal sealed class HmacChain
    {
        private const int KeySize = 32;
        private readonly byte[] _key;

        public HmacChain(byte[] key)
        {
            if (key == null || key.Length < KeySize) throw new ArgumentException("HMAC anahtarı geçersiz.", "key");
            _key = (byte[])key.Clone();
        }

        public static byte[] NewKey()
        {
            byte[] key = new byte[KeySize];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(key);
            return key;
        }

        /// <summary>
        /// Satırın HMAC'ini hesaplar (küçük harf hex).
        /// Alanlar uzunluk önekli birleştirilir: "ab"+"c" ile "a"+"bc" farklı sonuç verir.
        /// </summary>
        public string Compute(string previousHmac, params string[] fields)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                WriteField(w, previousHmac);
                foreach (var f in fields) WriteField(w, f);
                w.Flush();

                using (var hmac = new HMACSHA256(_key))
                    return ToHex(hmac.ComputeHash(ms.ToArray()));
            }
        }

        public bool Verify(string expectedHmac, string previousHmac, params string[] fields)
        {
            return string.Equals(expectedHmac, Compute(previousHmac, fields), StringComparison.Ordinal);
        }

        private static void WriteField(BinaryWriter w, string value)
        {
            if (value == null)
            {
                w.Write(-1);
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            w.Write(bytes.Length);
            w.Write(bytes);
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
