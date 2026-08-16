# XDS GHC Verification Service

A production-ready **ASP.NET Core (.NET 10) Web API** that acts as a secure, authenticated proxy to a private upstream REST API — including dedicated, typed endpoints for the **Selfie Verification (NIA face-match) API**. Built for deployment on **IIS** via the **ASP.NET Core Module** (in-process hosting) — no bridging module, no separate service to manage.

> **Deploying this to production?** See [`PRODUCTION_DEPLOYMENT.md`](PRODUCTION_DEPLOYMENT.md) for the full app-server + database-server runbook.

---

## Features

| Feature | Detail |
|---|---|
| 🔐 Incoming auth | `X-API-Key` header required on all endpoints (except `/health` and login) |
| 👤 Per-user accounts | Real username/password accounts (`ProxyUsers`) with Admin/Standard roles — login issues both the shared `X-API-Key` and a personal JWT |
| 🖥️ Admin console | Separate `frontend/` web app to manage accounts, browse the audit log, and test the live API with a camera-captured photo |
| 🪪 Selfie Verification API | Typed KYC and YES/NO face verification endpoints — merchant key injected server-side |
| 🔑 Upstream auth (generic proxy) | Configurable — API key, Bearer token, Basic Auth, or none |
| 🔄 Full HTTP method support | GET, POST, PUT, PATCH, DELETE (generic proxy) |
| 📋 Audit logging | Every request logged to SQL Server — endpoint, username, timestamp, response, found Y/N |
| 📖 Auto-docs | OpenAPI document at `/openapi/v1.json` (dev only) |
| 💚 Health check | `GET /health` — no auth required |
| 🪟 IIS ready | `dotnet publish` auto-generates `web.config` for the ASP.NET Core Module |

---

## Quick Start

### 1. Prerequisites

- .NET 10 SDK
- SQL Server (any edition/version — SQL Server 2012+ supported by `Microsoft.Data.SqlClient`)

### 2. Provision the database

```cmd
sqlcmd -S <server> -E -i sql\001_create_xds_ghc_verification_database.sql
sqlcmd -S <server> -d XdsGhcVerification -E -i sql\002_add_proxy_users_table.sql
```

Edit `001` first and replace `<CHANGE_ME_STRONG_PASSWORD>` with a freshly generated password — never commit a real one. `001` creates the `XdsGhcVerification` database, the `ApiTransactionLog` table, and a least-privilege `xds_ghc_svc` login (`SELECT`/`INSERT` only). `002` is additive — safe to re-run — and adds the `ProxyUsers` table (individual login accounts), granting `xds_ghc_svc` full CRUD on just that table.

### 3. Configure local secrets

This project uses [`dotnet user-secrets`](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) instead of a `.env` file — secrets never touch the repo or the filesystem in a committable location.

```cmd
cd src\XDS_GHC_Verification
dotnet user-secrets set "ConnectionStrings:Verification" "Server=<server>;Database=XdsGhcVerification;User Id=xds_ghc_svc;Password=<the password from step 2>;Encrypt=True;TrustServerCertificate=True;"
dotnet user-secrets set "ServiceAuth:ApiKey" "<a real generated key>"
dotnet user-secrets set "ServiceAuth:AuthUsername" "<username>"
dotnet user-secrets set "ServiceAuth:AuthPassword" "<password>"
dotnet user-secrets set "Selfie:MerchantKey" "<real vendor merchant key>"
dotnet user-secrets set "Selfie:UserId" "<real vendor user id>"
dotnet user-secrets set "Jwt:SigningKey" "<random string, at least 32 characters>"
```

`ServiceAuth:AuthUsername`/`AuthPassword` double as the seed account: on first run, if `ProxyUsers` is empty, one `Admin` row is created from those two values so there's a way to log in before any accounts exist. After that, real accounts in the database take over — manage them from the admin console (step 5) or via `POST/GET/PUT/DELETE /api/v1/users` directly.

### 4. Run the API locally

```cmd
dotnet run
```

- API: `https://localhost:<port>` (see console output for the exact port, or set `--urls`)
- OpenAPI document: `/openapi/v1.json` (dev only)
- Health: `/health`

### 5. Run the admin console (optional)

```cmd
cd frontend
npm install
npm run dev
```

Open the printed `http://localhost:5173` URL and log in with the seed Admin account from step 3. `frontend/.env.development` points it at `http://localhost:5068` by default — change `VITE_API_BASE_URL` if your API runs on a different port. See [Admin Console](#admin-console) below.

---

## Usage

All endpoints require the `X-API-Key` header. Clients obtain that key by logging in with a personal username/password (a `ProxyUsers` account — see [Admin Console](#admin-console)) rather than being handed the key directly.

### Login

```bash
curl -X POST https://localhost:5001/api/v1/auth/login \
     -H "Content-Type: application/json" \
     -d '{"username": "your-username", "password": "your-password"}'
```

Response:

```json
{
  "apiKey": "the-service-x-api-key",
  "tokenType": "apikey",
  "token": "<jwt>",
  "role": "Admin",
  "expiresAtUtc": "2026-01-01T00:00:00Z"
}
```

Use the returned `apiKey` as the `X-API-Key` header on every proxy/selfie endpoint below — this part is unchanged, and every account gets the same shared key. `token` is a separate personal JWT, only needed for the admin console's own endpoints (`/api/v1/users`, `/api/v1/transactions`) — plain API clients (curl, Postman, another service) can ignore it entirely.

### Selfie Verification API

Two dedicated, typed endpoints wrap the upstream NIA face-match API. The `center`, `userID`, and `merchantKey` fields are injected server-side from configuration — callers only supply `pinNumber` and `image` (Base64-encoded PNG, 640x480 minimum, under 1MB).

```bash
# KYC face verification — returns full matched person details on success
curl -X POST https://localhost:5001/api/v1/selfie/verification/kyc/face \
     -H "X-API-Key: your-secret-key-for-clients" \
     -H "Content-Type: application/json" \
     -d '{"pinNumber": "GHA-123456789-0", "image": "<base64-png>"}'

# YES/NO face verification — returns a simple verified/unverified result
curl -X POST https://localhost:5001/api/v1/selfie/verification/yes_no/face \
     -H "X-API-Key: your-secret-key-for-clients" \
     -H "Content-Type: application/json" \
     -d '{"pinNumber": "GHA-123456789-0", "image": "<base64-png>"}'
```

Response `code` field (from the upstream API):

| Code | Meaning |
|---|---|
| `00` | Successful verification |
| `01` | Unsuccessful verification (reason in `data`) |
| `02` | Invalid data |
| `03` | On NIA watch list |
| `04` | Internal server error |

Non-2xx upstream responses (e.g. face not detected) are passed through with the same HTTP status code and body.

### Generic proxy

Any other upstream path can still be reached through the catch-all proxy:

```bash
curl http://localhost:5000/api/v1/proxy/users \
     -H "X-API-Key: your-secret-key-for-clients"

curl -X POST http://localhost:5000/api/v1/proxy/orders \
     -H "X-API-Key: your-secret-key-for-clients" \
     -H "Content-Type: application/json" \
     -d '{"item": "widget", "qty": 3}'
```

---

## Admin Console

A separate frontend app (`frontend/`, Vite + React + TypeScript) for managing accounts, browsing the audit log, and testing the live API without curl. It's a plain client of this API — see step 5 above to run it. It authenticates against the same `/api/v1/auth/login` endpoint, then uses the returned `token` (JWT) as `Authorization: Bearer <token>` for its own endpoints, and the returned `apiKey` as `X-API-Key` when it calls the selfie/proxy endpoints on your behalf.

- **Users tab** (Admin role only) — create, edit (role/active), reset password, and delete `ProxyUsers` accounts via `/api/v1/users`. An Admin cannot demote, deactivate, or delete their own account, and the last active Admin can't be removed — both guarded server-side.
- **Transactions tab** — browse `ApiTransactionLog` via `/api/v1/transactions` (paginated, filterable by username/endpoint). Admins see the full row including the Ghana Card PIN and NIA response payload; Standard-role accounts see a redacted summary with the PIN and payload withheld.
- **Test API tab** — exercises the real endpoints above as an authenticated caller. The Selfie Verification panel captures a photo **live from the browser camera only** (no file-upload option) at 640×480, checks it's under the vendor's 1MB cap client-side before sending, and posts it to the real KYC/YES-NO endpoints with a PIN you type in. A Generic Proxy panel does the same for the catch-all proxy (method/path/JSON body).

Every call made through this console (not just login) is attributed to the real logged-in username in `ApiTransactionLog.Username`, instead of the one generic `ServiceAuth:AuthUsername` value that external `X-API-Key`-only clients still show up as.

---

## IIS Deployment

1. Install the [.NET 10 Hosting Bundle](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) on the IIS server (not the SDK).
2. `dotnet publish -c Release -o publish` — this auto-generates a working `web.config` with the ASP.NET Core Module entry.
3. Copy the `publish` output to your IIS site root (e.g. `C:\inetpub\wwwroot\xds-ghc-verification`).
4. Set the real secrets on the server — either `appsettings.Production.json` (gitignored, never commit) or `<environmentVariables>` in `web.config`. See [`PRODUCTION_DEPLOYMENT.md`](PRODUCTION_DEPLOYMENT.md).
5. Point IIS at the site folder, set the app pool to *No Managed Code* — IIS runs .NET in-process via the ASP.NET Core Module, no reverse proxy needed.

---

## Project Structure

```
XDS_GHC_Verification/
├── XDS_GHC_Verification.slnx
├── src/
│   └── XDS_GHC_Verification/
│       ├── Program.cs                # DI, JWT auth, middleware pipeline, OpenAPI, ProxyUsers seed
│       ├── appsettings.json          # Safe-to-commit config structure
│       ├── Controllers/
│       │   ├── HealthController.cs
│       │   ├── AuthController.cs     # POST /api/v1/auth/login (validates against ProxyUsers)
│       │   ├── SelfieController.cs   # KYC + YES/NO verification endpoints
│       │   ├── ProxyController.cs    # Catch-all proxy
│       │   ├── ProxyUsersController.cs   # Admin-only account management
│       │   └── TransactionsController.cs # Audit log viewer (role-redacted)
│       ├── Auth/
│       │   ├── ApiKeyAuthFilter.cs   # X-API-Key validation
│       │   └── HttpContextExtensions.cs  # JWT-aware audit-log attribution
│       ├── Services/
│       │   ├── UpstreamClient.cs             # Upstream HTTP client
│       │   ├── SelfieVerificationService.cs  # Injects merchantKey/center/userID
│       │   ├── AuditLogService.cs            # Dapper insert into ApiTransactionLog
│       │   ├── ProxyUserService.cs           # Dapper CRUD over ProxyUsers
│       │   ├── ProxyUserSeeder.cs            # Bootstraps the first Admin account
│       │   ├── JwtTokenService.cs            # Issues the per-user JWT
│       │   └── TransactionQueryService.cs    # Paginated audit-log reads
│       ├── Models/
│       │   ├── LoginModels.cs
│       │   ├── SelfieModels.cs
│       │   ├── ProxyUserModels.cs
│       │   └── TransactionModels.cs
│       ├── Options/                  # Strongly-typed config sections (incl. JwtOptions)
│       └── Utils/
│           ├── JsonRedactor.cs       # Strips image fields before logging
│           └── SecureCompare.cs      # Constant-time credential comparison
├── frontend/                          # Admin console — Vite + React + TypeScript, calls the API above
│   └── src/
│       ├── api/client.ts, auth/, pages/, components/
├── tests/
│   └── XDS_GHC_Verification.Tests/    # xUnit — unit + WebApplicationFactory integration tests
├── sql/
│   ├── 001_create_xds_ghc_verification_database.sql
│   └── 002_add_proxy_users_table.sql
├── .gitignore
├── README.md
└── PRODUCTION_DEPLOYMENT.md
```

---

## Configuration Reference

| Section | Key | Description |
|---|---|---|
| `ServiceAuth` | `ApiKey` | Key clients use to call this service (returned by login) |
| `ServiceAuth` | `AuthUsername` / `AuthPassword` | Credentials clients log in with |
| `Upstream` | `BaseUrl` | Base URL of the upstream API |
| `Upstream` | `AuthType` | `apikey`, `bearer`, `basic`, or `none` |
| `Selfie` | `MerchantKey` / `Center` / `UserId` | Injected into every selfie verification request |
| `Cors` | `AllowedOrigins` | Comma-separated allowed origins |
| `Jwt` | `SigningKey` | Signs the per-user JWT (≥32 chars) — independent of `ServiceAuth:ApiKey`, rotate separately |
| `Jwt` | `Issuer` / `Audience` / `ExpiryMinutes` | JWT validation parameters and token lifetime (default 480 = 8h) |
| `ConnectionStrings` | `Verification` | SQL Server connection string for the audit log and `ProxyUsers` |

All of the above should be set via `dotnet user-secrets` locally, or `appsettings.Production.json` / environment variables on a real server — never committed to `appsettings.json`.
