using System.Collections.Generic;

namespace SecurityGuardService.Core.Data
{
    /// <summary>
    /// Şema sürümleri. Yeni tablo / kolon gerektiğinde listenin SONUNA yeni bir sürüm eklenir.
    /// Yayınlanmış bir sürümün SQL'i asla değiştirilmez (müşteride zaten çalışmış olur).
    /// Zamanlar: UTC ISO 8601 metin (DbTime). Mantıksal alanlar: INTEGER 0/1.
    /// </summary>
    internal static class Migrations
    {
        public sealed class Migration
        {
            public readonly int Version;
            public readonly string Description;
            public readonly string Sql;

            public Migration(int version, string description, string sql)
            {
                Version = version;
                Description = description;
                Sql = sql;
            }
        }

        public static readonly IReadOnlyList<Migration> All = new[]
        {
            new Migration(1, "İlk şema", @"
CREATE TABLE Settings (
    Key          TEXT    NOT NULL PRIMARY KEY,
    Value        TEXT,
    IsEncrypted  INTEGER NOT NULL DEFAULT 0,
    UpdatedUtc   TEXT    NOT NULL
);

CREATE TABLE AdminUser (
    Id              INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    UserName        TEXT    NOT NULL,
    PasswordHash    TEXT    NOT NULL,
    PasswordSalt    TEXT    NOT NULL,
    Iterations      INTEGER NOT NULL,
    FailedCount     INTEGER NOT NULL DEFAULT 0,
    LockedUntilUtc  TEXT,
    CreatedUtc      TEXT    NOT NULL,
    LastLoginUtc    TEXT
);

CREATE TABLE SqlInstances (
    Id              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    InstanceName    TEXT    NOT NULL UNIQUE,
    ProductVersion  TEXT,
    Edition         TEXT,
    Port            INTEGER,
    ConnectionEnc   TEXT,
    IsEnabled       INTEGER NOT NULL DEFAULT 1,
    DetectedUtc     TEXT    NOT NULL,
    LastSeenUtc     TEXT
);

CREATE TABLE EventRules (
    EventType    TEXT    NOT NULL PRIMARY KEY,
    Monitor      INTEGER NOT NULL DEFAULT 1,
    WriteLog     INTEGER NOT NULL DEFAULT 1,
    SendMail     INTEGER NOT NULL DEFAULT 0,
    Severity     INTEGER NOT NULL DEFAULT 0,
    BanEnabled   INTEGER NOT NULL DEFAULT 0,
    Threshold    INTEGER,
    UpdatedUtc   TEXT    NOT NULL
);

CREATE TABLE Recipients (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Email        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    DisplayName  TEXT,
    IsEnabled    INTEGER NOT NULL DEFAULT 1,
    CreatedUtc   TEXT    NOT NULL
);

CREATE TABLE EventLog (
    Id          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    TimeUtc     TEXT    NOT NULL,
    Source      TEXT    NOT NULL,
    EventType   TEXT    NOT NULL,
    Severity    INTEGER NOT NULL,
    UserName    TEXT,
    IpAddress   TEXT,
    Summary     TEXT    NOT NULL,
    Detail      TEXT,
    MailSent    INTEGER NOT NULL DEFAULT 0,
    PrevHmac    TEXT,
    Hmac        TEXT
);
CREATE INDEX IX_EventLog_TimeUtc   ON EventLog (TimeUtc);
CREATE INDEX IX_EventLog_EventType ON EventLog (EventType, TimeUtc);
CREATE INDEX IX_EventLog_IpAddress ON EventLog (IpAddress, TimeUtc);

CREATE TABLE EventBookmarks (
    Channel     TEXT NOT NULL PRIMARY KEY,
    Bookmark    TEXT NOT NULL,
    UpdatedUtc  TEXT NOT NULL
);

CREATE TABLE LoginAttempts (
    IpAddress     TEXT    NOT NULL,
    Protocol      TEXT    NOT NULL,
    FailCount     INTEGER NOT NULL DEFAULT 0,
    SuccessCount  INTEGER NOT NULL DEFAULT 0,
    LastUserName  TEXT,
    FirstUtc      TEXT    NOT NULL,
    LastUtc       TEXT    NOT NULL,
    PRIMARY KEY (IpAddress, Protocol)
);

CREATE TABLE BannedIps (
    IpAddress       TEXT    NOT NULL PRIMARY KEY,
    Protocol        TEXT,
    Reason          TEXT    NOT NULL,
    RuleName        TEXT,
    FailCount       INTEGER,
    IsManual        INTEGER NOT NULL DEFAULT 0,
    FirewallRuleNo  INTEGER,
    BannedUtc       TEXT    NOT NULL
);

CREATE TABLE Whitelist (
    Id          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Cidr        TEXT    NOT NULL UNIQUE,
    Note        TEXT,
    IsSystem    INTEGER NOT NULL DEFAULT 0,
    CreatedUtc  TEXT    NOT NULL
);

CREATE TABLE Baselines (
    Scope        TEXT NOT NULL,
    Key          TEXT NOT NULL,
    Snapshot     TEXT NOT NULL,
    CapturedUtc  TEXT NOT NULL,
    PRIMARY KEY (Scope, Key)
);

CREATE TABLE CheckResults (
    Id          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    RunId       TEXT    NOT NULL,
    CheckId     TEXT    NOT NULL,
    Category    TEXT    NOT NULL,
    Status      INTEGER NOT NULL,
    Title       TEXT    NOT NULL,
    Detail      TEXT,
    HowToFix    TEXT,
    CreatedUtc  TEXT    NOT NULL
);
CREATE INDEX IX_CheckResults_RunId ON CheckResults (RunId);

CREATE TABLE CheckIgnores (
    CheckId     TEXT NOT NULL PRIMARY KEY,
    Note        TEXT,
    CreatedUtc  TEXT NOT NULL
);

CREATE TABLE License (
    Id               INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    DocumentEnc      TEXT,
    Status           INTEGER NOT NULL DEFAULT 0,
    ExpiresUtc       TEXT,
    LastValidateUtc  TEXT,
    LastServerUtc    TEXT
);

CREATE TABLE MailQueue (
    Id              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CreatedUtc      TEXT    NOT NULL,
    Subject         TEXT    NOT NULL,
    BodyHtml        TEXT    NOT NULL,
    Recipients      TEXT,
    Priority        INTEGER NOT NULL DEFAULT 0,
    Status          INTEGER NOT NULL DEFAULT 0,
    Attempts        INTEGER NOT NULL DEFAULT 0,
    NextAttemptUtc  TEXT    NOT NULL,
    LastError       TEXT,
    SentUtc         TEXT
);
CREATE INDEX IX_MailQueue_Pending ON MailQueue (Status, NextAttemptUtc);

CREATE TABLE AuditTrail (
    Id          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    TimeUtc     TEXT    NOT NULL,
    UserName    TEXT,
    Action      TEXT    NOT NULL,
    Detail      TEXT
);
CREATE INDEX IX_AuditTrail_TimeUtc ON AuditTrail (TimeUtc);
"),

            new Migration(2, "Mail imza resimleri", @"
CREATE TABLE MailImages (
    ContentId   TEXT    NOT NULL PRIMARY KEY,
    FileName    TEXT    NOT NULL,
    MimeType    TEXT    NOT NULL,
    Data        BLOB    NOT NULL,
    CreatedUtc  TEXT    NOT NULL
);
"),

            new Migration(3, "Olay bazında mail alıcıları", @"
CREATE TABLE RuleRecipients (
    EventType    TEXT    NOT NULL,
    RecipientId  INTEGER NOT NULL REFERENCES Recipients (Id) ON DELETE CASCADE,
    PRIMARY KEY (EventType, RecipientId)
);
"),

            new Migration(4, "Son başarılı girişten beri başarısız deneme sayacı", @"
ALTER TABLE LoginAttempts ADD COLUMN FailsSinceSuccess INTEGER NOT NULL DEFAULT 0;
"),

            new Migration(5, "Tarama çalıştırmaları ve madde ayrıntıları", @"
CREATE TABLE CheckRuns (
    RunId        TEXT    NOT NULL PRIMARY KEY,
    Scope        TEXT    NOT NULL,
    TriggerText  TEXT,
    StartedUtc   TEXT    NOT NULL,
    FinishedUtc  TEXT    NOT NULL,
    Passed       INTEGER NOT NULL DEFAULT 0,
    Info         INTEGER NOT NULL DEFAULT 0,
    Warning      INTEGER NOT NULL DEFAULT 0,
    Critical     INTEGER NOT NULL DEFAULT 0,
    Errors       INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IX_CheckRuns_Scope ON CheckRuns (Scope, StartedUtc);
ALTER TABLE CheckResults ADD COLUMN Summary TEXT;
ALTER TABLE CheckResults ADD COLUMN Script TEXT;
ALTER TABLE CheckResults ADD COLUMN ScriptLanguage TEXT;
ALTER TABLE CheckResults ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;
"),

            new Migration(6, "SQL bakımı geçmişi", @"
CREATE TABLE MaintenanceRuns (
    RunId        TEXT    NOT NULL PRIMARY KEY,
    Instance     TEXT    NOT NULL,
    TriggerText  TEXT,
    DryRun       INTEGER NOT NULL,
    StartedUtc   TEXT    NOT NULL,
    FinishedUtc  TEXT,
    Status       TEXT    NOT NULL,
    Summary      TEXT
);
CREATE INDEX IX_MaintenanceRuns_Started ON MaintenanceRuns (StartedUtc);
CREATE TABLE MaintenanceOps (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    RunId         TEXT    NOT NULL,
    TimeUtc       TEXT    NOT NULL,
    DatabaseName  TEXT,
    ObjectName    TEXT,
    Operation     TEXT    NOT NULL,
    BeforeText    TEXT,
    AfterText     TEXT,
    DurationMs    INTEGER,
    Result        TEXT    NOT NULL,
    Error         TEXT
);
CREATE INDEX IX_MaintenanceOps_RunId ON MaintenanceOps (RunId);
"),

            new Migration(7, "Olay logunda yöneticinin yaptığı silme / değiştirmeler", @"
CREATE TABLE EventLogAdminChanges (
    EventLogId  INTEGER NOT NULL,
    ChangeType  TEXT    NOT NULL,
    TimeUtc     TEXT    NOT NULL,
    UserName    TEXT,
    Hmac        TEXT,
    PRIMARY KEY (EventLogId, ChangeType)
);
"),

            new Migration(8, "Günlük raporlar", @"
CREATE TABLE Reports (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CreatedUtc    TEXT    NOT NULL,
    PeriodFromUtc TEXT    NOT NULL,
    PeriodToUtc   TEXT    NOT NULL,
    Subject       TEXT    NOT NULL,
    TriggerText   TEXT,
    Mailed        INTEGER NOT NULL DEFAULT 0,
    MailNote      TEXT,
    Html          TEXT    NOT NULL
);
CREATE INDEX IX_Reports_Created ON Reports (CreatedUtc);
"),

            new Migration(9, "SQL yedekleme geçmişi", @"
CREATE TABLE BackupRuns (
    RunId        TEXT    NOT NULL PRIMARY KEY,
    Instance     TEXT    NOT NULL,
    TriggerText  TEXT,
    StartedUtc   TEXT    NOT NULL,
    FinishedUtc  TEXT,
    Status       TEXT    NOT NULL,
    Summary      TEXT
);
CREATE INDEX IX_BackupRuns_Started ON BackupRuns (StartedUtc);
CREATE TABLE BackupFiles (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    RunId         TEXT    NOT NULL,
    TimeUtc       TEXT    NOT NULL,
    DatabaseName  TEXT,
    Step          TEXT    NOT NULL,
    ArchivePath   TEXT,
    SizeMb        REAL,
    DurationMs    INTEGER,
    Result        TEXT    NOT NULL,
    Note          TEXT
);
CREATE INDEX IX_BackupFiles_RunId ON BackupFiles (RunId);
"),

            new Migration(10, "İşlemci / bellek geçmişi (dakikalık)", @"
CREATE TABLE ResourceSamples (
    TimeUtc  TEXT NOT NULL PRIMARY KEY,
    Cpu      REAL NOT NULL,
    Ram      REAL NOT NULL
);
"),

            new Migration(11, "Port IP kısıtları", @"
CREATE TABLE PortRestrictions (
    Id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Name             TEXT    NOT NULL,
    Protocol         TEXT    NOT NULL,
    Ports            TEXT    NOT NULL,
    Addresses        TEXT,
    AllowLan         INTEGER NOT NULL DEFAULT 1,
    Enabled          INTEGER NOT NULL DEFAULT 0,
    Note             TEXT,
    UpdatedUtc       TEXT    NOT NULL,
    UpdatedBy        TEXT,
    PendingUntilUtc  TEXT,
    PreviousJson     TEXT
);
"),

            new Migration(12, "Tuzak dosyalar", @"
CREATE TABLE CanaryFolders (
    Id          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Path        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    Source      TEXT    NOT NULL,
    SourceName  TEXT,
    Enabled     INTEGER NOT NULL DEFAULT 1,
    CreatedUtc  TEXT    NOT NULL
);
CREATE TABLE CanaryFiles (
    Id          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    FolderId    INTEGER NOT NULL,
    Path        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    Sha256      TEXT    NOT NULL,
    CreatedUtc  TEXT    NOT NULL
);
"),

            new Migration(13, "Port kısıtında ülke seçimi", @"
ALTER TABLE PortRestrictions ADD COLUMN Countries TEXT;
"),

            new Migration(14, "RDP oturum geçmişi", @"
CREATE TABLE RdpSessions (
    Id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    SessionId      INTEGER NOT NULL,
    UserName       TEXT    NOT NULL,
    ClientIp       TEXT,
    Ips            TEXT,
    Country        TEXT,
    StartUtc       TEXT    NOT NULL,
    EndUtc         TEXT,
    LastChangeUtc  TEXT    NOT NULL,
    State          TEXT    NOT NULL,
    Disconnects    INTEGER NOT NULL DEFAULT 0,
    Reconnects     INTEGER NOT NULL DEFAULT 0,
    OffHours       INTEGER NOT NULL DEFAULT 0,
    StartUnknown   INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IX_RdpSessions_Start ON RdpSessions (StartUtc);
CREATE INDEX IX_RdpSessions_Open ON RdpSessions (State, SessionId);
"),

            new Migration(15, "Servis takibi", @"
CREATE TABLE WatchedServices (
    Name         TEXT    NOT NULL PRIMARY KEY COLLATE NOCASE,
    DisplayName  TEXT,
    AutoRestart  INTEGER NOT NULL DEFAULT 0,
    UpdatedUtc   TEXT    NOT NULL,
    UpdatedBy    TEXT
);
"),

            new Migration(16, "Rapor türü (günlük / aylık)", @"
ALTER TABLE Reports ADD COLUMN Kind TEXT NOT NULL DEFAULT 'daily';
CREATE INDEX IX_Reports_Kind ON Reports (Kind, Id);
"),

            new Migration(17, "Lisans doğrulama durumu", @"
ALTER TABLE License ADD COLUMN LastValidUtc TEXT;
ALTER TABLE License ADD COLUMN LastAttemptUtc TEXT;
ALTER TABLE License ADD COLUMN MaxSeenUtc TEXT;
ALTER TABLE License ADD COLUMN LastError TEXT;
ALTER TABLE License ADD COLUMN Notices TEXT;
ALTER TABLE License ADD COLUMN Active INTEGER NOT NULL DEFAULT 0;
ALTER TABLE License ADD COLUMN EverActive INTEGER NOT NULL DEFAULT 0;
"),
        };
    }
}
