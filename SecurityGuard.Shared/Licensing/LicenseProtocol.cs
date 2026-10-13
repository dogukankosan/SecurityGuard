
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SecurityGuard.Shared.Licensing
{
    // Bu dosya hem SecurityGuard servisinde (.NET Framework 4.8) hem Lisans API'sinde (.NET 10, bağlantılı dosya) derlenir:
    // iki taraf aynı istek / cevap / imzalanan metin biçimini kullanır. C# 7.3 ile uyumlu kalmalı.

    /// <summary>Servis → API: "bu makinenin lisansı geçerli mi?"</summary>
    public sealed class LicenseValidateRequest
    {
        /// <summary>SG1-XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX (4 donanım parçasının özeti).</summary>
        public string HwId { get; set; }

        /// <summary>Rastgele tek kullanımlık değer (base64, 16 bayt): cevap bu değeri imzalı geri verir, eski cevap tekrar oynatılamaz.</summary>
        public string Nonce { get; set; }

        public string ClientTimeUtc { get; set; }
        public string Version { get; set; }
        public string MachineName { get; set; }
    }

    /// <summary>API → servis: RSA (SHA-256, PKCS#1) imzalı cevap. İmza <see cref="LicenseProtocol.Canonical"/> metni üzerindedir.</summary>
    public sealed class LicenseValidateResponse
    {
        /// <summary><see cref="LicenseProtocol.StatusValid"/> vb.</summary>
        public string Status { get; set; }

        public int LicenseId { get; set; }
        public string Customer { get; set; }

        /// <summary>yyyy-MM-ddTHH:mm:ssZ; lisans yoksa boş.</summary>
        public string ExpiresUtc { get; set; }

        public string ServerTimeUtc { get; set; }
        public string Nonce { get; set; }
        public string HwId { get; set; }

        /// <summary>Eşleşen HWID parçası: 4 tam, 3 donanım değişmiş (yine geçerli).</summary>
        public int Matched { get; set; }

        public string Signature { get; set; }
    }

    public static class LicenseProtocol
    {
        public const string Version = "SGLIC1";
        public const string ValidatePath = "api/v1/license/validate";

        public const string StatusValid = "valid";
        public const string StatusExpired = "expired";
        public const string StatusRevoked = "revoked";
        public const string StatusUnknown = "unknown";

        public const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        private static readonly Regex HwIdPattern = new Regex(@"^SG1-[0-9A-F]{8}-[0-9A-F]{8}-[0-9A-F]{8}-[0-9A-F]{8}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Lisans sunucusunun imza anahtarının açık kısmı (RSA 3072). Özel anahtar sadece API sunucusunda, DPAPI ile şifreli durur.
        /// Değiştirilirse eski SecurityGuard sürümleri yeni sunucuyu tanımaz (bilerek: anahtar sızarsa yeni sürümle döndürülür).
        /// </summary>
        public const string PublicModulus = "hhekSTeq6Ek52nTUhai7eE1UK4vX57Vqwit7SD0CIWkcp1V1+S3Zx51gpMBSvK/RMhpli6cKfzDwIZlEdpzSkXzblWZAlxBaPuxO2tJnIFHOq5xJ8/cgQd329jYjuVlqBK1Ll37MuiaaCfjWk1NtQC9wZTEGq+gAALm56UilMUk0cAxNeUt33SYeBgi7aH7x1GJ2vOUfBEeTPVCd4P5c60DCSNTAepKlgeDQ9lTXnsPpK8mhIMuaivotyYNGZDnx4zs0fVEn327TcvGYFtzqlkFTVg5i6Smyl8eUC/EHj19a/Ytb1TadUBAhny4U3+371249Fc5/w9D/sxWK+O0zerLW4Tqh6fJQGrxSwIQQDemhsy9t7wCSAxJNdrscK9hy3jqASFwPLOsIKrGcEWe0tgvEp8miq+xg2yuSfqLqpWLncvOC/ZvdwHCWJFAze/r6LURskrj57fRAiXDqLiZH6P0F5dlWe8B7Tw2zgjOFuNt2p5HvCZYGT3vhZHR6+ObV";
        public const string PublicExponent = "AQAB";

        public static bool IsValidHwId(string hwId)
        {
            return hwId != null && HwIdPattern.IsMatch(hwId);
        }

        public static string FormatTime(DateTime utc)
        {
            return utc.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);
        }

        public static DateTime ParseTime(string text)
        {
            return DateTime.ParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        /// <summary>İmzalanan metin: alanlar sabit sırada, '|' ile (alanlarda '|' olmaz; müşteri adında varsa '/' yapılır).</summary>
        public static string Canonical(LicenseValidateResponse r)
        {
            return string.Join("|", new[]
            {
                Version, r.Status ?? "", r.LicenseId.ToString(CultureInfo.InvariantCulture), Clean(r.Customer), r.ExpiresUtc ?? "",
                r.ServerTimeUtc ?? "", r.Nonce ?? "", r.HwId ?? "", r.Matched.ToString(CultureInfo.InvariantCulture)
            });
        }

        private static string Clean(string s)
        {
            return (s ?? "").Replace('|', '/');
        }

        /// <summary>İmzayı gömülü açık anahtarla doğrular.</summary>
        public static bool Verify(LicenseValidateResponse r)
        {
            return Verify(r, PublicModulus, PublicExponent);
        }

        public static bool Verify(LicenseValidateResponse r, string modulusBase64, string exponentBase64)
        {
            if (r == null || string.IsNullOrEmpty(r.Signature)) return false;
            try
            {
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(modulusBase64), Exponent = Convert.FromBase64String(exponentBase64) });
                    return rsa.VerifyData(Encoding.UTF8.GetBytes(Canonical(r)), Convert.FromBase64String(r.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }
}
