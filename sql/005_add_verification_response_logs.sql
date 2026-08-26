/* ============================================================================
   005_add_verification_response_logs.sql
   Microsoft SQL Server — additive migration for XdsGhcVerification.

   Adds two detail tables for the Selfie Verification (KYC + YES/NO) flow,
   split out from the existing dbo.ApiTransactionLog summary row:

     - dbo.NiaResponseLog:   the ORIGINAL, unmasked response the upstream
                             (NIA) verification service returned.
     - dbo.ProxyResponseLog: the MASKED response this proxy actually
                             returned to the calling client for that same call.

   The two rows for one call share the same RequestId (app-generated, not a
   DB default) so they can be correlated — NiaResponseLog only gets a row
   when NIA was actually called (e.g. not on a request that failed image
   validation before ever reaching NIA); ProxyResponseLog gets a row for
   every response sent to the client, NIA-backed or not.

   This script is ADDITIVE and safe to re-run.

   Run from a machine with sqlcmd/SSMS access to the target SQL Server instance:
     sqlcmd -S <server> -d XdsGhcVerification -E -i 005_add_verification_response_logs.sql
   (uses Windows Authentication; swap -E for -U/-P if using SQL auth)
   ============================================================================ */

USE XdsGhcVerification;
GO

IF OBJECT_ID('dbo.NiaResponseLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NiaResponseLog
    (
        Id                   BIGINT           IDENTITY(1,1) NOT NULL,
        RequestId            UNIQUEIDENTIFIER NOT NULL, -- correlates to the matching dbo.ProxyResponseLog row
        EndpointPath         NVARCHAR(200)    NOT NULL,
        PinNumber            VARCHAR(20)      NULL,

        -- When we called NIA / when NIA responded. Populated from NIA's own
        -- echoed requestTimestamp/responseTimestamp when present (its own
        -- clock, exact processing moment); falls back to our own measured
        -- time around the call when NIA didn't echo one back.
        CallAtUtc            DATETIME2(3)     NOT NULL,
        ResponseAtUtc        DATETIME2(3)     NULL,

        HttpStatusCode       SMALLINT         NOT NULL,
        RawResponsePayload   NVARCHAR(MAX)    NULL, -- the unmasked NIA response (image/biometric blobs still redacted)

        Username             NVARCHAR(100)    NULL,
        SubscriberId         INT              NULL,
        SubscriberName       NVARCHAR(200)    NULL,

        CONSTRAINT PK_NiaResponseLog PRIMARY KEY CLUSTERED (Id)
    );
END
GO

CREATE INDEX IX_NiaResponseLog_RequestId  ON dbo.NiaResponseLog (RequestId);
CREATE INDEX IX_NiaResponseLog_CallAtUtc  ON dbo.NiaResponseLog (CallAtUtc DESC);
GO

IF OBJECT_ID('dbo.ProxyResponseLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProxyResponseLog
    (
        Id                     BIGINT           IDENTITY(1,1) NOT NULL,
        RequestId              UNIQUEIDENTIFIER NOT NULL, -- correlates to the matching dbo.NiaResponseLog row, when one exists
        EndpointPath           NVARCHAR(200)    NOT NULL,
        PinNumber               VARCHAR(20)      NULL,

        -- When the client's request arrived at this proxy / when we sent our
        -- response back to them. Measured locally — this is about our own
        -- system's boundary with the client, not NIA's.
        CallAtUtc              DATETIME2(3)     NOT NULL,
        ResponseAtUtc          DATETIME2(3)     NOT NULL,

        HttpStatusCode         SMALLINT         NOT NULL,
        MaskedResponsePayload  NVARCHAR(MAX)    NULL, -- the masked/restructured response actually returned to the client

        Username               NVARCHAR(100)    NULL,
        SubscriberId            INT              NULL,
        SubscriberName          NVARCHAR(200)    NULL,

        CONSTRAINT PK_ProxyResponseLog PRIMARY KEY CLUSTERED (Id)
    );
END
GO

CREATE INDEX IX_ProxyResponseLog_RequestId  ON dbo.ProxyResponseLog (RequestId);
CREATE INDEX IX_ProxyResponseLog_CallAtUtc  ON dbo.ProxyResponseLog (CallAtUtc DESC);
GO

-- ─── Extend the existing least-privilege login for the two new tables ──────
GRANT SELECT, INSERT ON dbo.NiaResponseLog TO xds_ghc_svc;
GRANT SELECT, INSERT ON dbo.ProxyResponseLog TO xds_ghc_svc;
GO
