<#
.SYNOPSIS
    Provisions a full XDS_GHC_Verification stack (SQL schema, API, admin console,
    IIS application) on a single Windows Server box that already has IIS, the
    .NET 10 Hosting Bundle, and SQL Server installed.

.DESCRIPTION
    Run this AS ADMINISTRATOR, directly on the target machine, from a checkout of
    this repo on that same machine (e.g. after `git clone`/`git pull` there).
    There is no remote-execution step here — this script has no way to reach
    another machine, and isn't meant to; you RDP/log into the target box yourself
    and run it there.

    Deploys the app as a nested IIS Application at /xdsghc under an existing site
    (reusing "Default Web Site" if present), matching the frontend's hardcoded
    Vite `base: '/xdsghc/'` — deploying it as a top-level site at the domain root
    instead would break every asset reference.

    Prerequisites this script assumes are already true:
      - IIS is installed (Web Server role) with the .NET 10 Hosting Bundle.
      - SQL Server is installed and reachable as -SqlServerInstance, with SQL
        authentication enabled and a known 'sa' (or equivalent) login.
      - Node.js is installed (for building the frontend).
      - The XdsGhanaAdmin database (owned by a different XDS system) either
        already exists on this SQL Server instance, or will before anyone uses
        the admin console's Subscribers page — this script does not create it,
        only grants xds_ghc_svc read access to it if/when it's present.

    What it deliberately does NOT do:
      - Install IIS or the Hosting Bundle (OS feature changes are out of scope
        for an unattended script — do that yourself first).
      - Fill in real Upstream (NIA) / Selfie / CreditApi vendor secrets. Those
        are written as "<FILL-ME-IN>" placeholders in appsettings.Production.json
        since this script has no way to know your real vendor credentials —
        selfie verification won't work until you fill those in and recycle the
        app pool.
      - Configure HTTPS. Binds HTTP only by default, which is fine for an
        internal-only box; add a certificate/binding yourself if this needs to
        be reachable over TLS.

.EXAMPLE
    .\Provision-FullStack.ps1
    (run from the repo root, using all defaults — SQL Server on localhost,
    IIS application at /xdsghc under Default Web Site)
#>

[CmdletBinding()]
param(
    [string]$RepoPath = (Get-Location).Path,
    [string]$SqlServerInstance = "localhost",
    [string]$ParentSiteName = "Default Web Site",
    [int]$HttpPort = 80,
    [string]$AppPath = "xdsghc",
    [string]$AppPoolName = "xds-ghc-verification",
    [string]$SitePhysicalPath = "C:\inetpub\wwwroot\xds-ghc-verification",
    [string]$InitialAdminUsername = "Admin",
    # Optional: path to a filled-in copy of deploy/vendor-secrets.template.json
    # (Upstream/Selfie/CreditApi values this script has no way to know itself).
    # Defaults to deploy/vendor-secrets.json next to this script if present;
    # falls back to "<FILL-ME-IN>" placeholders with a warning if not found.
    [string]$VendorSecretsPath = (Join-Path $PSScriptRoot "vendor-secrets.json")
)

$ErrorActionPreference = "Stop"

function New-RandomSecret {
    param([int]$Bytes = 32)
    return [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes($Bytes))
}

Write-Host "=== XDS_GHC_Verification - full-stack provisioning ===" -ForegroundColor Cyan
Write-Host "Target IIS path: http://<this-host>:$HttpPort/$AppPath/"

# ── 0. The one secret this script needs from you interactively ─────────────
# Never hardcode this into a saved script file.
$sqlSaPasswordSecure = Read-Host -AsSecureString -Prompt "SQL Server 'sa' (or equivalent) password"
$sqlSaPassword = [System.Net.NetworkCredential]::new('', $sqlSaPasswordSecure).Password

# ── 1. Generate fresh secrets for THIS environment (never reused from prod) ─
$svcSqlPassword   = New-RandomSecret
$jwtSigningKey    = New-RandomSecret
$serviceApiKey    = New-RandomSecret
$initialAdminPass = New-RandomSecret -Bytes 18

Write-Host "`n[1/7] Generated fresh secrets for this environment." -ForegroundColor Green

# ── 2. Run SQL migrations 001-005 ───────────────────────────────────────────
Write-Host "[2/7] Running SQL migrations against $SqlServerInstance ..." -ForegroundColor Green

$sqlDir = Join-Path $RepoPath "sql"
$tempDir = Join-Path $env:TEMP "xds-ghc-sql-deploy"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

# 001 has a literal password placeholder for the xds_ghc_svc login - substitute
# it into a temp copy rather than editing (or leaving a real secret inside) the
# committed repo file.
$script001Path = Join-Path $sqlDir "001_create_xds_ghc_verification_database.sql"
$script001 = (Get-Content $script001Path -Raw).Replace('<CHANGE_ME_STRONG_PASSWORD>', $svcSqlPassword)
$temp001 = Join-Path $tempDir "001_create_xds_ghc_verification_database.sql"
Set-Content -Path $temp001 -Value $script001 -Encoding UTF8

try {
    sqlcmd -S $SqlServerInstance -U sa -P $sqlSaPassword -i $temp001
    sqlcmd -S $SqlServerInstance -U sa -P $sqlSaPassword -d XdsGhcVerification -i (Join-Path $sqlDir "002_add_proxy_users_table.sql")
    sqlcmd -S $SqlServerInstance -U sa -P $sqlSaPassword -d XdsGhcVerification -i (Join-Path $sqlDir "003_add_subscribers_table.sql")
    sqlcmd -S $SqlServerInstance -U sa -P $sqlSaPassword -d XdsGhcVerification -i (Join-Path $sqlDir "004_link_subscribers_to_ghana_admin.sql")
    sqlcmd -S $SqlServerInstance -U sa -P $sqlSaPassword -d XdsGhcVerification -i (Join-Path $sqlDir "005_add_verification_response_logs.sql")
}
finally {
    Remove-Item -Recurse -Force $tempDir
}

# 004's own header comment documents a prerequisite grant against XdsGhanaAdmin -
# run it only if that database is actually present (it's owned by a different
# system and may not have been restored onto this box yet).
$ghanaAdminExists = (sqlcmd -S $SqlServerInstance -U sa -P $sqlSaPassword -h -1 -Q `
    "SET NOCOUNT ON; IF DB_ID('XdsGhanaAdmin') IS NOT NULL SELECT 1 ELSE SELECT 0").Trim()

if ($ghanaAdminExists -eq "1") {
    Write-Host "  XdsGhanaAdmin found - granting xds_ghc_svc read access to dbo.Subscriber." -ForegroundColor Green
    sqlcmd -S $SqlServerInstance -U sa -P $sqlSaPassword -Q `
        "USE XdsGhanaAdmin; IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'xds_ghc_svc') CREATE USER xds_ghc_svc FOR LOGIN xds_ghc_svc; GRANT SELECT ON dbo.Subscriber TO xds_ghc_svc;"
}
else {
    Write-Warning ("XdsGhanaAdmin not found on $SqlServerInstance yet. Subscriber-linked features " +
        "(the Subscribers page, SubscriberId on users) won't work until it's restored AND you re-run:`n" +
        "  USE XdsGhanaAdmin; CREATE USER xds_ghc_svc FOR LOGIN xds_ghc_svc; GRANT SELECT ON dbo.Subscriber TO xds_ghc_svc;")
}

# ── 3. Build the frontend and stage it into wwwroot ─────────────────────────
Write-Host "[3/7] Building frontend ..." -ForegroundColor Green
Push-Location (Join-Path $RepoPath "frontend")
npm install
npm run build
Pop-Location

$wwwroot = Join-Path $RepoPath "src\XDS_GHC_Verification\wwwroot"
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $RepoPath "frontend\dist\*") -Destination $wwwroot -Recurse -Force

# ── 4. Publish the API (bundles wwwroot as of right now) ────────────────────
Write-Host "[4/7] Publishing to $SitePhysicalPath ..." -ForegroundColor Green
New-Item -ItemType Directory -Force -Path $SitePhysicalPath | Out-Null
dotnet publish (Join-Path $RepoPath "src\XDS_GHC_Verification") -c Release -o $SitePhysicalPath

# ── 5. Write appsettings.Production.json next to the published DLL ──────────
Write-Host "[5/7] Writing appsettings.Production.json ..." -ForegroundColor Green

$vendorSecretsFilled = $false
if (Test-Path $VendorSecretsPath) {
    Write-Host "  Loading vendor secrets from $VendorSecretsPath" -ForegroundColor Green
    $vendor = Get-Content $VendorSecretsPath -Raw | ConvertFrom-Json
    $upstreamSection = [ordered]@{
        BaseUrl      = $vendor.Upstream.BaseUrl
        AuthType     = $vendor.Upstream.AuthType
        ApiKey       = $vendor.Upstream.ApiKey
        ApiKeyHeader = $vendor.Upstream.ApiKeyHeader
    }
    $selfieSection = [ordered]@{
        MerchantKey = $vendor.Selfie.MerchantKey
        Center      = $vendor.Selfie.Center
        UserId      = $vendor.Selfie.UserId
    }
    $creditApiSection = [ordered]@{
        BaseUrl  = $vendor.CreditApi.BaseUrl
        Username = $vendor.CreditApi.Username
        Password = $vendor.CreditApi.Password
    }
    $vendorSecretsFilled = $true
}
else {
    Write-Warning ("No vendor secrets file found at $VendorSecretsPath - writing <FILL-ME-IN> placeholders for " +
        "Upstream/Selfie/CreditApi. Copy deploy\vendor-secrets.template.json, fill in the real values, save it as " +
        "deploy\vendor-secrets.json (gitignored), and re-run this script, or hand-edit the generated config afterward.")
    $upstreamSection = [ordered]@{
        BaseUrl      = "<FILL-ME-IN: real NIA base URL>"
        AuthType     = "<FILL-ME-IN>"
        ApiKey       = "<FILL-ME-IN>"
        ApiKeyHeader = "X-API-Key"
    }
    $selfieSection = [ordered]@{
        MerchantKey = "<FILL-ME-IN>"
        Center      = "BRANCHLESS"
        UserId      = "<FILL-ME-IN>"
    }
    $creditApiSection = [ordered]@{
        BaseUrl  = "<FILL-ME-IN>"
        Username = "<FILL-ME-IN>"
        Password = "<FILL-ME-IN>"
    }
}

$prodConfig = [ordered]@{
    ServiceAuth = [ordered]@{
        ApiKey       = $serviceApiKey
        AuthUsername = $InitialAdminUsername
        AuthPassword = $initialAdminPass
    }
    Upstream  = $upstreamSection
    Selfie    = $selfieSection
    CreditApi = $creditApiSection
    Cors = [ordered]@{
        AllowedOrigins = ""
    }
    Jwt = [ordered]@{
        SigningKey    = $jwtSigningKey
        Issuer        = "XDS_GHC_Verification"
        Audience      = "XDS_GHC_Verification"
        ExpiryMinutes = 480
    }
    LoginRateLimit = [ordered]@{
        PermitLimit   = 5
        WindowSeconds = 60
    }
    ConnectionStrings = [ordered]@{
        Verification = "Server=$SqlServerInstance;Database=XdsGhcVerification;User Id=xds_ghc_svc;Password=$svcSqlPassword;Encrypt=True;TrustServerCertificate=True;"
    }
} | ConvertTo-Json -Depth 5

$prodConfigPath = Join-Path $SitePhysicalPath "appsettings.Production.json"
Set-Content -Path $prodConfigPath -Value $prodConfig -Encoding UTF8

icacls $prodConfigPath /inheritance:r | Out-Null
icacls $prodConfigPath /grant "IIS AppPool\${AppPoolName}:R" | Out-Null
icacls $prodConfigPath /grant "Administrators:F" | Out-Null

# ── 6. Create the IIS application + app pool ────────────────────────────────
Write-Host "[6/7] Configuring IIS application /$AppPath under '$ParentSiteName' ..." -ForegroundColor Green
Import-Module WebAdministration

if (-not (Get-Website -Name $ParentSiteName -ErrorAction SilentlyContinue)) {
    Write-Host "  '$ParentSiteName' not found - creating it bound to port $HttpPort." -ForegroundColor Yellow
    New-Website -Name $ParentSiteName -PhysicalPath "C:\inetpub\wwwroot" -Port $HttpPort | Out-Null
}

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-WebAppPool -Name $AppPoolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""

if (-not (Get-WebApplication -Site $ParentSiteName -Name $AppPath -ErrorAction SilentlyContinue)) {
    New-WebApplication -Site $ParentSiteName -Name $AppPath -PhysicalPath $SitePhysicalPath -ApplicationPool $AppPoolName | Out-Null
}

icacls $SitePhysicalPath /grant "IIS AppPool\${AppPoolName}:(OI)(CI)RX" /T | Out-Null

Restart-WebAppPool -Name $AppPoolName

# ── 7. Smoke test ────────────────────────────────────────────────────────────
Write-Host "[7/7] Smoke test ..." -ForegroundColor Green
Start-Sleep -Seconds 3
$healthUrl = "http://localhost:$HttpPort/$AppPath/health"
try {
    $health = Invoke-RestMethod -Uri $healthUrl
    Write-Host "Health check OK ($healthUrl): $($health | ConvertTo-Json -Compress)" -ForegroundColor Green
}
catch {
    Write-Warning "Health check against $healthUrl failed - check the app pool's stdout log / Windows Event Log. $_"
}

Write-Host "`n=== Done ===" -ForegroundColor Cyan
Write-Host "Reachable at: http://<this-host>:$HttpPort/$AppPath/"
Write-Host "`nInitial admin login (seeded on first start - SAVE THIS NOW, it is not shown again):"
Write-Host "  Username: $InitialAdminUsername"
Write-Host "  Password: $initialAdminPass"
Write-Host "`nStill required before this is fully functional:"
if ($vendorSecretsFilled) {
    Write-Host "  1. Upstream / Selfie / CreditApi values were loaded from $VendorSecretsPath - verify they're correct." -ForegroundColor Green
}
else {
    Write-Host "  1. Fill in real Upstream / Selfie / CreditApi values in:" -ForegroundColor Yellow
    Write-Host "       $prodConfigPath"
    Write-Host "     then: Restart-WebAppPool -Name '$AppPoolName'"
}
Write-Host "  2. If XdsGhanaAdmin wasn't found above, restore it, then re-run the grant shown in that warning."
Write-Host "  3. Log in once with the credentials above, create named accounts for real users, consider deactivating this seed account."
Write-Host "  4. Add an HTTPS binding yourself if this needs to be reachable over TLS."
