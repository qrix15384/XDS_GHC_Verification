# Production Deployment Guide

Deploying **XDS_GHC_Verification** to two separate, networked machines:

- **App server** — Windows Server 2016, running this ASP.NET Core app via IIS's **ASP.NET Core Module** (in-process hosting — IIS runs the app directly inside `w3wp.exe`, no bridging module or separate service).
- **Database server** — Windows Server 2008 R2, running SQL Server 2012, hosting the `XdsGhcVerification` database.

```
Internet
   │  HTTPS (443)
   ▼
┌─────────────────────────────┐        ┌──────────────────────────┐
│  APP SERVER (Win Server 2016)│  1433  │  DB SERVER (Win Server 2008 R2)│
│  IIS (in-process ANCM) →     │───────▶│  SQL Server 2012          │
│  ASP.NET Core app            │  TLS   │  XdsGhcVerification.ApiTransactionLog │
└──────────────┬───────────────┘        └──────────────────────────┘
               │ HTTPS
               ▼
      Upstream Selfie / NIA API
      (selfie.imsgh.org:2035)
```

This is *substantially* simpler than a comparable Python/IIS deployment — IIS hosts .NET natively via ANCM, so there's no ARR, no URL Rewrite proxy rule, no NSSM/Scheduled Task, and no venv-equivalent process-launcher quirks to work around.

---

## 1. Before you start

- [ ] Windows Server 2016 for the app, with IIS available.
- [ ] Windows Server 2008 R2 running SQL Server 2012 for the database — **this OS is out of Microsoft's product support** (extended support ended January 2020, no security patches without a paid ESU contract) and **does not support TLS 1.2 out of the box**. Both are addressed below (§2.6), but flag this to whoever owns infrastructure risk.
- [ ] A real TLS certificate for the app's public hostname.
- [ ] Network path from the app server to the DB server on port 1433, and from the app server out to the internet (upstream API) on port 443/2035.
- [ ] The production values for every secret currently in local `user-secrets`: `ServiceAuth:ApiKey`/`AuthUsername`/`AuthPassword`, `Selfie:MerchantKey`/`UserId`, `Jwt:SigningKey` (a fresh, random ≥32-character string — do **not** reuse the dev value), and the DB connection string.

---

## 2. Database server: SQL Server 2012 on Windows Server 2008 R2

### 2.1 Run the schema script

Edit `sql/001_create_xds_ghc_verification_database.sql` first and replace `<CHANGE_ME_STRONG_PASSWORD>` with a freshly generated password — never commit a real one. Then:

```cmd
sqlcmd -S <db-server> -E -i sql\001_create_xds_ghc_verification_database.sql
sqlcmd -S <db-server> -d XdsGhcVerification -E -i sql\002_add_proxy_users_table.sql
```

`001` creates the `XdsGhcVerification` database, the `ApiTransactionLog` table, and the least-privilege `xds_ghc_svc` login (`SELECT`/`INSERT` only — no `db_owner`, no `sysadmin`). `002` is additive and safe to re-run — it adds the `ProxyUsers` table (individual login accounts, replacing the old single shared username/password check) and grants `xds_ghc_svc` full CRUD on just that one table.

> **`001` drops and recreates `ApiTransactionLog`.** Run it once, on initial provisioning. If the table already holds production data, do **not** re-run this file — write a new, additive `00N_*.sql` script instead, following `002`'s pattern.

On first run against a freshly-migrated database, the app seeds one `Admin` account into `ProxyUsers` using the `ServiceAuth:AuthUsername`/`AuthPassword` secrets below — that's your first login on a new environment. Create real named accounts (and disable/delete the seed one if desired) via the admin console once you're in.

### 2.2 Enable Mixed Mode authentication

SQL Server Authentication (username/password) requires **Mixed Mode** (SQL Server Configuration Manager → instance Properties → Security), which needs a service restart to take effect. This is deliberate: given the app server and DB server are on separate boxes, Windows Integrated Authentication would hit the classic Kerberos "double-hop" problem (the app server can't forward a client's Windows credentials on to a second server without constrained delegation configured in AD) — SQL auth avoids that entirely.

### 2.3 Network access

- Enable **TCP/IP** in SQL Server Configuration Manager.
- Firewall: allow inbound **1433** only from the app server's IP, not `0.0.0.0/0`.
  - PS 5.1+: `New-NetFirewallRule -DisplayName "SQL from app server" -Direction Inbound -LocalPort 1433 -Protocol TCP -Action Allow -RemoteAddress <app-server-ip>`
  - PS2 / no WMF (Server 2008 R2 default): `netsh advfirewall firewall add rule name="SQL from app server" dir=in action=allow protocol=TCP localport=1433 remoteip=<app-server-ip>`

### 2.4 Backups

Set up scheduled backups (full daily, differential every few hours, log backup every 5–15 min) before you have production data to lose. Test a restore at least once.

### 2.5 The connection string — `Encrypt`/`TrustServerCertificate`

`Microsoft.Data.SqlClient` defaults to `Encrypt=True` since v4.0. Against a SQL Server 2012 instance without a properly configured certificate, this fails outright unless you also set `TrustServerCertificate=True` (encrypts the connection using SQL Server's own self-signed cert, skipping certificate-chain validation — the pragmatic choice here) or `Encrypt=False` if the link between the two servers is a genuinely trusted, private network segment. The connection string in `appsettings.json`/user-secrets already includes `Encrypt=True;TrustServerCertificate=True;` — keep it, or make the tradeoff deliberately if you change it.

### 2.6 Critical: TLS 1.2 on Windows Server 2008 R2

**This is the step most likely to cause a silent connection failure if skipped.** Windows Server 2008 R2 does not support TLS 1.2 out of the box. Even with `TrustServerCertificate=True` set correctly, the encrypted handshake itself can fail if the OS/SQL Server can't negotiate TLS 1.2 with a modern client driver. Before testing connectivity from the app server:

1. Install **KB3135244** (TLS 1.2 support for SQL Server 2008/2008 R2/2012/2014).
2. Enable TLS 1.2 at the OS level (Schannel registry keys) — see [Microsoft's guidance](https://learn.microsoft.com/en-us/mem/configmgr/core/plan-design/security/enable-tls-1-2-client).
3. Reboot.
4. Verify: connect with `sqlcmd` from the app server itself, and separately confirm the actual application connection succeeds (a successful `sqlcmd` connection doesn't always guarantee `Microsoft.Data.SqlClient`'s specific negotiation succeeds — test both).

---

## 3. App server: Windows Server 2016 + IIS

### 3.1 Install prerequisites

- **IIS** — enable via *Server Manager → Add Roles and Features → Web Server (IIS)*.
- **[.NET 10 Hosting Bundle](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)** — installs the ASP.NET Core Module and the .NET runtime on the server. This is the *only* extra install needed for hosting (no ARR, no URL Rewrite, no NSSM).

### 3.2 Get the code onto the server and publish

```cmd
cd C:\inetpub\wwwroot\xds-ghc-verification
git pull origin main
dotnet publish src\XDS_GHC_Verification -c Release -o .
```

`dotnet publish` auto-generates a correct `web.config` with the `<aspNetCore>` handler entry (`hostingModel="inprocess"`) — no manual authoring needed for the standard case.

### 3.3 Production secrets

Never publish real secrets in `appsettings.json`. Use one of:

- `appsettings.Production.json` next to the published DLL (gitignored, created directly on the server, never committed), or
- Environment variables set via `web.config`'s `<aspNetCore><environmentVariables>` block, e.g.:
  ```xml
  <aspNetCore processPath="dotnet" arguments=".\XDS_GHC_Verification.dll" hostingModel="inprocess">
    <environmentVariables>
      <environmentVariable name="ServiceAuth__ApiKey" value="<real key>" />
      <environmentVariable name="ConnectionStrings__Verification" value="Server=<db-server>;Database=XdsGhcVerification;User Id=xds_ghc_svc;Password=<real password>;Encrypt=True;TrustServerCertificate=True;" />
    </environmentVariables>
  </aspNetCore>
  ```
  (Double underscores `__` are ASP.NET Core's convention for nested config sections via environment variables.)

**Protect whichever file holds secrets.** Restrict NTFS permissions to the IIS application pool identity and administrators only:

```powershell
icacls "C:\inetpub\wwwroot\xds-ghc-verification\appsettings.Production.json" /inheritance:r
icacls "C:\inetpub\wwwroot\xds-ghc-verification\appsettings.Production.json" /grant "IIS AppPool\xds-ghc-verification:R"
icacls "C:\inetpub\wwwroot\xds-ghc-verification\appsettings.Production.json" /grant "Administrators:F"
```

Since the ASP.NET Core Module runs the app **as the app pool identity** (unlike a Python/NSSM setup, which can default to a different account like Local System), there's no separate service-account permission to account for here.

### 3.4 Configure the IIS site

1. In IIS Manager, create a new site pointed at `C:\inetpub\wwwroot\xds-ghc-verification`.
2. Set the site's **Application Pool** to *No Managed Code* — the ASP.NET Core Module handles the .NET runtime itself, IIS's own CLR isn't involved.
3. Bind **HTTPS (443)** with your production certificate. Add an HTTP→HTTPS redirect rule via `app.UseHttpsRedirection()` (already in `Program.cs`) or an IIS-level rule.
4. Grant the app pool identity read+execute on the published folder (usually already covered by default `C:\inetpub\wwwroot` permissions, but explicit costs nothing):
   ```powershell
   icacls "C:\inetpub\wwwroot\xds-ghc-verification" /grant "IIS AppPool\xds-ghc-verification:(OI)(CI)RX" /T
   ```
5. Start the site. Confirm:
   ```cmd
   curl https://your-domain.example.com/health
   ```

> **Bitness check**: in-process hosting requires the app pool's bitness to match the installed .NET runtime architecture (x64, typically) — a mismatch is the most common cause of a `500.30` error on first deploy.

> **If you bind without a hostname** (raw `*:80`/`*:443`), it can collide with "Default Web Site". Bind to your real production hostname to avoid this — the normal case for a public-facing site.

### 3.5 The admin console frontend (`frontend/`)

This is a separate static app (Vite build output — plain HTML/JS/CSS) from the API above; it isn't published by `dotnet publish` and isn't covered by the IIS steps above. `npm run build` in `frontend/` produces a `dist/` folder that can be served by any static host (a second IIS site with the static-content handler, a separate origin behind your reverse proxy, cloud static hosting, etc.) — pick one when you're ready to deploy it and point `VITE_API_BASE_URL` (baked in at build time) at the real API origin. Camera capture (`getUserMedia`, used by the Test API tab) requires the console itself be served over HTTPS once it's not on `localhost`. Not designed further here — flagging so it isn't missed at deploy time.

---

## 4. TLS end to end

- **Client → IIS**: real certificate, TLS 1.2+ only.
- **IIS → app**: in-process — there's no network hop at all; the app runs inside the same `w3wp.exe` process IIS uses.
- **App → SQL Server**: `Encrypt=True;TrustServerCertificate=True` in the connection string, **and** TLS 1.2 enabled on the Server 2008 R2 box per §2.6.
- **App → upstream Selfie API**: already HTTPS.

---

## 5. Getting code from GitHub onto the app server

### Option A — Manual pull + republish

```cmd
cd C:\inetpub\wwwroot\xds-ghc-verification
git pull origin main
dotnet publish src\XDS_GHC_Verification -c Release -o .
```
```powershell
Import-Module WebAdministration
Restart-WebAppPool -Name "xds-ghc-verification"
```

Recycling the app pool is what restarts the app — there's no separate service.

### Option B — GitHub Actions with a self-hosted runner

Install a [self-hosted runner](https://docs.github.com/en/actions/hosting-your-own-runners) on the app server, then add `.github/workflows/deploy.yml`:

```yaml
name: Deploy to production
on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: self-hosted
    steps:
      - uses: actions/checkout@v4
      - name: Publish
        run: dotnet publish src\XDS_GHC_Verification -c Release -o C:\inetpub\wwwroot\xds-ghc-verification
        shell: cmd
      - name: Recycle app pool
        run: powershell -Command "Import-Module WebAdministration; Restart-WebAppPool -Name 'xds-ghc-verification'"
        shell: cmd
      - name: Health check
        run: curl -f https://your-domain.example.com/health
        shell: cmd
```

Set branch protection on `main` if you go this route — a merge is now a deploy.

---

## 6. Rollback

- **Git-based**: `git log` to find the last-known-good commit, `git checkout <sha>`, republish, recycle the app pool. Tag releases (`git tag v1.0.0`).
- **Database**: schema changes should be additive (see the caution in §2.1) so a code rollback never needs a matching database rollback.

---

## 7. Smoke-test checklist

```bash
# 1. Health check — no auth
curl https://your-domain.example.com/health

# 2. Login works
curl -X POST https://your-domain.example.com/api/v1/auth/login \
     -H "Content-Type: application/json" \
     -d '{"username": "<prod-username>", "password": "<prod-password>"}'

# 3. A protected endpoint rejects a missing key
curl -i https://your-domain.example.com/api/v1/proxy/ping   # expect 401

# 4. A protected endpoint accepts the key from step 2
curl -i https://your-domain.example.com/api/v1/proxy/ping \
     -H "X-API-Key: <key from step 2>"
```

---

## 8. Ongoing operations

- **Logs**: `stdoutLogEnabled` in the generated `web.config` is `false` by default for in-process hosting (ANCM captures startup-critical failures separately) — enable it (`stdoutLogEnabled="true"`, `stdoutLogFile=".\logs\stdout.log"`, create the `logs` folder) if you need routine app logs captured to disk; otherwise rely on `ILogger` sinks (e.g. a file provider or centralized logging) configured in `Program.cs`.
- **Monitoring**: point your uptime tool at `/health`. No auth required by design.
- **Secret rotation**: rotate `ServiceAuth:ApiKey` in the production secrets file/environment variables, then recycle the app pool — any client using the old key gets `403` until they log in again for a new one. Rotating `Jwt:SigningKey` invalidates every currently-issued admin-console JWT immediately (everyone has to log in again) but does **not** affect `X-API-Key` clients at all — the two secrets are independent by design.
- **Capacity**: in-process hosting runs one worker process per app pool. Scale via IIS's own worker-process/app-pool settings, or by adding more app server instances behind a load balancer.
