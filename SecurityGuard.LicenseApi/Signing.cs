using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecurityGuard.Shared.Licensing;

namespace SecurityGuard.LicenseApi;

/// <summary>
/// Lisans cevaplarını imzalayan RSA anahtarı. Özel anahtar diskte DPAPI (makine kapsamı + ek entropi) ile şifreli durur;
/// sadece bu sunucuda çözülebilir. Açık anahtar SecurityGuard'ın içine gömülüdür (LicenseProtocol.PublicModulus).
/// </summary>
public sealed class Signing : IDisposable
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SecurityGuard.LicenseApi.SigningKey.v1");
    private readonly RSA _rsa;

    public Signing(string keyFile)
    {
        if (!File.Exists(keyFile))
            throw new InvalidOperationException("İmza anahtarı bulunamadı: " + keyFile + ". Önce: SecurityGuard.LicenseApi --generate-key");
        var stored = JsonSerializer.Deserialize<StoredKey>(File.ReadAllText(keyFile)) ?? throw new InvalidOperationException("Anahtar dosyası okunamadı.");
        byte[] pkcs8 = ProtectedData.Unprotect(Convert.FromBase64String(stored.Protected), Entropy, DataProtectionScope.LocalMachine);
        _rsa = RSA.Create();
        _rsa.ImportPkcs8PrivateKey(pkcs8, out _);
        CryptographicOperations.ZeroMemory(pkcs8);
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

    /// <summary>Yeni RSA 3072 anahtarı üretir, şifreli yazar; açık anahtarı döner. Var olan dosyanın üzerine yazmaz.</summary>
    public static (string Modulus, string Exponent) Generate(string keyFile)
    {
        if (File.Exists(keyFile)) throw new InvalidOperationException("Anahtar zaten var: " + keyFile + " (yenisi tüm istemcilerin güncellenmesini gerektirir; bilerek silin).");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyFile))!);
        using var rsa = RSA.Create(3072);
        byte[] pkcs8 = rsa.ExportPkcs8PrivateKey();
        var stored = new StoredKey { Protected = Convert.ToBase64String(ProtectedData.Protect(pkcs8, Entropy, DataProtectionScope.LocalMachine)), CreatedUtc = DateTime.UtcNow };
        CryptographicOperations.ZeroMemory(pkcs8);
        File.WriteAllText(keyFile, JsonSerializer.Serialize(stored));
        var p = rsa.ExportParameters(false);
        return (Convert.ToBase64String(p.Modulus!), Convert.ToBase64String(p.Exponent!));
    }

    public void Dispose() => _rsa.Dispose();

    private sealed class StoredKey
    {
        public string Protected { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
    }
}
