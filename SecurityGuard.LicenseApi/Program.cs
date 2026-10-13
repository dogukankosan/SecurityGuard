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

bool swagger = builder.Configuration.GetValue("Swagger:Enabled", true);
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

public sealed class AdminKeyFilter(AdminKey keys, ILogger<AdminKeyFilter> log) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (!keys.Check(ctx.HttpContext.Request.Headers[AdminKey.Header]))
        {
            log.LogWarning("Hatalı yönetici anahtarı: {Ip} {Path}", ctx.HttpContext.Connection.RemoteIpAddress, ctx.HttpContext.Request.Path);
            return Results.Unauthorized();
        }
        return await next(ctx);
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
