using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.OpenApi.Models;
using SecurityGuard.LicenseApi;
using SecurityGuard.Shared.Licensing;

// Komut satırı araçları (sunucu kurulumu sırasında bir kez)
if (args.Length > 0) Console.OutputEncoding = Encoding.UTF8;
if (args.Contains("--generate-key"))
{
    string keyFile = KeyFilePath(new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", true).Build());
    var (modulus, exponent) = Signing.Generate(keyFile);
    Console.WriteLine("İmza anahtarı oluşturuldu: " + keyFile);
    Console.WriteLine("Bu değerleri SecurityGuard.Shared\\Licensing\\LicenseProtocol.cs içine yazın:");
    Console.WriteLine("PublicModulus = \"" + modulus + "\"");
    Console.WriteLine("PublicExponent = \"" + exponent + "\"");
    return;
}
if (args.Contains("--show-public-key"))
{
    // Konsol satırları kırpılabildiği için dosyaya da yazılır
    string keyFile = KeyFilePath(new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", true).Build());
    using var key = new Signing(keyFile);
    string text = "PublicModulus = \"" + key.PublicModulus + "\"" + Environment.NewLine + "PublicExponent = \"" + key.PublicExponent + "\"" + Environment.NewLine;
    string outFile = Path.Combine(AppContext.BaseDirectory, "public-key.txt");
    File.WriteAllText(outFile, text);
    Console.Write(text);
    Console.WriteLine("Dosyaya da yazıldı: " + outFile);
    return;
}
if (args.Contains("--generate-admin-key"))
{
    string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    Console.WriteLine("Yönetici anahtarı (bir yere saklayın, tekrar gösterilmez):");
    Console.WriteLine("  " + key);
    Console.WriteLine("appsettings.json → Admin:KeyHashes içine bu özeti ekleyin:");
    Console.WriteLine("  " + AdminKey.Hash(key));
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddWindowsService(o => o.ServiceName = "SecurityGuardLicenseApi");

// Servis (.NET 4.8 DataContractJsonSerializer) alan adlarını büyük/küçük harf duyarlı okur: PascalCase kalsın
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = null);

builder.Services.AddSingleton<LicenseRepository>();
builder.Services.AddSingleton(sp => new Signing(KeyFilePath(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<AdminKey>();
builder.Services.AddSingleton<AdminAccess>();
builder.Services.AddHostedService<PurgeService>();

int validatePerMinute = builder.Configuration.GetValue("RateLimit:ValidatePerMinute", 30);
int adminPerMinute = builder.Configuration.GetValue("RateLimit:AdminPerMinute", 120);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("validate", ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = validatePerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("admin", ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = adminPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Swagger varsayılan SADECE geliştirmede açık (üretimde kapalı: bilgi sızıntısı / saldırı yüzeyi). appsettings ile açıkça açılabilir.
bool swagger = builder.Configuration.GetValue("Swagger:Enabled", builder.Environment.IsDevelopment());
if (swagger)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(o =>
    {
        o.SwaggerDoc("v1", new OpenApiInfo { Title = "SecurityGuard Lisans API", Version = "v1" });
        var scheme = new OpenApiSecurityScheme
        {
            Name = AdminKey.Header, In = ParameterLocation.Header, Type = SecuritySchemeType.ApiKey,
            Description = "Yönetici anahtarı (--generate-admin-key ile üretilir)",
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "AdminKey" }
        };
        o.AddSecurityDefinition("AdminKey", scheme);
        o.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
        string xml = Path.Combine(AppContext.BaseDirectory, "SecurityGuard.LicenseApi.xml");
        if (File.Exists(xml)) o.IncludeXmlComments(xml);
    });
}

var app = builder.Build();

// Anahtar yoksa / gömülü anahtarla uyuşmuyorsa açılışta söyle (istemciler imzayı reddeder)
var signing = app.Services.GetRequiredService<Signing>();
if (!signing.MatchesEmbeddedKey)
    app.Logger.LogWarning("Bu sunucunun imza anahtarı SecurityGuard'a gömülü açık anahtarla aynı değil: istemciler cevapları reddeder.");
var adminKeys = app.Services.GetRequiredService<AdminKey>();
if (adminKeys.Invalid > 0)
    app.Logger.LogError("Admin:KeyHashes içinde {Count} hatalı değer yok sayıldı (64 karakter hex olmalı: --generate-admin-key çıktısındaki özet).", adminKeys.Invalid);
if (adminKeys.Count == 0)
    app.Logger.LogWarning("Admin:KeyHashes boş: yönetici uçları kapalı (--generate-admin-key).");
var adminAccess = app.Services.GetRequiredService<AdminAccess>();
if (adminKeys.Count > 0 && !adminAccess.Restricted)
    app.Logger.LogWarning("Admin:AllowedIps boş: yönetici uçlarına HER IP'den erişilebilir. Üretimde kendi yönetim IP'nizi ekleyin (örn. [\"203.0.113.10\", \"198.51.100.0/24\"]).");
if (adminKeys.Count > 0 && !adminAccess.RequireHttps)
    app.Logger.LogWarning("Admin:RequireHttps kapalı: yönetici anahtarı (X-Admin-Key) HTTP ile düz metin gidebilir. HTTPS kurup Admin:RequireHttps=true yapın.");

app.UseRateLimiter();
if (swagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "SecurityGuard Lisans API");
}

app.MapGet("/api/v1/health", (Signing s) => Results.Ok(new { Status = "ok", ServerTimeUtc = LicenseProtocol.FormatTime(DateTime.UtcNow), KeyMatches = s.MatchesEmbeddedKey }))
    .ExcludeFromDescription();

// ---- İstemci: lisans doğrulama ----
app.MapPost("/" + LicenseProtocol.ValidatePath, async (LicenseValidateRequest? req, HttpContext ctx, LicenseRepository repo, Signing sign) =>
{
    if (req == null || !LicenseProtocol.IsValidHwId(req.HwId) || string.IsNullOrEmpty(req.Nonce) || req.Nonce.Length > 64)
        return Results.BadRequest(new { Error = "Geçersiz istek." });

    var row = await repo.ValidateAsync(req.HwId, ClientIp(ctx), req.Version, req.MachineName);
    var response = new LicenseValidateResponse
    {
        Status = row.Result switch
        {
            1 => LicenseProtocol.StatusValid,
            2 => LicenseProtocol.StatusExpired,
            3 => LicenseProtocol.StatusRevoked,
            _ => LicenseProtocol.StatusUnknown
        },
        LicenseId = row.LicenseId ?? 0,
        Customer = row.CustomerName ?? "",
        ExpiresUtc = row.ExpiresUtc.HasValue ? LicenseProtocol.FormatTime(Utc(row.ExpiresUtc.Value)) : "",
        ServerTimeUtc = LicenseProtocol.FormatTime(Utc(row.ServerUtc)),
        Nonce = req.Nonce,
        HwId = req.HwId,
        Matched = row.Matched
    };
    sign.Sign(response);
    return Results.Ok(response);
})
.RequireRateLimiting("validate")
.WithTags("İstemci")
.WithSummary("SecurityGuard servisinin çağırdığı doğrulama (imzalı cevap).");

// ---- İstemci: merkezi panel heartbeat (ajan outbound, 1-5 dk) ----
// Kimlik = HwId + aktif lisans (ayrı sır yok; validate ile aynı güven modeli). Müşteri adı lisanstan okunur.
// Faz 1 salt-okunur: HwId'yi bilen biri sahte durum gönderebilir (düşük risk). Faz 2'de payload RSA imzalanacak.
app.MapPost("/api/v1/agent/heartbeat", async (HeartbeatRequest? req, LicenseRepository repo) =>
{
    if (req == null || !LicenseProtocol.IsValidHwId(req.HwId)) return Results.BadRequest(new { Error = "Geçersiz istek." });
    if (req.Payload is { Length: > 65536 }) return Results.BadRequest(new { Error = "Payload çok büyük." });
    int licenseId = await repo.HeartbeatAsync(req);
    if (licenseId == 0) return Results.StatusCode(StatusCodes.Status403Forbidden); // makinenin aktif lisansı yok
    return Results.Ok(new { LicenseId = licenseId });
})
.RequireRateLimiting("validate")
.WithTags("İstemci")
.WithSummary("Ajanın periyodik durum bildirimi (merkezi panel).");

// ---- Yönetici ----
var admin = app.MapGroup("/api/v1/admin").AddEndpointFilter<AdminKeyFilter>().RequireRateLimiting("admin").WithTags("Yönetici");

admin.MapGet("/licenses", async (string? search, int? top, LicenseRepository repo) =>
    Results.Ok(await repo.ListAsync(search, Math.Clamp(top ?? 500, 1, 5000))))
    .WithSummary("Lisansları listele (müşteri adı, HWID veya makine adına göre ara).");

admin.MapGet("/licenses/{id:int}", async (int id, LicenseRepository repo) =>
    await repo.GetAsync(id) is { } row ? Results.Ok(row) : Results.NotFound(new { Error = "Lisans bulunamadı." }))
    .WithSummary("Tek lisans.");

admin.MapPost("/licenses", (CreateLicenseRequest req, HttpContext ctx, LicenseRepository repo) => Sql(async () =>
{
    string hwId = (req.HwId ?? "").Trim().ToUpperInvariant();
    if (!LicenseProtocol.IsValidHwId(hwId)) return Results.BadRequest(new { Error = "HWID biçimi hatalı (SG1-XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX)." });
    if (string.IsNullOrWhiteSpace(req.Customer)) return Results.BadRequest(new { Error = "Müşteri adı gerekli." });
    var expires = Utc(req.ExpiresUtc);
    if (expires <= DateTime.UtcNow) return Results.BadRequest(new { Error = "Bitiş tarihi gelecekte olmalı." });
    var row = await repo.CreateAsync(hwId, req.Customer.Trim(), expires, req.Note, ClientIp(ctx));
    return Results.Created("/api/v1/admin/licenses/" + row.LicenseId, row);
}))
.WithSummary("Yeni lisans oluştur (makinede zaten aktif lisans varsa 409).");

admin.MapPut("/licenses/{id:int}", (int id, UpdateLicenseRequest req, HttpContext ctx, LicenseRepository repo) => Sql(async () =>
{
    if (req.Customer != null && string.IsNullOrWhiteSpace(req.Customer)) return Results.BadRequest(new { Error = "Müşteri adı boş olamaz." });
    return Results.Ok(await repo.UpdateAsync(id, req.Customer?.Trim(), req.ExpiresUtc.HasValue ? Utc(req.ExpiresUtc.Value) : null, req.Note, ClientIp(ctx)));
}))
.WithSummary("Müşteri adı / bitiş tarihi / not güncelle (süre uzatma).");

admin.MapPost("/licenses/{id:int}/revoke", (int id, HttpContext ctx, LicenseRepository repo) => Sql(async () =>
    Results.Ok(await repo.SetStatusAsync(id, 2, ClientIp(ctx)))))
    .WithSummary("Lisansı iptal et (istemci en geç 5 dakikada durur).");

admin.MapPost("/licenses/{id:int}/restore", (int id, HttpContext ctx, LicenseRepository repo) => Sql(async () =>
    Results.Ok(await repo.SetStatusAsync(id, 1, ClientIp(ctx)))))
    .WithSummary("İptal edilen lisansı geri aç.");

admin.MapGet("/licenses/{id:int}/checks", async (int id, int? top, LicenseRepository repo) =>
    Results.Ok(await repo.ChecksAsync(id, null, Math.Clamp(top ?? 200, 1, 5000))))
    .WithSummary("Lisansın doğrulama geçmişi.");

admin.MapGet("/unknown-machines", async (int? days, LicenseRepository repo) =>
    Results.Ok(await repo.UnknownMachinesAsync(Math.Clamp(days ?? 7, 1, 365))))
    .WithSummary("Lisansı olmayan ama doğrulama yapan makineler (yeni kurulumların HWID'si buradan alınabilir).");

admin.MapPost("/purge", async (int? keepDays, LicenseRepository repo) =>
    Results.Ok(new { Deleted = await repo.PurgeAsync(Math.Clamp(keepDays ?? 180, 7, 3650)) }))
    .WithSummary("Eski doğrulama kayıtlarını sil.");

// ---- Yönetici: merkezi panel (tüm müşterilerin durumu) ----
admin.MapGet("/panel/agents", async (string? search, int? top, LicenseRepository repo) =>
    Results.Ok(await repo.PanelAgentsAsync(search, Math.Clamp(top ?? 1000, 1, 5000))))
    .WithSummary("Tüm aktif müşteriler + ajan durumları (merkezi panel ızgarası).");

admin.MapGet("/panel/agents/{id:int}", async (int id, LicenseRepository repo) =>
    await repo.PanelAgentDetailAsync(id) is { } row ? Results.Ok(row) : Results.NotFound(new { Error = "Bulunamadı." }))
    .WithSummary("Tek müşteri / makine detayı (merkezi panel).");

app.Run();

static string KeyFilePath(IConfiguration config)
{
    // Varsayılan: ProgramData (yeniden derleme / yayınlama ile silinmesin)
    string path = config["Signing:KeyFile"] is { Length: > 0 } p ? Environment.ExpandEnvironmentVariables(p)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SecurityGuardLicenseApi", "signing.key");
    return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}

static string ClientIp(HttpContext ctx) => (ctx.Connection.RemoteIpAddress ?? IPAddress.None).ToString();

// SQL datetime2 → Dapper DateTime (Kind=Unspecified); veritabanında her şey UTC
static DateTime Utc(DateTime value) => value.Kind switch
{
    DateTimeKind.Utc => value,
    DateTimeKind.Local => value.ToUniversalTime(),
    _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
};

// Saklı prosedürlerin THROW kodları → HTTP
static async Task<IResult> Sql(Func<Task<IResult>> action)
{
    try
    {
        return await action();
    }
    catch (SqlException ex) when (ex.Number == 50001)
    {
        return Results.Conflict(new { Error = ex.Message });
    }
    catch (SqlException ex) when (ex.Number is 50002 or 50003)
    {
        return Results.NotFound(new { Error = ex.Message });
    }
}

/// <summary>X-Admin-Key: anahtarın kendisi değil SHA-256 özeti ayarlarda durur; karşılaştırma sabit sürede.</summary>
public sealed class AdminKey
{
    public const string Header = "X-Admin-Key";
    private readonly byte[][] _hashes;

    public AdminKey(IConfiguration config)
    {
        var entries = (config.GetSection("Admin:KeyHashes").Get<string[]>() ?? [])
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .ToArray();
        // Hatalı özet servisi çökertmesin: atlanır, açılışta uyarı yazılır
        bool valid(string h) => h.Length == 64 && h.All(Uri.IsHexDigit);
        Invalid = entries.Count(h => !valid(h));
        _hashes = entries.Where(valid).Select(Convert.FromHexString).ToArray();
    }

    public int Count => _hashes.Length;

    /// <summary>64 karakterlik hex olmayan (yok sayılan) özet sayısı.</summary>
    public int Invalid { get; }

    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public bool Check(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 200) return false;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        bool ok = false;
        foreach (var h in _hashes) ok |= CryptographicOperations.FixedTimeEquals(hash, h);
        return ok;
    }
}

public sealed class AdminKeyFilter(AdminKey keys, AdminAccess access, ILogger<AdminKeyFilter> log) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var ip = http.Connection.RemoteIpAddress;

        // 1) HTTPS zorunluysa düz HTTP reddedilir (yönetici anahtarı düz metin gitmesin)
        if (access.RequireHttps && !http.Request.IsHttps)
        {
            log.LogWarning("Yönetici ucu HTTPS dışından çağrıldı, reddedildi: {Ip} {Path}", ip, http.Request.Path);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        // 2) IP izin listesi (yapılandırıldıysa): dışarıdakine ucun varlığını bile belli etme
        if (!access.IsAllowed(ip))
        {
            log.LogWarning("İzinli olmayan IP'den yönetici ucu denemesi: {Ip} {Path}", ip, http.Request.Path);
            return Results.NotFound();
        }
        // 3) X-Admin-Key
        if (!keys.Check(http.Request.Headers[AdminKey.Header]))
        {
            log.LogWarning("Hatalı yönetici anahtarı: {Ip} {Path}", ip, http.Request.Path);
            return Results.Unauthorized();
        }
        return await next(ctx);
    }
}

/// <summary>
/// Yönetici uçlarına erişim kısıtı: izinli IP'ler (exact veya CIDR) ve HTTPS zorunluluğu.
/// Admin:AllowedIps boşsa herkese açık (açılışta uyarı). IPv4-mapped IPv6 adresler normalize edilir.
/// </summary>
public sealed class AdminAccess
{
    private readonly (byte[] Net, int Bits)[] _ranges;
    public bool RequireHttps { get; }
    public bool Restricted => _ranges.Length > 0;

    public AdminAccess(IConfiguration config)
    {
        RequireHttps = config.GetValue("Admin:RequireHttps", false);
        var list = new List<(byte[], int)>();
        foreach (var raw in config.GetSection("Admin:AllowedIps").Get<string[]>() ?? [])
        {
            var s = raw?.Trim();
            if (string.IsNullOrEmpty(s)) continue;
            int bits = -1;
            string ipPart = s;
            int slash = s.IndexOf('/');
            if (slash >= 0 && int.TryParse(s[(slash + 1)..], out int b)) { ipPart = s[..slash]; bits = b; }
            if (!IPAddress.TryParse(ipPart, out var ip)) continue;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            var bytes = ip.GetAddressBytes();
            if (bits < 0 || bits > bytes.Length * 8) bits = bytes.Length * 8; // exact
            list.Add((bytes, bits));
        }
        _ranges = list.ToArray();
    }

    public bool IsAllowed(IPAddress? addr)
    {
        if (_ranges.Length == 0) return true;          // yapılandırılmadı: herkese açık (uyarı loglanır)
        if (addr == null) return false;
        if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();
        var ab = addr.GetAddressBytes();
        foreach (var (net, bits) in _ranges)
        {
            if (net.Length != ab.Length) continue;     // IPv4/IPv6 uyuşmazlığı
            if (PrefixMatch(ab, net, bits)) return true;
        }
        return false;
    }

    private static bool PrefixMatch(byte[] a, byte[] b, int bits)
    {
        int full = bits / 8, rem = bits % 8;
        for (int i = 0; i < full; i++) if (a[i] != b[i]) return false;
        if (rem == 0) return true;
        int mask = (0xFF << (8 - rem)) & 0xFF;
        return (a[full] & mask) == (b[full] & mask);
    }
}

/// <summary>Doğrulama kayıtlarını günde bir temizler (5 dk'da bir × makine sayısı hızla büyür).</summary>
public sealed class PurgeService(LicenseRepository repo, IConfiguration config, ILogger<PurgeService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        int keepDays = Math.Clamp(config.GetValue("Purge:KeepDays", 180), 7, 3650);
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stop);
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    long n = await repo.PurgeAsync(keepDays);
                    if (n > 0) log.LogInformation("{Count} eski doğrulama kaydı silindi ({Days} günden eski).", n, keepDays);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Doğrulama kayıtları temizlenemedi.");
                }
                await Task.Delay(TimeSpan.FromHours(24), stop);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
