/*
    SecurityGuard Lisans veritabanı (SQL Server 2016+; geliştirme: SQL Server 2022 Developer).
    Yeniden çalıştırılabilir: veritabanı / tablo / indeks yoksa oluşturur, prosedürleri günceller. Veri silmez.

    Performans:
      - Tüm erişim saklı prosedürlerle (tek gidiş-dönüş, plan önbelleği, en az yetki: API rolü sadece EXECUTE).
      - Doğrulama (en sık çağrı): HWID'nin 4 parçasına ayrı indeks; tam eşleşme veya 4 parçanın 3'ü (donanım değişimi toleransı).
      - READ_COMMITTED_SNAPSHOT: okumalar yazmaları beklemez (doğrulama kaydı + son görülme güncellemesi aynı anda).
      - Doğrulama logu PAGE sıkıştırmalı, tarih indeksli, eski kayıtlar parça parça silinir (usp_Checks_Purge).
      - Query Store açık (yavaş sorgu takibi), istatistikler asenkron güncellenir.
    Not: Geliştirme için RECOVERY SIMPLE. Canlıda FULL + düzenli log yedeği önerilir (lisans kayıtları iş verisidir).
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- hesaplanan sütun / filtreli indeks için şart (sqlcmd varsayılanı OFF)
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
USE master;
GO

IF DB_ID(N'SecurityGuardLicense') IS NULL
BEGIN
    CREATE DATABASE SecurityGuardLicense;
END
GO

ALTER DATABASE SecurityGuardLicense SET RECOVERY SIMPLE;
ALTER DATABASE SecurityGuardLicense SET PAGE_VERIFY CHECKSUM;
ALTER DATABASE SecurityGuardLicense SET AUTO_CLOSE OFF;
ALTER DATABASE SecurityGuardLicense SET AUTO_SHRINK OFF;
ALTER DATABASE SecurityGuardLicense SET AUTO_CREATE_STATISTICS ON;
ALTER DATABASE SecurityGuardLicense SET AUTO_UPDATE_STATISTICS ON;
ALTER DATABASE SecurityGuardLicense SET AUTO_UPDATE_STATISTICS_ASYNC ON;
ALTER DATABASE SecurityGuardLicense SET ALLOW_SNAPSHOT_ISOLATION ON;
ALTER DATABASE SecurityGuardLicense SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
ALTER DATABASE SecurityGuardLicense SET TARGET_RECOVERY_TIME = 60 SECONDS;
ALTER DATABASE SecurityGuardLicense SET QUERY_STORE = ON;
ALTER DATABASE SecurityGuardLicense SET QUERY_STORE (OPERATION_MODE = READ_WRITE, MAX_STORAGE_SIZE_MB = 256, QUERY_CAPTURE_MODE = AUTO);
GO

-- Dosya büyüme: yüzde değil sabit (küçük büyüme parçalanma, yüzde büyüme dev artışlar yapar)
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER DATABASE SecurityGuardLicense MODIFY FILE (NAME = N''' + name + N''', FILEGROWTH = 64MB);'
FROM sys.master_files WHERE database_id = DB_ID(N'SecurityGuardLicense') AND (is_percent_growth = 1 OR growth < 8192);
EXEC (@sql);
GO

USE SecurityGuardLicense;
GO

-- ============================================================ Tablolar

IF OBJECT_ID(N'dbo.Licenses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Licenses
    (
        LicenseId     int IDENTITY(1, 1) NOT NULL,
        HwId          char(39)       NOT NULL,     -- SG1-XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX
        H1            AS CAST(SUBSTRING(HwId, 5, 8)  AS char(8)) PERSISTED,
        H2            AS CAST(SUBSTRING(HwId, 14, 8) AS char(8)) PERSISTED,
        H3            AS CAST(SUBSTRING(HwId, 23, 8) AS char(8)) PERSISTED,
        H4            AS CAST(SUBSTRING(HwId, 32, 8) AS char(8)) PERSISTED,
        CustomerName  nvarchar(200)  NOT NULL,
        ExpiresUtc    datetime2(0)   NOT NULL,
        Status        tinyint        NOT NULL CONSTRAINT DF_Licenses_Status DEFAULT (1),   -- 1 aktif, 2 iptal
        Note          nvarchar(500)  NULL,
        CreatedUtc    datetime2(0)   NOT NULL CONSTRAINT DF_Licenses_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedUtc    datetime2(0)   NOT NULL CONSTRAINT DF_Licenses_Updated DEFAULT (SYSUTCDATETIME()),
        LastSeenUtc   datetime2(0)   NULL,
        LastIp        varchar(45)    NULL,
        LastVersion   varchar(32)    NULL,
        LastMachine   nvarchar(64)   NULL,
        RowVer        rowversion     NOT NULL,
        CONSTRAINT PK_Licenses PRIMARY KEY CLUSTERED (LicenseId),
        CONSTRAINT CK_Licenses_HwId CHECK (HwId LIKE 'SG1-[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]-[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]-[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]-[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
        CONSTRAINT CK_Licenses_Status CHECK (Status IN (1, 2))
    );
END
GO
-- Aynı makineye tek aktif lisans (iptal edilenler geçmiş olarak kalır)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Licenses_HwId_Active' AND object_id = OBJECT_ID(N'dbo.Licenses'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Licenses_HwId_Active ON dbo.Licenses (HwId) WHERE Status = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Licenses_HwId' AND object_id = OBJECT_ID(N'dbo.Licenses'))
    CREATE NONCLUSTERED INDEX IX_Licenses_HwId ON dbo.Licenses (HwId) INCLUDE (Status, ExpiresUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Licenses_H1' AND object_id = OBJECT_ID(N'dbo.Licenses'))
    CREATE NONCLUSTERED INDEX IX_Licenses_H1 ON dbo.Licenses (H1);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Licenses_H2' AND object_id = OBJECT_ID(N'dbo.Licenses'))
    CREATE NONCLUSTERED INDEX IX_Licenses_H2 ON dbo.Licenses (H2);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Licenses_H3' AND object_id = OBJECT_ID(N'dbo.Licenses'))
    CREATE NONCLUSTERED INDEX IX_Licenses_H3 ON dbo.Licenses (H3);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Licenses_H4' AND object_id = OBJECT_ID(N'dbo.Licenses'))
    CREATE NONCLUSTERED INDEX IX_Licenses_H4 ON dbo.Licenses (H4);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Licenses_Customer' AND object_id = OBJECT_ID(N'dbo.Licenses'))
    CREATE NONCLUSTERED INDEX IX_Licenses_Customer ON dbo.Licenses (CustomerName);
GO

IF OBJECT_ID(N'dbo.LicenseChecks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LicenseChecks
    (
        CheckId      bigint IDENTITY(1, 1) NOT NULL,
        TimeUtc      datetime2(0)  NOT NULL CONSTRAINT DF_LicenseChecks_Time DEFAULT (SYSUTCDATETIME()),
        LicenseId    int           NULL,
        HwId         char(39)      NOT NULL,
        Result       tinyint       NOT NULL,   -- 1 geçerli, 2 süresi dolmuş, 3 iptal, 4 bilinmeyen makine
        Matched      tinyint       NOT NULL,   -- eşleşen HWID parçası (4 tam, 3 donanım değişmiş, 0 yok)
        Ip           varchar(45)   NULL,
        Version      varchar(32)   NULL,
        MachineName  nvarchar(64)  NULL,
        CONSTRAINT PK_LicenseChecks PRIMARY KEY CLUSTERED (CheckId) WITH (DATA_COMPRESSION = PAGE)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseChecks_Time' AND object_id = OBJECT_ID(N'dbo.LicenseChecks'))
    CREATE NONCLUSTERED INDEX IX_LicenseChecks_Time ON dbo.LicenseChecks (TimeUtc) WITH (DATA_COMPRESSION = PAGE);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseChecks_License' AND object_id = OBJECT_ID(N'dbo.LicenseChecks'))
    CREATE NONCLUSTERED INDEX IX_LicenseChecks_License ON dbo.LicenseChecks (LicenseId, CheckId DESC) WHERE LicenseId IS NOT NULL WITH (DATA_COMPRESSION = PAGE);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseChecks_Unknown' AND object_id = OBJECT_ID(N'dbo.LicenseChecks'))
    CREATE NONCLUSTERED INDEX IX_LicenseChecks_Unknown ON dbo.LicenseChecks (HwId, CheckId DESC) WHERE LicenseId IS NULL WITH (DATA_COMPRESSION = PAGE);
GO

IF OBJECT_ID(N'dbo.AdminAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdminAudit
    (
        AuditId    bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_AdminAudit PRIMARY KEY CLUSTERED,
        TimeUtc    datetime2(0)   NOT NULL CONSTRAINT DF_AdminAudit_Time DEFAULT (SYSUTCDATETIME()),
        Action     varchar(32)    NOT NULL,
        LicenseId  int            NULL,
        Ip         varchar(45)    NULL,
        Detail     nvarchar(1000) NULL
    );
END
GO

-- ============================================================ Rol (API sadece prosedür çalıştırır)

IF DATABASE_PRINCIPAL_ID(N'LicenseApiRole') IS NULL
    CREATE ROLE LicenseApiRole;
GRANT EXECUTE ON SCHEMA::dbo TO LicenseApiRole;
/* Canlıda API'yi çalıştıran servis hesabı için (örnek):
     CREATE LOGIN [ALANADI\svc-license] FROM WINDOWS;
     USE SecurityGuardLicense; CREATE USER [ALANADI\svc-license] FOR LOGIN [ALANADI\svc-license];
     ALTER ROLE LicenseApiRole ADD MEMBER [ALANADI\svc-license];                                  */
GO

-- ============================================================ Doğrulama (SecurityGuard servisleri 5 dk'da bir)

CREATE OR ALTER PROCEDURE dbo.usp_License_Validate
    @HwId        char(39),
    @Ip          varchar(45)  = NULL,
    @Version     varchar(32)  = NULL,
    @MachineName nvarchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @now datetime2(0) = SYSUTCDATETIME();
    DECLARE @h1 char(8) = SUBSTRING(@HwId, 5, 8), @h2 char(8) = SUBSTRING(@HwId, 14, 8),
            @h3 char(8) = SUBSTRING(@HwId, 23, 8), @h4 char(8) = SUBSTRING(@HwId, 32, 8);
    DECLARE @id int, @customer nvarchar(200), @expires datetime2(0), @status tinyint, @matched tinyint = 0, @result tinyint;

    -- Önce tam eşleşme (en sık durum, tek indeks araması), yoksa 4 parçanın en az 3'ü (disk / anakart değişmiş)
    SELECT TOP (1) @id = LicenseId, @customer = CustomerName, @expires = ExpiresUtc, @status = Status, @matched = 4
    FROM dbo.Licenses WHERE HwId = @HwId
    ORDER BY Status, ExpiresUtc DESC;

    IF @id IS NULL
        SELECT TOP (1) @id = LicenseId, @customer = CustomerName, @expires = ExpiresUtc, @status = Status, @matched = m
        FROM (
            SELECT LicenseId, CustomerName, ExpiresUtc, Status,
                   CAST(IIF(H1 = @h1, 1, 0) + IIF(H2 = @h2, 1, 0) + IIF(H3 = @h3, 1, 0) + IIF(H4 = @h4, 1, 0) AS tinyint) AS m
            FROM dbo.Licenses
            WHERE H1 = @h1 OR H2 = @h2 OR H3 = @h3 OR H4 = @h4
        ) c
        WHERE m >= 3
        ORDER BY m DESC, Status, ExpiresUtc DESC;

    SET @result = CASE WHEN @id IS NULL THEN 4 WHEN @status = 2 THEN 3 WHEN @expires < @now THEN 2 ELSE 1 END;

    INSERT dbo.LicenseChecks (TimeUtc, LicenseId, HwId, Result, Matched, Ip, Version, MachineName)
    VALUES (@now, @id, @HwId, @result, @matched, @Ip, @Version, @MachineName);

    IF @id IS NOT NULL
        UPDATE dbo.Licenses SET LastSeenUtc = @now, LastIp = @Ip, LastVersion = @Version, LastMachine = @MachineName
        WHERE LicenseId = @id;

    SELECT @result AS Result, @id AS LicenseId, @customer AS CustomerName, @expires AS ExpiresUtc, @matched AS Matched, @now AS ServerUtc;
END
GO

-- ============================================================ Yönetim

CREATE OR ALTER PROCEDURE dbo.usp_License_Get
    @LicenseId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT LicenseId, HwId, CustomerName, ExpiresUtc, Status, Note, CreatedUtc, UpdatedUtc, LastSeenUtc, LastIp, LastVersion, LastMachine
    FROM dbo.Licenses WHERE LicenseId = @LicenseId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_License_Create
    @HwId char(39), @CustomerName nvarchar(200), @ExpiresUtc datetime2(0), @Note nvarchar(500) = NULL, @Ip varchar(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM dbo.Licenses WITH (UPDLOCK, HOLDLOCK) WHERE HwId = @HwId AND Status = 1)
        THROW 50001, N'Bu HWID için zaten aktif bir lisans var. Süresini uzatın veya önce iptal edin.', 1; -- XACT_ABORT: işlem geri alınır
    INSERT dbo.Licenses (HwId, CustomerName, ExpiresUtc, Note) VALUES (@HwId, @CustomerName, @ExpiresUtc, @Note);
    DECLARE @id int = SCOPE_IDENTITY();
    INSERT dbo.AdminAudit (Action, LicenseId, Ip, Detail)
    VALUES ('create', @id, @Ip, CONCAT(@CustomerName, N' | ', @HwId, N' | bitiş ', CONVERT(varchar(19), @ExpiresUtc, 120)));
    COMMIT;
    EXEC dbo.usp_License_Get @LicenseId = @id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_License_Update
    @LicenseId int, @CustomerName nvarchar(200) = NULL, @ExpiresUtc datetime2(0) = NULL, @Note nvarchar(500) = NULL, @Ip varchar(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    UPDATE dbo.Licenses
    SET CustomerName = COALESCE(@CustomerName, CustomerName),
        ExpiresUtc = COALESCE(@ExpiresUtc, ExpiresUtc),
        Note = COALESCE(@Note, Note),
        UpdatedUtc = SYSUTCDATETIME()
    WHERE LicenseId = @LicenseId;
    IF @@ROWCOUNT = 0 THROW 50002, N'Lisans bulunamadı.', 1;
    INSERT dbo.AdminAudit (Action, LicenseId, Ip, Detail)
    VALUES ('update', @LicenseId, @Ip, CONCAT(N'müşteri ', @CustomerName, N' | bitiş ', CONVERT(varchar(19), @ExpiresUtc, 120), N' | not ', @Note));
    EXEC dbo.usp_License_Get @LicenseId = @LicenseId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_License_SetStatus
    @LicenseId int, @Status tinyint, @Ip varchar(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Status = 1 AND EXISTS (SELECT 1 FROM dbo.Licenses a JOIN dbo.Licenses b ON b.HwId = a.HwId AND b.Status = 1 AND b.LicenseId <> a.LicenseId WHERE a.LicenseId = @LicenseId)
        THROW 50003, N'Bu HWID için başka bir aktif lisans var.', 1;
    UPDATE dbo.Licenses SET Status = @Status, UpdatedUtc = SYSUTCDATETIME() WHERE LicenseId = @LicenseId;
    IF @@ROWCOUNT = 0 THROW 50002, N'Lisans bulunamadı.', 1;
    INSERT dbo.AdminAudit (Action, LicenseId, Ip) VALUES (IIF(@Status = 2, 'revoke', 'restore'), @LicenseId, @Ip);
    EXEC dbo.usp_License_Get @LicenseId = @LicenseId;
END
GO



CREATE OR ALTER PROCEDURE dbo.usp_License_List
    @Search nvarchar(200) = NULL, @Top int = 500
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top) LicenseId, HwId, CustomerName, ExpiresUtc, Status, Note, CreatedUtc, UpdatedUtc, LastSeenUtc, LastIp, LastVersion, LastMachine
    FROM dbo.Licenses
    WHERE @Search IS NULL OR CustomerName LIKE N'%' + @Search + N'%' OR HwId LIKE '%' + @Search + '%' OR LastMachine LIKE N'%' + @Search + N'%'
    ORDER BY LicenseId DESC
    OPTION (RECOMPILE);
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_License_Checks
    @LicenseId int = NULL, @HwId char(39) = NULL, @Top int = 200
AS
BEGIN
    SET NOCOUNT ON;
    IF @LicenseId IS NOT NULL
        SELECT TOP (@Top) CheckId, TimeUtc, LicenseId, HwId, Result, Matched, Ip, Version, MachineName
        FROM dbo.LicenseChecks WHERE LicenseId = @LicenseId ORDER BY CheckId DESC;
    ELSE
        SELECT TOP (@Top) CheckId, TimeUtc, LicenseId, HwId, Result, Matched, Ip, Version, MachineName
        FROM dbo.LicenseChecks WHERE LicenseId IS NULL AND (@HwId IS NULL OR HwId = @HwId) ORDER BY CheckId DESC;
END
GO

-- Lisansı olmayan ama doğrulama yapan makineler (yeni kurulumlar: HWID buradan da alınabilir)
CREATE OR ALTER PROCEDURE dbo.usp_Unknown_Machines
    @Days int = 7
AS
BEGIN
    SET NOCOUNT ON;
    SELECT HwId, MAX(MachineName) AS MachineName, MAX(Ip) AS LastIp, MAX(Version) AS Version, MIN(TimeUtc) AS FirstSeenUtc, MAX(TimeUtc) AS LastSeenUtc, COUNT_BIG(*) AS Checks
    FROM dbo.LicenseChecks
    WHERE LicenseId IS NULL AND TimeUtc >= DATEADD(DAY, -@Days, SYSUTCDATETIME())
    GROUP BY HwId
    ORDER BY LastSeenUtc DESC;
END
GO

-- Eski doğrulama kayıtları: küçük parçalarla (log ve kilit şişmesin)
CREATE OR ALTER PROCEDURE dbo.usp_Checks_Purge
    @KeepDays int = 90
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cut datetime2(0) = DATEADD(DAY, -@KeepDays, SYSUTCDATETIME()), @n int = 1, @total bigint = 0;
    WHILE @n > 0
    BEGIN
        DELETE TOP (5000) FROM dbo.LicenseChecks WHERE TimeUtc < @cut;
        SET @n = @@ROWCOUNT;
        SET @total += @n;
    END
    SELECT @total AS Deleted;
END
GO

-- ============================================================ Merkezi panel (ajan durum anlık görüntüsü)
-- Her aktif lisansın GÜNCEL durumu (tek satır / lisans). Ajan outbound heartbeat ile (1-5 dk) günceller.
-- Geçmiş tutulmaz (panel salt-okunur özet); gerekirse ileride AgentStatusHistory eklenir.

IF OBJECT_ID(N'dbo.AgentStatus', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AgentStatus
    (
        LicenseId         int           NOT NULL,
        HwId              char(39)      NOT NULL,
        LastHeartbeatUtc  datetime2(0)  NOT NULL,
        AgentVersion      varchar(32)   NULL,
        MachineName       nvarchar(64)  NULL,
        ServiceReady      bit           NOT NULL CONSTRAINT DF_AgentStatus_Ready  DEFAULT (0),
        LicenseValid      bit           NOT NULL CONSTRAINT DF_AgentStatus_Lic    DEFAULT (0),
        ProtectionOk      bit           NOT NULL CONSTRAINT DF_AgentStatus_Prot   DEFAULT (0),
        SqlMonitoring     bit           NOT NULL CONSTRAINT DF_AgentStatus_Sql    DEFAULT (0),
        BannedIps         int           NOT NULL CONSTRAINT DF_AgentStatus_Bans   DEFAULT (0),
        NewBans24h        int           NOT NULL CONSTRAINT DF_AgentStatus_NewB   DEFAULT (0),
        FailedLogins24h   int           NOT NULL CONSTRAINT DF_AgentStatus_Fails  DEFAULT (0),
        CriticalEvents24h int           NOT NULL CONSTRAINT DF_AgentStatus_Crit   DEFAULT (0),
        MemoryMb          int           NOT NULL CONSTRAINT DF_AgentStatus_Mem    DEFAULT (0),
        PendingMail       int           NOT NULL CONSTRAINT DF_AgentStatus_Mail   DEFAULT (0),
        AvailableVersion  nvarchar(32)  NULL,     -- mevcut sürümden yeni, kurulabilir sürüm (yoksa NULL)
        UpdateReady       bit           NOT NULL CONSTRAINT DF_AgentStatus_UpdRdy DEFAULT (0),
        Payload           nvarchar(max) NULL,     -- detay sayfası için tam JSON anlık görüntü
        UpdatedUtc        datetime2(0)  NOT NULL CONSTRAINT DF_AgentStatus_Upd    DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_AgentStatus PRIMARY KEY CLUSTERED (LicenseId),
        CONSTRAINT FK_AgentStatus_License FOREIGN KEY (LicenseId) REFERENCES dbo.Licenses (LicenseId)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgentStatus_Heartbeat' AND object_id = OBJECT_ID(N'dbo.AgentStatus'))
    CREATE NONCLUSTERED INDEX IX_AgentStatus_Heartbeat ON dbo.AgentStatus (LastHeartbeatUtc DESC);
GO
-- Sonradan eklenen kolonlar (mevcut veritabanı için; tablo zaten varsa yoksa ekle)
IF COL_LENGTH(N'dbo.AgentStatus', 'AvailableVersion') IS NULL ALTER TABLE dbo.AgentStatus ADD AvailableVersion nvarchar(32) NULL;
IF COL_LENGTH(N'dbo.AgentStatus', 'UpdateReady') IS NULL ALTER TABLE dbo.AgentStatus ADD UpdateReady bit NOT NULL CONSTRAINT DF_AgentStatus_UpdRdy DEFAULT (0);
GO

-- Ajan heartbeat: SADECE aktif lisansı olan makine kabul edilir (lisanssız/iptal ajan panele düşmez).
-- Müşteri adı ayrıca gelmez; lisanstan okunur (tek doğruluk kaynağı). LicenseId=0 => reddedildi.
CREATE OR ALTER PROCEDURE dbo.usp_Agent_Heartbeat
    @HwId              char(39),
    @AgentVersion      varchar(32)   = NULL,
    @MachineName       nvarchar(64)  = NULL,
    @ServiceReady      bit           = 0,
    @LicenseValid      bit           = 0,
    @ProtectionOk      bit           = 0,
    @SqlMonitoring     bit           = 0,
    @BannedIps         int           = 0,
    @NewBans24h        int           = 0,
    @FailedLogins24h   int           = 0,
    @CriticalEvents24h int           = 0,
    @MemoryMb          int           = 0,
    @PendingMail       int           = 0,
    @AvailableVersion  nvarchar(32)  = NULL,
    @UpdateReady       bit           = 0,
    @Payload           nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @now datetime2(0) = SYSUTCDATETIME(), @id int;

    SELECT TOP (1) @id = LicenseId FROM dbo.Licenses WHERE HwId = @HwId AND Status = 1 ORDER BY ExpiresUtc DESC;
    IF @id IS NULL BEGIN SELECT CAST(0 AS int) AS LicenseId; RETURN; END

    UPDATE dbo.AgentStatus
    SET HwId = @HwId, LastHeartbeatUtc = @now, AgentVersion = @AgentVersion, MachineName = @MachineName,
        ServiceReady = @ServiceReady, LicenseValid = @LicenseValid, ProtectionOk = @ProtectionOk, SqlMonitoring = @SqlMonitoring,
        BannedIps = @BannedIps, NewBans24h = @NewBans24h, FailedLogins24h = @FailedLogins24h, CriticalEvents24h = @CriticalEvents24h,
        MemoryMb = @MemoryMb, PendingMail = @PendingMail, AvailableVersion = @AvailableVersion, UpdateReady = @UpdateReady,
        Payload = @Payload, UpdatedUtc = @now
    WHERE LicenseId = @id;

    IF @@ROWCOUNT = 0
        INSERT dbo.AgentStatus (LicenseId, HwId, LastHeartbeatUtc, AgentVersion, MachineName, ServiceReady, LicenseValid, ProtectionOk, SqlMonitoring,
                                BannedIps, NewBans24h, FailedLogins24h, CriticalEvents24h, MemoryMb, PendingMail, AvailableVersion, UpdateReady, Payload, UpdatedUtc)
        VALUES (@id, @HwId, @now, @AgentVersion, @MachineName, @ServiceReady, @LicenseValid, @ProtectionOk, @SqlMonitoring,
                @BannedIps, @NewBans24h, @FailedLogins24h, @CriticalEvents24h, @MemoryMb, @PendingMail, @AvailableVersion, @UpdateReady, @Payload, @now);

    SELECT @id AS LicenseId;
END
GO

-- Panel: tüm aktif lisanslar + (varsa) güncel ajan durumu. Online/offline hesabı app'te (LastHeartbeatUtc eşiğiyle).
CREATE OR ALTER PROCEDURE dbo.usp_Panel_Agents
    @Search nvarchar(200) = NULL, @Top int = 1000
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
        l.LicenseId, l.HwId, l.CustomerName, l.ExpiresUtc, l.Status AS LicenseStatus,
        COALESCE(a.MachineName, l.LastMachine)   AS MachineName,
        COALESCE(a.AgentVersion, l.LastVersion)  AS AgentVersion,
        a.LastHeartbeatUtc, a.ServiceReady, a.LicenseValid, a.ProtectionOk, a.SqlMonitoring,
        a.BannedIps, a.NewBans24h, a.FailedLogins24h, a.CriticalEvents24h, a.MemoryMb, a.PendingMail,
        a.AvailableVersion, a.UpdateReady
    FROM dbo.Licenses l
    LEFT JOIN dbo.AgentStatus a ON a.LicenseId = l.LicenseId
    WHERE l.Status = 1
      AND (@Search IS NULL OR l.CustomerName LIKE N'%' + @Search + N'%' OR l.HwId LIKE '%' + @Search + '%' OR l.LastMachine LIKE N'%' + @Search + N'%')
    ORDER BY l.CustomerName, a.LastHeartbeatUtc DESC
    OPTION (RECOMPILE);
END
GO

-- Panel: tek müşteri/makine detayı (tam JSON anlık görüntü dahil).
CREATE OR ALTER PROCEDURE dbo.usp_Panel_AgentDetail
    @LicenseId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT l.LicenseId, l.HwId, l.CustomerName, l.ExpiresUtc, l.Status AS LicenseStatus, l.Note,
           l.CreatedUtc, l.LastSeenUtc, l.LastIp,
           a.LastHeartbeatUtc, a.AgentVersion, a.MachineName, a.ServiceReady, a.LicenseValid, a.ProtectionOk, a.SqlMonitoring,
           a.BannedIps, a.NewBans24h, a.FailedLogins24h, a.CriticalEvents24h, a.MemoryMb, a.PendingMail,
           a.AvailableVersion, a.UpdateReady, a.Payload
    FROM dbo.Licenses l
    LEFT JOIN dbo.AgentStatus a ON a.LicenseId = l.LicenseId
    WHERE l.LicenseId = @LicenseId;
END
GO

PRINT N'SecurityGuardLicense hazır.';
