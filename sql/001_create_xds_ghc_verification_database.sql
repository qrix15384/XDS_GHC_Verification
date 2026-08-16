/* ============================================================================
   001_create_xds_ghc_verification_database.sql
   Microsoft SQL Server — provisions the `XdsGhcVerification` database used to
   log every transaction handled by this proxy: which endpoint was hit, who
   hit it, when, the response returned to the client, and whether details
   were found.

   Run from a machine with sqlcmd/SSMS access to the target SQL Server
   instance:
     sqlcmd -S <server> -E -i 001_create_xds_ghc_verification_database.sql
   (uses Windows Authentication; swap -E for -U/-P if using SQL auth and the
   executing login has rights to CREATE DATABASE / CREATE LOGIN)

   Security note: raw secrets (X-API-Key, upstream merchant key, login
   passwords) are never stored here. PinNumber is retained in the clear for
   lookups; if your compliance requirements call for encryption at rest,
   apply SQL Server Always Encrypted to that column and to ResponsePayload
   (which may echo it back).
   ============================================================================ */

IF DB_ID('XdsGhcVerification') IS NULL
    CREATE DATABASE XdsGhcVerification;
GO

USE XdsGhcVerification;
GO

IF OBJECT_ID('dbo.ApiTransactionLog', 'U') IS NOT NULL
    DROP TABLE dbo.ApiTransactionLog;
GO

CREATE TABLE dbo.ApiTransactionLog
(
    Id                   BIGINT           IDENTITY(1,1) NOT NULL,
    RequestId            UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_ApiTransactionLog_RequestId DEFAULT NEWID(),
    RequestAtUtc         DATETIME2(3)     NOT NULL
        CONSTRAINT DF_ApiTransactionLog_RequestAtUtc DEFAULT SYSUTCDATETIME(),

    -- Which endpoint was hit
    EndpointPath         NVARCHAR(200)    NOT NULL, -- e.g. /api/v1/selfie/verification/kyc/face
    HttpMethod           VARCHAR(10)      NOT NULL,

    -- Who hit it
    Username             NVARCHAR(100)    NULL,

    -- Response details
    HttpStatusCode       SMALLINT         NOT NULL,
    ResponsePayload      NVARCHAR(MAX)    NULL, -- JSON of the response returned to the client (image data stripped)
    DetailsFound         CHAR(1)          NULL
        CONSTRAINT CK_ApiTransactionLog_DetailsFound CHECK (DetailsFound IN ('Y', 'N')),
    ErrorMessage         NVARCHAR(500)    NULL,
    DurationMs           INT              NULL,

    -- Selfie-specific correlation field; NULL for non-selfie endpoints
    PinNumber            VARCHAR(20)      NULL,

    CONSTRAINT PK_ApiTransactionLog PRIMARY KEY CLUSTERED (Id)
);
GO

CREATE INDEX IX_ApiTransactionLog_RequestAtUtc  ON dbo.ApiTransactionLog (RequestAtUtc DESC);
CREATE INDEX IX_ApiTransactionLog_EndpointPath  ON dbo.ApiTransactionLog (EndpointPath);
CREATE INDEX IX_ApiTransactionLog_Username      ON dbo.ApiTransactionLog (Username);
GO

-- ─── Least-privilege login for the app ──────────────────────────────────────
-- Append-only log: SELECT + INSERT only, no UPDATE/DELETE, no db_owner.
-- Replace '<CHANGE_ME_STRONG_PASSWORD>' with a freshly generated password
-- before running this script — never commit a real password here. Put the
-- real value only in user-secrets (local dev) or the server's production
-- config (never in appsettings.json).
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'xds_ghc_svc')
    CREATE LOGIN xds_ghc_svc WITH PASSWORD = '<CHANGE_ME_STRONG_PASSWORD>', CHECK_POLICY = ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'xds_ghc_svc')
    CREATE USER xds_ghc_svc FOR LOGIN xds_ghc_svc;
GO

GRANT SELECT, INSERT ON dbo.ApiTransactionLog TO xds_ghc_svc;
GO
