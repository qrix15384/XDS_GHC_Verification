/* ============================================================================
   002_add_proxy_users_table.sql
   Microsoft SQL Server — additive migration for XdsGhcVerification. Adds
   dbo.ProxyUsers so individual named accounts (not one shared username/
   password) can log in via POST /api/v1/auth/login. Everyone with a valid
   ProxyUsers account still receives the one shared ServiceAuth:ApiKey used
   as X-API-Key on every other endpoint — this table only gates WHO can
   obtain that key under their own logged-in identity, and who may manage
   other accounts (Role = 'Admin').

   This script is ADDITIVE and safe to re-run — unlike 001, it must never
   assume a clean database and never drops anything. It does not touch
   dbo.ApiTransactionLog.

   Run from a machine with sqlcmd/SSMS access to the target SQL Server
   instance:
     sqlcmd -S <server> -d XdsGhcVerification -E -i 002_add_proxy_users_table.sql
   (uses Windows Authentication; swap -E for -U/-P if using SQL auth)
   ============================================================================ */

USE XdsGhcVerification;
GO

IF OBJECT_ID('dbo.ProxyUsers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProxyUsers
    (
        Id            INT           IDENTITY(1,1) NOT NULL,
        Username      NVARCHAR(100) NOT NULL,
        PasswordHash  NVARCHAR(300) NOT NULL, -- ASP.NET Core Identity PasswordHasher<T> output, base64
        Role          NVARCHAR(20)  NOT NULL
            CONSTRAINT DF_ProxyUsers_Role DEFAULT 'Standard',
        IsActive      BIT           NOT NULL
            CONSTRAINT DF_ProxyUsers_IsActive DEFAULT 1,
        CreatedAtUtc  DATETIME2(3)  NOT NULL
            CONSTRAINT DF_ProxyUsers_CreatedAtUtc DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_ProxyUsers PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_ProxyUsers_Role CHECK (Role IN ('Admin', 'Standard'))
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_ProxyUsers_Username' AND object_id = OBJECT_ID('dbo.ProxyUsers')
)
    CREATE UNIQUE INDEX UX_ProxyUsers_Username ON dbo.ProxyUsers (Username);
GO

-- ─── Extend the existing least-privilege login for the new table ───────────
-- Unlike ApiTransactionLog (append-only: SELECT/INSERT), ProxyUsers needs
-- full CRUD from the app (create/edit/deactivate/delete accounts) — still
-- no db_owner, still no rights on any other table.
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ProxyUsers TO xds_ghc_svc;
GO
