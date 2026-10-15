using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecurityGuard.Shared.Licensing;

namespace SecurityGuard.LicenseApi;

/// <summary>
/// Lisans cevaplarını imzalayan RSA anahtarı.
/// İki saklama biçimi desteklenir:
///  - "portable-v1": özel anahtar bir PAROLA ile korunur (PBKDF2-SHA256 → AES-256-GCM). Makineden bağımsız;
///    aynı dosya + aynı parola her sunucuda açılır. Yeni/önerilen biçim.
///  - eski DPAPI biçimi (makine kapsamı + entropi): yalnızca üretildiği makinede çözülür. Sadece OKUMA için
///    desteklenir (eski sunucuda --export-portable ile taşınabilir biçime çevirmek üzere).
/// Açık anahtar SecurityGuard'ın içine gömülüdür (LicenseProtocol.PublicModulus) ve her iki biçimde de aynıdır.
/// </summary>
public sealed class Signing : IDisposable
{
    private const string PortableFormat = "portable-v1";
    private const int Pbkdf2Iterations = 200_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;   // AES-GCM standart nonce
    private const int TagSize = 16;     // AES-GCM etiket
    private const int KeySize = 32;     // AES-256

    // Eski DPAPI biçimi için ek entropi (geri uyumluluk)
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SecurityGuard.LicenseApi.SigningKey.v1");

    private readonly RSA _rsa;

    public Signing(string keyFile, string? passphrase)
    {
        if (!File.Exists(keyFile))
            throw new InvalidOperationException("İmza anahtarı bulunamadı: " + keyFile + ". Önce: SecurityGuard.LicenseApi --generate-key");

        var stored = JsonSerializer.Deserialize<StoredKey>(File.ReadAllText(keyFile))
            ?? throw new InvalidOperationException("Anahtar dosyası okunamadı.");

        byte[] pkcs8;
        if (string.Equals(stored.Format, PortableFormat, StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(passphrase))
                throw new InvalidOperationException(
                    "Taşınabilir imza anahtarı için parola gerekli. appsettings → Signing:Passphrase (veya ortam değişkeni SG_SIGNING_PASSPHRASE) ayarlayın.");
            pkcs8 = DecryptPortable(stored, passphrase);
        }
        else if (!string.IsNullOrEmpty(stored.Protected))
        {
            // Eski DPAPI biçimi: yalnızca üretildiği makinede çözülür.
            pkcs8 = ProtectedData.Unprotect(Convert.FromBase64String(stored.Protected), Entropy, DataProtectionScope.LocalMachine);
        }
        else
        {
            throw new InvalidOperationException("Anahtar dosyası biçimi tanınmadı (ne portable-v1 ne de DPAPI).");
        }

        _rsa = RSA.Create();
        try
        {
            _rsa.ImportPkcs8PrivateKey(pkcs8, out _);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        var p = _rsa.ExportParameters(false);
        PublicModulus = Convert.ToBase64String(p.Modulus!);
        PublicExponent = Convert.ToBase64String(p.Exponent!);
    }

    public string PublicModulus { get; }
    public string PublicExponent { get; }

    /// <summary>Gömülü açık anahtar bu sunucunun anahtarıyla aynı mı (değilse istemciler imzayı reddeder).</summary>
    public bool MatchesEmbeddedKey => PublicModulus == LicenseProtocol.PublicModulus && PublicExponent == LicenseProtocol.PublicExponent;

    public void Sign(LicenseValidateResponse response)
    {
        byte[] data = Encoding.UTF8.GetBytes(LicenseProtocol.Canonical(response));
        response.Signature = Convert.ToBase64String(_rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    public void Sign(UpdateManifest manifest)
    {
        byte[] data = Encoding.UTF8.GetBytes(LicenseProtocol.Canonical(manifest));
        manifest.Signature = Convert.ToBase64String(_rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    /// <summary>
    /// Yeni RSA-3072 anahtarı üretir, PAROLA ile korur (taşınabilir), açık anahtarı döner.
    /// Var olan dosyanın üzerine yazmaz. (Yeni anahtar tüm istemcilerin güncellenmesini gerektirir.)
    /// </summary>
    public static (string Modulus, string Exponent) Generate(string keyFile, string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            throw new InvalidOperationException("Taşınabilir anahtar üretmek için parola gerekli (Signing:Passphrase / SG_SIGNING_PASSPHRASE).");
        if (File.Exists(keyFile))
            throw new InvalidOperationException("Anahtar zaten var: " + keyFile + " (yenisi tüm istemcilerin güncellenmesini gerektirir; bilerek silin).");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyFile))!);
        using var rsa = RSA.Create(3072);
        byte[] pkcs8 = rsa.ExportPkcs8PrivateKey();
        try
        {
            var stored = EncryptPortable(pkcs8, passphrase);
            File.WriteAllText(keyFile, JsonSerializer.Serialize(stored));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
        var p = rsa.ExportParameters(false);
        return (Convert.ToBase64String(p.Modulus!), Convert.ToBase64String(p.Exponent!));
    }

    /// <summary>
    /// Var olan anahtarı (DPAPI eski biçim VEYA portable) okuyup PAROLA korumalı taşınabilir biçimde outFile'a yazar.
    /// Özel anahtar değişmez → açık anahtar aynı kalır → ajanları yeniden dağıtmak GEREKMEZ.
    /// DPAPI kaynağı yalnızca üretildiği makinede çözülebildiğinden bu komut ESKİ SUNUCUDA çalıştırılmalıdır.
    /// </summary>
    public static (string Modulus, string Exponent) ExportPortable(string keyFile, string outFile, string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            throw new InvalidOperationException("Taşınabilir anahtar için parola gerekli (Signing:Passphrase / SG_SIGNING_PASSPHRASE).");
        if (!File.Exists(keyFile))
            throw new InvalidOperationException("Kaynak anahtar bulunamadı: " + keyFile);

        var stored = JsonSerializer.Deserialize<StoredKey>(File.ReadAllText(keyFile))
            ?? throw new InvalidOperationException("Anahtar dosyası okunamadı.");

        byte[] pkcs8;
        if (string.Equals(stored.Format, PortableFormat, StringComparison.Ordinal))
            pkcs8 = DecryptPortable(stored, passphrase);               // zaten portable → aynı parolayla doğrula
        else if (!string.IsNullOrEmpty(stored.Protected))
            pkcs8 = ProtectedData.Unprotect(Convert.FromBase64String(stored.Protected), Entropy, DataProtectionScope.LocalMachine);
        else
            throw new InvalidOperationException("Kaynak anahtar biçimi tanınmadı.");

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(pkcs8, out _);
            var portable = EncryptPortable(pkcs8, passphrase);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
            File.WriteAllText(outFile, JsonSerializer.Serialize(portable));
            var p = rsa.ExportParameters(false);
            return (Convert.ToBase64String(p.Modulus!), Convert.ToBase64String(p.Exponent!));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    private static StoredKey EncryptPortable(byte[] pkcs8, string passphrase)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
        byte[] cipher = new byte[pkcs8.Length];
        byte[] tag = new byte[TagSize];
        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Encrypt(nonce, pkcs8, cipher, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return new StoredKey
        {
            Format = PortableFormat,
            Kdf = "PBKDF2-SHA256",
            Iterations = Pbkdf2Iterations,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Cipher = Convert.ToBase64String(cipher),
            CreatedUtc = DateTime.UtcNow,
        };
    }

    private static byte[] DecryptPortable(StoredKey stored, string passphrase)
    {
        if (stored.Iterations <= 0 || stored.Salt is null || stored.Nonce is null || stored.Tag is null || stored.Cipher is null)
            throw new InvalidOperationException("Taşınabilir anahtar dosyası eksik alanlar içeriyor.");

        byte[] salt = Convert.FromBase64String(stored.Salt);
        byte[] nonce = Convert.FromBase64String(stored.Nonce);
        byte[] tag = Convert.FromBase64String(stored.Tag);
        byte[] cipher = Convert.FromBase64String(stored.Cipher);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, stored.Iterations, HashAlgorithmName.SHA256, KeySize);
        byte[] pkcs8 = new byte[cipher.Length];
        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(nonce, cipher, tag, pkcs8);
            return pkcs8;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(pkcs8);
            throw new InvalidOperationException("İmza anahtarı çözülemedi: parola yanlış veya dosya bozuk (Signing:Passphrase).");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public void Dispose() => _rsa.Dispose();

    private sealed class StoredKey
    {
        // Yeni taşınabilir biçim
        public string? Format { get; set; }
        public string? Kdf { get; set; }
        public int Iterations { get; set; }
        public string? Salt { get; set; }
        public string? Nonce { get; set; }
        public string? Tag { get; set; }
        public string? Cipher { get; set; }

        // Eski DPAPI biçimi (geri uyumluluk, salt-okunur)
        public string? Protected { get; set; }

        public DateTime CreatedUtc { get; set; }
    }
}
