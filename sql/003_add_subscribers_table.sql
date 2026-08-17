/* ============================================================================
   003_add_subscribers_table.sql
   Microsoft SQL Server — additive migration for XdsGhcVerification. Adds
   dbo.Subscribers (client organizations using this service — e.g. a bank or
   telco), assigns each dbo.ProxyUsers account to one (nullable — accounts
   don't have to belong to a subscriber), and records which subscriber made
   each call directly on dbo.ApiTransactionLog (denormalized as a name
   snapshot at write time, same treatment as the existing Username column,
   so a later subscriber rename doesn't rewrite history).

   This script is ADDITIVE and safe to re-run. It does not drop or rewrite
   any existing table or data.

   Run from a machine with sqlcmd/SSMS access to the target SQL Server
   instance:
     sqlcmd -S <server> -d XdsGhcVerification -E -i 003_add_subscribers_table.sql
   (uses Windows Authentication; swap -E for -U/-P if using SQL auth)
   ============================================================================ */

USE XdsGhcVerification;
GO

IF OBJECT_ID('dbo.Subscribers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Subscribers
    (
        Id            INT           IDENTITY(1,1) NOT NULL,
        Name          NVARCHAR(200) NOT NULL,
        IsActive      BIT           NOT NULL CONSTRAINT DF_Subscribers_IsActive DEFAULT 1,
        CreatedAtUtc  DATETIME2(3)  NOT NULL CONSTRAINT DF_Subscribers_CreatedAtUtc DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_Subscribers PRIMARY KEY CLUSTERED (Id)
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_Subscribers_Name' AND object_id = OBJECT_ID('dbo.Subscribers')
)
    CREATE UNIQUE INDEX UX_Subscribers_Name ON dbo.Subscribers (Name);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ProxyUsers') AND name = 'SubscriberId'
)
BEGIN
    ALTER TABLE dbo.ProxyUsers ADD SubscriberId INT NULL;
    ALTER TABLE dbo.ProxyUsers ADD CONSTRAINT FK_ProxyUsers_Subscribers
        FOREIGN KEY (SubscriberId) REFERENCES dbo.Subscribers (Id);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ApiTransactionLog') AND name = 'SubscriberId'
)
    ALTER TABLE dbo.ApiTransactionLog ADD SubscriberId INT NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ApiTransactionLog') AND name = 'SubscriberName'
)
    ALTER TABLE dbo.ApiTransactionLog ADD SubscriberName NVARCHAR(200) NULL;
GO

-- ─── Extend the existing least-privilege login for the new table ───────────
-- xds_ghc_svc already has SELECT/INSERT/UPDATE/DELETE on ProxyUsers and
-- SELECT/INSERT on ApiTransactionLog — those grants already cover the new
-- columns added above (grants are table-level). Subscribers needs the same
-- full-CRUD treatment as ProxyUsers.
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Subscribers TO xds_ghc_svc;
GO
