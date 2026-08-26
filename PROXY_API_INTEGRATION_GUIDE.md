# Proxy API — Integration Guide

This guide covers everything you need to integrate with the Proxy API: authentication, how requests are forwarded, and error handling. It does not cover any other product this platform offers — only the generic proxy passthrough.

---

## 1. Overview

The Proxy API lets you make an authenticated call to a backend resource through a single, consistent interface — you send it a request, it forwards that request through on your behalf, and hands you back the response. The specific resource paths and payloads available to your integration are provided separately by your integration contact; this guide covers the mechanics of calling the proxy itself, which are the same regardless of what's behind it.

All requests are made over HTTPS to:

```
https://<YOUR_API_BASE_URL>
```

Replace `<YOUR_API_BASE_URL>` with the base URL provided by your integration contact.

---

## 2. Authentication

Every request requires an `X-API-Key` header. You obtain that key by exchanging the username and password issued to you for it.

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

> The response may include a couple of additional fields beyond what's shown above — you can safely ignore anything other than `apiKey`.

### 2.2 Use your key

Attach the returned `apiKey` as the `X-API-Key` header on every proxy request. Store it like any other server-side secret — never in a mobile app, browser bundle, or any client-side code — and contact your integration contact to have it rotated if you suspect it's been exposed.

---

## 3. Making a Request

```
{METHOD} https://<YOUR_API_BASE_URL>/api/v1/proxy/<resource-path>
```

- **Methods supported:** `GET`, `POST`, `PUT`, `PATCH`, `DELETE` — all use the same route pattern, just with the method that matches the operation you're calling.
- **Path:** whatever you put after `/api/v1/proxy/` is the specific resource you're calling. Your integration contact will tell you which paths are available to you and what each expects.
- **Query string:** forwarded through unchanged.
- **Request body:** forwarded through unchanged, for `POST`/`PUT`/`PATCH`/`DELETE`.
- **Headers forwarded:** only `Content-Type`, `Accept`, `X-Request-Id`, and `X-Correlation-Id` are passed through. Any other header you send (custom headers, cookies, etc.) is dropped before the request continues on.

**Example (illustrative — replace `example-resource` with a real path from your integration contact):**

```bash
# GET
curl https://<YOUR_API_BASE_URL>/api/v1/proxy/example-resource \
     -H "X-API-Key: your-service-api-key"

# POST
curl -X POST https://<YOUR_API_BASE_URL>/api/v1/proxy/example-resource \
     -H "X-API-Key: your-service-api-key" \
     -H "Content-Type: application/json" \
     -d '{"field": "value"}'
```

---

## 4. Response Behavior

| Situation | What you get back |
|---|---|
| Successful call | `200 OK`, with the response body as-is |
| Business-level error (validation, not found, etc.) | The same HTTP status code and body the backend produced |
| Backend unreachable or timed out | `502` or `504` |
| Missing or invalid `X-API-Key` | `401` (missing) or `403` (invalid) |

A successful call always comes back as `200`, even if the underlying operation itself returned a different success code (e.g. `201 Created`) — check the response body for the actual result rather than relying on the status code alone in that case.

Error responses carry a `detail` field. Depending on the failure, `detail` may be a plain string or a structured object with its own fields — inspect it rather than assuming a fixed shape:

```json
{ "detail": "a plain-text explanation" }
```
```json
{ "detail": { "code": "...", "msg": "a more structured explanation", "...": "additional fields" } }
```

---

## 5. Code Samples

**Node.js**

```javascript
const response = await fetch("https://<YOUR_API_BASE_URL>/api/v1/proxy/example-resource", {
  method: "GET",
  headers: { "X-API-Key": process.env.PROXY_API_KEY },
});
const result = await response.json();
if (response.ok) {
  // use result
} else {
  const detail = typeof result.detail === "string" ? result.detail : JSON.stringify(result.detail);
  // handle error using `detail`
}
```

**Python**

```python
import requests

response = requests.get(
    "https://<YOUR_API_BASE_URL>/api/v1/proxy/example-resource",
    headers={"X-API-Key": PROXY_API_KEY},
)
if response.ok:
    result = response.json()
else:
    detail = response.json().get("detail")
    # handle error using `detail`
```

---

## 6. Best Practices

- **Retry `502`/`504` with exponential backoff.** These indicate a temporary backend issue, not a problem with your request.
- **Don't retry other 4xx errors unchanged** — fix the request first (bad path, malformed body, etc.); retrying identically will fail the same way.
- **Treat your API key like a database credential** — one shared secret per integration, stored server-side, rotated if ever exposed.
- **Don't assume a fixed error shape** — always check whether `detail` is a string or an object before displaying or logging it.

---

## 7. Support

For API key issues, to find out which resource paths are available to your integration, or general integration questions, contact **[your integration support contact]**.
