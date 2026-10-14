using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SecurityGuard.LicenseApi;

/// <summary>Tüm veritabanı erişimi saklı prosedürlerle (tek gidiş-dönüş, plan önbelleği, API rolü sadece EXECUTE).</summary>
public sealed class LicenseRepository(IConfiguration config)
{
    private readonly string _connection = config.GetConnectionString("License") ?? throw new InvalidOperationException("ConnectionStrings:License tanımlı değil.");

    private SqlConnection Open() => new(_connection);

    public async Task<ValidateRow> ValidateAsync(string hwId, string? ip, string? version, string? machine)
    {
        await using var c = Open();
        return await c.QuerySingleAsync<ValidateRow>("dbo.usp_License_Validate",
            new { HwId = hwId, Ip = Trim(ip, 45), Version = Trim(version, 32), MachineName = Trim(machine, 64) },
            commandType: CommandType.StoredProcedure, commandTimeout: 15);
    }

    public async Task<IEnumerable<LicenseRow>> ListAsync(string? search, int top)
    {
        await using var c = Open();
        return await c.QueryAsync<LicenseRow>("dbo.usp_License_List", new { Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), Top = top }, commandType: CommandType.StoredProcedure);
    }

    public async Task<LicenseRow?> GetAsync(int id)
    {
        await using var c = Open();
        return await c.QuerySingleOrDefaultAsync<LicenseRow>("dbo.usp_License_Get", new { LicenseId = id }, commandType: CommandType.StoredProcedure);
    }

    public async Task<LicenseRow> CreateAsync(string hwId, string customer, DateTime expiresUtc, string? note, string? ip)
    {
        await using var c = Open();
        return await c.QuerySingleAsync<LicenseRow>("dbo.usp_License_Create",
            new { HwId = hwId, CustomerName = customer, ExpiresUtc = expiresUtc, Note = note, Ip = Trim(ip, 45) }, commandType: CommandType.StoredProcedure);
    }

    public async Task<LicenseRow> UpdateAsync(int id, string? customer, DateTime? expiresUtc, string? note, string? ip)
    {
        await using var c = Open();
        return await c.QuerySingleAsync<LicenseRow>("dbo.usp_License_Update",
            new { LicenseId = id, CustomerName = customer, ExpiresUtc = expiresUtc, Note = note, Ip = Trim(ip, 45) }, commandType: CommandType.StoredProcedure);
    }

    public async Task<LicenseRow> SetStatusAsync(int id, byte status, string? ip)
    {
        await using var c = Open();
        return await c.QuerySingleAsync<LicenseRow>("dbo.usp_License_SetStatus", new { LicenseId = id, Status = status, Ip = Trim(ip, 45) }, commandType: CommandType.StoredProcedure);
    }

    public async Task<IEnumerable<CheckRow>> ChecksAsync(int? licenseId, string? hwId, int top)
    {
        await using var c = Open();
        return await c.QueryAsync<CheckRow>("dbo.usp_License_Checks", new { LicenseId = licenseId, HwId = hwId, Top = top }, commandType: CommandType.StoredProcedure);
    }

    public async Task<IEnumerable<UnknownMachineRow>> UnknownMachinesAsync(int days)
    {
        await using var c = Open();
        return await c.QueryAsync<UnknownMachineRow>("dbo.usp_Unknown_Machines", new { Days = days }, commandType: CommandType.StoredProcedure);
    }

    public async Task<long> PurgeAsync(int keepDays)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<long>("dbo.usp_Checks_Purge", new { KeepDays = keepDays }, commandType: CommandType.StoredProcedure, commandTimeout: 600);
    }

    // ---- Merkezi panel ----

    /// <summary>Ajan heartbeat: durumu kaydeder. Dönen LicenseId=0 ise makinenin aktif lisansı yok (reddedildi).</summary>
    public async Task<int> HeartbeatAsync(HeartbeatRequest r)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<int>("dbo.usp_Agent_Heartbeat", new
        {
            r.HwId,
            AgentVersion = Trim(r.AgentVersion, 32),
            MachineName = Trim(r.MachineName, 64),
            r.ServiceReady, r.LicenseValid, r.ProtectionOk, r.SqlMonitoring,
            r.BannedIps, r.NewBans24h, r.FailedLogins24h, r.CriticalEvents24h, r.MemoryMb, r.PendingMail,
            AvailableVersion = Trim(r.AvailableVersion, 32), r.UpdateReady,
            r.Payload
        }, commandType: CommandType.StoredProcedure, commandTimeout: 15);
    }

    public async Task<IEnumerable<PanelAgentRow>> PanelAgentsAsync(string? search, int top)
    {
        await using var c = Open();
        return await c.QueryAsync<PanelAgentRow>("dbo.usp_Panel_Agents", new { Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), Top = top }, commandType: CommandType.StoredProcedure);
    }

    public async Task<PanelAgentDetailRow?> PanelAgentDetailAsync(int licenseId)
    {
        await using var c = Open();
        return await c.QuerySingleOrDefaultAsync<PanelAgentDetailRow>("dbo.usp_Panel_AgentDetail", new { LicenseId = licenseId }, commandType: CommandType.StoredProcedure);
    }

    private static string? Trim(string? s, int max) => s == null ? null : s.Length <= max ? s : s[..max];
}

public sealed class ValidateRow
{
    public byte Result { get; set; }
    public int? LicenseId { get; set; }
    public string? CustomerName { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public byte Matched { get; set; }
    public DateTime ServerUtc { get; set; }
}

public sealed class LicenseRow
{
    public int LicenseId { get; set; }
    public string HwId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public byte Status { get; set; }
    public string StatusText => Status == 1 ? (ExpiresUtc < DateTime.UtcNow ? "süresi doldu" : "aktif") : "iptal";
    public string? Note { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string? LastIp { get; set; }
    public string? LastVersion { get; set; }
    public string? LastMachine { get; set; }
}

public sealed class CheckRow
{
    public long CheckId { get; set; }
    public DateTime TimeUtc { get; set; }
    public int? LicenseId { get; set; }
    public string HwId { get; set; } = "";
    public byte Result { get; set; }
    public byte Matched { get; set; }
    public string? Ip { get; set; }
    public string? Version { get; set; }
    public string? MachineName { get; set; }
}

public sealed class UnknownMachineRow
{
    public string HwId { get; set; } = "";
    public string? MachineName { get; set; }
    public string? LastIp { get; set; }
    public string? Version { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public long Checks { get; set; }
}

/// <summary>Yönetici: yeni lisans.</summary>
public sealed class CreateLicenseRequest
{
    /// <summary>SecurityGuard → Lisans sayfasındaki donanım kimliği (SG1-...).</summary>
    public string HwId { get; set; } = "";
    public string Customer { get; set; } = "";
    /// <summary>Bitiş (UTC). Örnek: 2027-10-09T00:00:00Z</summary>
    public DateTime ExpiresUtc { get; set; }
    public string? Note { get; set; }
}

/// <summary>Yönetici: lisans güncelle (boş alanlar değişmez).</summary>
public sealed class UpdateLicenseRequest
{
    public string? Customer { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public string? Note { get; set; }
}

// ---- Merkezi panel ----

/// <summary>Ajan → hub heartbeat (müşteri adı GÖNDERİLMEZ; lisanstan okunur). HwId + aktif lisans = kimlik.</summary>
public sealed class HeartbeatRequest
{
    public string HwId { get; set; } = "";
    public string? AgentVersion { get; set; }
    public string? MachineName { get; set; }
    public bool ServiceReady { get; set; }
    public bool LicenseValid { get; set; }
    public bool ProtectionOk { get; set; }
    public bool SqlMonitoring { get; set; }
    public int BannedIps { get; set; }
    public int NewBans24h { get; set; }
    public int FailedLogins24h { get; set; }
    public int CriticalEvents24h { get; set; }
    public int MemoryMb { get; set; }
    public int PendingMail { get; set; }
    public string? AvailableVersion { get; set; }
    public bool UpdateReady { get; set; }
    /// <summary>Detay sayfası için tam JSON anlık görüntü (opsiyonel).</summary>
    public string? Payload { get; set; }
}

/// <summary>Panel ızgara satırı: lisans + güncel ajan durumu (LastHeartbeatUtc null = hiç bağlanmadı / eski sürüm).</summary>
public sealed class PanelAgentRow
{
    public int LicenseId { get; set; }
    public string HwId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public byte LicenseStatus { get; set; }
    public string? MachineName { get; set; }
    public string? AgentVersion { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }
    public bool? ServiceReady { get; set; }
    public bool? LicenseValid { get; set; }
    public bool? ProtectionOk { get; set; }
    public bool? SqlMonitoring { get; set; }
    public int? BannedIps { get; set; }
    public int? NewBans24h { get; set; }
    public int? FailedLogins24h { get; set; }
    public int? CriticalEvents24h { get; set; }
    public int? MemoryMb { get; set; }
    public int? PendingMail { get; set; }
    public string? AvailableVersion { get; set; }
    public bool? UpdateReady { get; set; }
}

/// <summary>Panel detay: lisans + güncel ajan durumu + tam JSON anlık görüntü.</summary>
public sealed class PanelAgentDetailRow
{
    public int LicenseId { get; set; }
    public string HwId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public byte LicenseStatus { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string? LastIp { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }
    public string? AgentVersion { get; set; }
    public string? MachineName { get; set; }
    public bool? ServiceReady { get; set; }
    public bool? LicenseValid { get; set; }
    public bool? ProtectionOk { get; set; }
    public bool? SqlMonitoring { get; set; }
    public int? BannedIps { get; set; }
    public int? NewBans24h { get; set; }
    public int? FailedLogins24h { get; set; }
    public int? CriticalEvents24h { get; set; }
    public int? MemoryMb { get; set; }
    public int? PendingMail { get; set; }
    public string? AvailableVersion { get; set; }
    public bool? UpdateReady { get; set; }
    public string? Payload { get; set; }
}
