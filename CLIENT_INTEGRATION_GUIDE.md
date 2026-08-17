# GHC Verification API — Integration Guide

This guide covers everything you need to integrate identity verification into your systems: authentication, the two verification endpoints, request/response formats, and error handling.

---

## 1. Overview

The GHC Verification API lets you verify a person's identity against their Ghana Card in real time, using a live selfie photo. Two verification modes are available:

| Mode | Returns | Typical use |
|---|---|---|
| **KYC Face Verification** | Full matched identity details | Onboarding / know-your-customer flows |
| **YES/NO Face Verification** | A simple match / no-match result | Lightweight re-authentication, step-up checks |

All requests are made over HTTPS to:

```
https://<YOUR_API_BASE_URL>
```

Replace `<YOUR_API_BASE_URL>` with the base URL provided by your integration contact.

---

## 2. Authentication

Every request (other than login) requires an `X-API-Key` header. You obtain that key by exchanging the username and password issued to you for it — you never handle it as a separate secret you have to request.

### 2.1 Log in

```bash
curl -X POST https://<YOUR_API_BASE_URL>/api/v1/auth/login \
     -H "Content-Type: application/json" \
     -d '{"username": "your-username", "password": "your-password"}'
```

**Response — `200 OK`:**

```json
{
  "apiKey": "your-service-api-key",
  "tokenType": "apikey"
}
```

> The response may include a couple of additional fields beyond what's shown above — you can safely ignore anything other than `apiKey` for a standard integration.

### 2.2 Use your key

Attach the returned `apiKey` as the `X-API-Key` header on every verification request below.

**Store it like any other server-side secret:**
- Keep it in your backend configuration/secrets store — never in a mobile app, browser bundle, or any client-side code.
- If you suspect it's been exposed, contact your integration contact to have it rotated. Rotation invalidates the old key immediately.

---

## 3. Making a Verification Request

Both verification endpoints accept the same request body:

| Field | Type | Required | Notes |
|---|---|---|---|
| `pinNumber` | string | Yes | The Ghana Card PIN, in the format `GHA-XXXXXXXXX-X` |
| `image` | string | Yes | Base64-encoded PNG. Minimum 640×480px, **maximum 1MB decoded size** |
| `dataType` | string | No | Defaults to `"PNG"` — currently the only supported format |

**Image capture guidance:**
- Capture the photo live from the device camera at the point of verification, rather than accepting an existing photo from a gallery or file picker — this produces meaningfully better match accuracy.
- A well-lit, front-facing, unobstructed photo at or near 640×480 reliably stays well under the 1MB cap.
- If your image exceeds 1MB, you'll get a `422` response (see [§5](#5-error-handling)) — recapture at a lower resolution rather than upscaling a smaller image.

### 3.1 KYC Face Verification

Returns the full matched identity record when successful — use this for onboarding flows where you need the person's verified details on file.

```bash
curl -X POST https://<YOUR_API_BASE_URL>/api/v1/selfie/verification/kyc/face \
     -H "X-API-Key: your-service-api-key" \
     -H "Content-Type: application/json" \
     -d '{"pinNumber": "GHA-123456789-0", "image": "<base64-png>"}'
```

**Response — match found:**

```json
{
  "code": "00",
  "data": {
    "person": {
      "...": "matched identity fields"
    }
  }
}
```

### 3.2 YES/NO Face Verification

Returns a simple verified/not-verified result — use this when you don't need the full identity record, just a pass/fail (e.g. confirming a returning user is still the same person).

```bash
curl -X POST https://<YOUR_API_BASE_URL>/api/v1/selfie/verification/yes_no/face \
     -H "X-API-Key: your-service-api-key" \
     -H "Content-Type: application/json" \
     -d '{"pinNumber": "GHA-123456789-0", "image": "<base64-png>"}'
```

**Response:**

```json
{
  "code": "00",
  "data": {
    "verified": "YES"
  }
}
```

---

## 4. Response Codes

Check the `code` field in every `200 OK` response body:

| Code | Meaning |
|---|---|
| `00` | Successful verification |
| `01` | Unsuccessful verification — see `data` for the reason |
| `02` | Invalid data submitted |
| `03` | Flagged on a watch list |
| `04` | Internal error during verification |

A `200` status with `code` other than `00` is not a technical failure — it's a business-level result (no match, watch-list hit, etc.) and should be handled as part of your normal flow, not as an error case.

---

## 5. Error Handling

| HTTP status | Meaning | What to do |
|---|---|---|
| `401` | Missing or expired API key | Re-authenticate via [§2.1](#21-log-in) |
| `403` | Invalid API key | Confirm you're sending the current key; contact support if it should be valid |
| `422` | Invalid request — bad Base64, wrong format, or image over 1MB | Fix the image/request and retry — do not retry unchanged |
| `502` / `504` | Verification service temporarily unavailable | Retry after a short delay with exponential backoff |

Error responses carry a `detail` field explaining what went wrong:

```json
{ "detail": "image is 1400000 bytes; the upstream API requires it to be under 1048576 bytes (1MB)" }
```

---

## 6. Code Samples

**Node.js**

```javascript
const response = await fetch("https://<YOUR_API_BASE_URL>/api/v1/selfie/verification/kyc/face", {
  method: "POST",
  headers: {
    "X-API-Key": process.env.VERIFICATION_API_KEY,
    "Content-Type": "application/json",
  },
  body: JSON.stringify({ pinNumber, image: base64Png }),
});
const result = await response.json();
if (response.ok && result.code === "00") {
  // matched — result.data.person has the verified identity
}
```

**Python**

```python
import requests

response = requests.post(
    "https://<YOUR_API_BASE_URL>/api/v1/selfie/verification/yes_no/face",
    headers={"X-API-Key": VERIFICATION_API_KEY},
    json={"pinNumber": pin_number, "image": base64_png},
)
result = response.json()
if response.ok and result["code"] == "00" and result["data"]["verified"] == "YES":
    ...  # confirmed match
```

---

## 7. Best Practices

- **Distinguish technical errors from business results.** A `4xx`/`5xx` status means something went wrong with the request or service; a `200` with `code != "00"` means the verification itself didn't pass — handle these differently in your flow and your logging.
- **Retry `502`/`504` with backoff**, but don't retry `422` without changing the request — it will fail again unchanged.
- **Never cache or store the raw image** beyond what's needed to make the request.
- **Treat your API key like a database credential** — one shared secret per integration, stored server-side, rotated if ever exposed.

---

## 8. Support

For API key issues, integration questions, or to request a rate limit increase, contact **[your integration support contact]**.
