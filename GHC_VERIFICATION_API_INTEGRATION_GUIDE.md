# XDS GHC Verification API — Integration Guide

This is the single guide you need to integrate with the XDS GHC Verification API. It covers everything: authentication, every endpoint available to you, request/response formats, error handling, and working code samples.

Integrate using only the base URL and endpoints described in this guide.

---

## 1. Base URL

All requests in this guide are made over HTTPS to:

```
https://online.xdsdata.com/xdsghc/
```

Every path below (e.g. `/health`, `/api/v1/auth/login`) is relative to this base URL. For example, the health check is:

```
GET https://online.xdsdata.com/xdsghc/health
```

---

## 2. What's available to you

| # | Endpoint | Method | Auth | Purpose |
|---|---|---|---|---|
| 1 | `/health` | GET | None | Confirm the service is up |
| 2 | `/api/v1/auth/login` | POST | Username/password | Exchange your credentials for your API key |
| 3 | `/api/v1/selfie/verification/kyc/face` | POST | `X-API-Key` | Full identity verification (selfie vs. Ghana Card) |
| 4 | `/api/v1/selfie/verification/yes_no/face` | POST | `X-API-Key` | Simple match/no-match verification |
| 5 | `/api/v1/proxy/{resource-path}` | GET/POST/PUT/PATCH/DELETE | `X-API-Key` | Any additional resource arranged with your integration contact |

You'll typically only need #1–#4. #5 only applies if your integration contact has told you about specific resource paths beyond identity verification.

---

## 3. Step 1 — Check the service is reachable (optional)

No credentials needed. Useful for uptime monitoring.

```bash
curl https://online.xdsdata.com/xdsghc/health
```

**Response — `200 OK`:**

```json
{
  "status": "ok",
  "service": "XDS GHC Verification Service",
  "version": "1.0.0",
  "environment": "Production"
}
```

---

## 4. Step 2 — Authenticate and get your API key

Every other endpoint requires an `X-API-Key` header. You get that key by exchanging the username and password issued to you by your integration contact — you don't manage the key as a separate secret you have to request up front.

```bash
curl -X POST https://online.xdsdata.com/xdsghc/api/v1/auth/login \
     -H "Content-Type: application/json" \
     -d '{"username": "your-username", "password": "your-password"}'
```

**Response — `200 OK`:**

```json
{
  "apiKey": "your-service-api-key",
  "tokenType": "apikey",
  "token": "eyJhbGciOi...",
  "role": "Client",
  "expiresAtUtc": "2026-08-28T09:15:00Z"
}
```

For a standard API integration, only `apiKey` matters — `token`, `role`, and `expiresAtUtc` are issued for the admin web console and can be ignored.

**Failure — `401 Unauthorized`:**

```json
{ "detail": "Invalid username or password." }
```

---

## 5. Step 3 — Use your API key

Attach the `apiKey` value from Step 2 as the `X-API-Key` header on every request to endpoints #3, #4, and #5 above.

```
X-API-Key: your-service-api-key
```

- **Store it like a database credential.** Keep it in your backend configuration/secrets store — never in a mobile app, browser bundle, or any client-side code.
- **It's one shared key per integration**, not per end-user or per-request.
- If you suspect it's been exposed, contact your integration contact to have it rotated. Rotation invalidates the old key immediately, so re-run Step 2 to pick up the new one.
- Missing the header → `401 Unauthorized`. Sending the wrong value → `403 Forbidden`.

---

## 6. Step 4 — Call the endpoint you need

### 6.1 KYC Face Verification

`POST /api/v1/selfie/verification/kyc/face`

Verifies a live selfie against a person's Ghana Card and, on a match, returns their verified identity details. Use this for onboarding / know-your-customer flows.

**Request body:**

| Field | Type | Required | Notes |
|---|---|---|---|
| `pinNumber` | string | Yes | Ghana Card PIN, format `GHA-XXXXXXXXX-X` |
| `image` | string | Yes | Base64-encoded PNG of a live selfie. A `data:image/png;base64,` prefix is fine — it's stripped automatically. |
| `dataType` | string | No | Defaults to `"PNG"` — currently the only supported format |
| `userID` | string | No | Optional caller identifier — most integrations can omit this |

**Image constraints:**
- Must be PNG.
- Recommended minimum 640×480px for reliable matching.
- **Maximum 1MB after Base64 decoding.** A well-lit, front-facing photo near 640×480 comfortably stays under this. Larger images are rejected — see [§7](#7-error-handling); do not upscale a smaller image to compensate.
- Capture the photo live from the device camera at the point of verification rather than accepting a gallery/file-picker image — this materially improves match accuracy.

```bash
curl -X POST https://online.xdsdata.com/xdsghc/api/v1/selfie/verification/kyc/face \
     -H "X-API-Key: your-service-api-key" \
     -H "Content-Type: application/json" \
     -d '{"pinNumber": "GHA-123456789-0", "image": "<base64-png>"}'
```

**Response — `200 OK`, match found:**

```json
{
  "N_success": true,
  "N_StatusCode": "00",
  "N_CodeMessage": "Success",
  "data": {
    "Tranx_NID": "a1b2c3d4-...",
    "ShortXGUID": "9F3A2B1C",
    "requestTimestamp": "2026-08-27T09:15:00Z",
    "responseTimestamp": "2026-08-27T09:15:02Z",
    "N_verified": "YES",
    "N_userID": "XDS_Ver",
    "N_center": "HEAD_OFFICE",
    "person": {
      "IDNo": "GHA-123456789-0",
      "N_cardID": "...",
      "N_cardValidFrom": "2020-01-01",
      "N_cardValidTo": "2030-01-01",
      "N_surname": "DOE",
      "N_forenames": "JANE",
      "N_nationality": "GHANAIAN",
      "N_birthDate": "1990-05-14",
      "N_gender": "F",
      "N_birthCountry": "GHANA",
      "N_birthDistrict": "...",
      "N_birthRegion": "...",
      "N_birthTown": "...",
      "addresses": [
        {
          "N_type": "RESIDENTIAL",
          "N_town": "...",
          "N_community": "...",
          "N_postalCode": "...",
          "N_countryName": "GHANA",
          "N_districtName": "...",
          "N_region": "...",
          "N_addressDigital": "GA-123-4567",
          "gpsAddressDetails": {
            "N_gpsName": "...",
            "N_region": "...",
            "N_district": "...",
            "N_area": "...",
            "N_street": "...",
            "N_longitude": "-0.1870",
            "N_latitude": "5.6037"
          }
        }
      ],
      "X_addressHistory": [
        {
          "X_upDateDate": "2024-03-01",
          "X_upDateOnDate": "2024-03-01",
          "X_address1": "...",
          "X_address2": "...",
          "X_address3": "...",
          "X_address4": "...",
          "X_addressTypeInd": "..."
        }
      ],
      "contact": {
        "N_email": "jane.doe@example.com",
        "phoneNumbers": [
          { "N_type": "MOBILE", "N_phoneNumber": "0244000000", "N_Provider": "MTN" }
        ]
      },
      "occupations": [
        { "N_name": "..." }
      ],
      "biometricFeed": {
        "face": { "N_PtotoType": "PNG", "N_PtotoData": "<base64>" }
      },
      "binaries": [
        { "N_type": "SIGNATURE", "N_PtotoType": "JPEG", "N_PtotoData": "<base64>" }
      ]
    }
  }
}
```

Notes on this response:
- These field names (`N_...`, `X_...`) are the stable contract to build against — don't expect them to change between calls.
- `X_addressHistory` may be an empty array if no history was found — this is normal and does not indicate a failed verification.
- Any field a given match didn't produce is simply omitted from the JSON rather than sent as `null` — don't assume every field above is always present.
- Check **`N_StatusCode`** to determine the verification result (see [§6.4](#64-checking-the-verification-result) below) — do not look for a top-level `code` field, KYC doesn't use one.

**Response — verification failure (e.g. no match):**

```json
{
  "detail": {
    "X_success": false,
    "X_code": "01",
    "X_msg": "No match found",
    "X_data": {
      "X_userID": "XDS_Ver"
    }
  }
}
```

The exact shape of `detail` varies with the failure — treat it as "some JSON object with `X_`-prefixed keys" rather than a fixed schema.

### 6.2 YES/NO Face Verification

`POST /api/v1/selfie/verification/yes_no/face`

Same request body as [§6.1](#61-kyc-face-verification). Returns a simple match/no-match result instead of full identity details — use this for lightweight re-authentication or step-up checks where you don't need the person's data on file.

```bash
curl -X POST https://online.xdsdata.com/xdsghc/api/v1/selfie/verification/yes_no/face \
     -H "X-API-Key: your-service-api-key" \
     -H "Content-Type: application/json" \
     -d '{"pinNumber": "GHA-123456789-0", "image": "<base64-png>"}'
```

**Response — `200 OK`:**

```json
{
  "code": "00",
  "data": {
    "verified": "YES"
  }
}
```

Check the top-level **`code`** field to determine the result (see [§6.4](#64-checking-the-verification-result)).

### 6.3 Additional Resources (only if applicable to your integration)

`{METHOD} /api/v1/proxy/{resource-path}`

For any resource beyond identity verification that your integration contact has arranged for you.

- **Methods supported:** `GET`, `POST`, `PUT`, `PATCH`, `DELETE`.
- **Path:** whatever you put after `/api/v1/proxy/` is the resource being called — your integration contact tells you which paths apply to you.
- **Query string and request body** are sent through unchanged.
- **Headers recognized:** only `Content-Type`, `Accept`, `X-Request-Id`, and `X-Correlation-Id`. Any other header you send (custom headers, cookies) is ignored.
- **A successful call always returns `200`** — check the response body for the specific result rather than relying solely on the HTTP status.

```bash
curl -X GET https://online.xdsdata.com/xdsghc/api/v1/proxy/example-resource \
     -H "X-API-Key: your-service-api-key"
```

### 6.4 Checking the verification result

A `200 OK` HTTP status only means the *request* succeeded — you still need to check a business-level result field inside the body:

| Endpoint | Field to check | On success |
|---|---|---|
| KYC (§6.1) | `N_StatusCode` (top level) | `"00"` |
| YES/NO (§6.2) | `code` (top level) | `"00"` |

| Code | Meaning |
|---|---|
| `00` | Successful verification |
| `01` | Unsuccessful verification — no match |
| `02` | Invalid data submitted |
| `03` | Flagged on a watch list |
| `04` | Internal error during verification |

A `200`/business-code combination other than `00` is not a technical failure — it's a normal business-level result (no match, watch-list hit, etc.) and belongs in your normal flow, not your error handling.

---

## 7. Error Handling

| HTTP status | Meaning | What to do |
|---|---|---|
| `401` | Missing API key (or, on login, wrong username/password) | Re-authenticate via [§4](#4-step-2--authenticate-and-get-your-api-key) |
| `403` | Invalid API key | Confirm you're sending the current key; contact support if it should be valid |
| `422` | Identity verification endpoints only — invalid Base64, or image over 1MB | Fix the image/request and retry — do not retry unchanged |
| `502` | The service is temporarily unavailable | Retry after a short delay with exponential backoff |
| `504` | The request took too long to process | Retry after a short delay with exponential backoff |

Error responses carry a `detail` field. On the identity verification endpoints it may be a plain string or a structured object (see [§6.1](#61-kyc-face-verification)); on additional resources (§6.3) it may be a plain string or an arbitrary structured object — inspect it rather than assuming a fixed shape.

```json
{ "detail": "image is 1400000 bytes; the upstream API requires it to be under 1048576 bytes (1MB)" }
```

---

## 8. Code Samples

**Node.js — full flow (login once, then verify):**

```javascript
const BASE_URL = "https://online.xdsdata.com/xdsghc";

async function login(username, password) {
  const res = await fetch(`${BASE_URL}/api/v1/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  });
  if (!res.ok) throw new Error(`Login failed: ${res.status}`);
  const { apiKey } = await res.json();
  return apiKey; // store this — don't log in again per-request
}

async function verifyKyc(apiKey, pinNumber, base64Png) {
  const res = await fetch(`${BASE_URL}/api/v1/selfie/verification/kyc/face`, {
    method: "POST",
    headers: { "X-API-Key": apiKey, "Content-Type": "application/json" },
    body: JSON.stringify({ pinNumber, image: base64Png }),
  });
  const result = await res.json();
  if (!res.ok) {
    // technical error — 401/403/422/502/504
    throw new Error(typeof result.detail === "string" ? result.detail : JSON.stringify(result.detail));
  }
  if (result.N_StatusCode !== "00") {
    // business result — no match, watch-list, etc. Not an exception.
    return { matched: false, code: result.N_StatusCode };
  }
  return { matched: true, person: result.data.person };
}
```

**Python — full flow:**

```python
import requests

BASE_URL = "https://online.xdsdata.com/xdsghc"

def login(username, password):
    resp = requests.post(f"{BASE_URL}/api/v1/auth/login", json={"username": username, "password": password})
    resp.raise_for_status()
    return resp.json()["apiKey"]  # store this — don't log in again per-request

def verify_yes_no(api_key, pin_number, base64_png):
    resp = requests.post(
        f"{BASE_URL}/api/v1/selfie/verification/yes_no/face",
        headers={"X-API-Key": api_key},
        json={"pinNumber": pin_number, "image": base64_png},
    )
    result = resp.json()
    if not resp.ok:
        raise RuntimeError(result.get("detail"))
    return result["code"] == "00" and result["data"]["verified"] == "YES"
```

---

## 9. Best Practices

- **Integrate using only the base URL and endpoints in this guide.**
- **Log in once, reuse the API key.** Don't call `/api/v1/auth/login` per verification request — store the returned `apiKey` server-side and reuse it until you rotate it.
- **Distinguish technical errors from business results.** A `4xx`/`5xx` status means something went wrong with the request or service; a `200` with a non-`"00"` result code means the verification itself didn't pass — handle these differently in your flow and logging.
- **Retry `502`/`504` with backoff**, but don't retry `422` without changing the request — it will fail again unchanged.
- **Never cache or store the raw selfie image** beyond what's needed to make the request.
- **Treat your API key like a database credential** — one shared secret per integration, stored server-side, rotated if ever exposed.
- **Don't assume a fixed error shape on additional resources (§6.3)** — always check whether `detail` is a string or an object before displaying or logging it.

---

## 10. Support

For credentials, integration questions, which additional resource paths are available to you, or to request a rate limit increase, contact **[your integration support contact]**.
