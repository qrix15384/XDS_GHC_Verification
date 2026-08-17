/* ============================================================================
   004_link_subscribers_to_ghana_admin.sql
   Microsoft SQL Server — additive/corrective migration for XdsGhcVerification
   ONLY. Does not touch XdsGhanaAdmin or any table in it.

   Supersedes 003's locally-owned dbo.Subscribers table: subscribers are now
   read directly (read-only) from the real, authoritative XdsGhanaAdmin.dbo.Subscriber
   table on the same SQL Server instance via a cross-database query, instead
   of duplicating them here. dbo.ProxyUsers.SubscriberId now refers to that
   table's SubscriberID — there is no cross-database FK (SQL Server doesn't
   support one), so existence is validated in application code instead.

   This script is ADDITIVE/CORRECTIVE and safe to re-run.

   Prerequisite (run once, against XdsGhanaAdmin, NOT by this script):
     USE XdsGhanaAdmin;
     CREATE USER xds_ghc_svc FOR LOGIN xds_ghc_svc;
     GRANT SELECT ON dbo.Subscriber TO xds_ghc_svc;

   Run from a machine with sqlcmd/SSMS access to the target SQL Server
   instance:
     sqlcmd -S <server> -d XdsGhcVerification -E -i 004_link_subscribers_to_ghana_admin.sql
   (uses Windows Authentication; swap -E for -U/-P if using SQL auth)
   ============================================================================ */

USE XdsGhcVerification;
GO

IF EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_ProxyUsers_Subscribers' AND parent_object_id = OBJECT_ID('dbo.ProxyUsers')
)
    ALTER TABLE dbo.ProxyUsers DROP CONSTRAINT FK_ProxyUsers_Subscribers;
GO

IF OBJECT_ID('dbo.Subscribers', 'U') IS NOT NULL
    DROP TABLE dbo.Subscribers;
GO
