# AllowedHosts Configuration Guide

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [What is AllowedHosts?](#what-is-allowedhosts)
- [Security Risks](#security-risks)
- [Guidelines](#guidelines)
- [Configuration](#configuration)
- [Environment-Specific Settings](#environment-specific-settings)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)
- [Implementation Details](#implementation-details)
- [Related Documentation](#related-documentation)

---

## Overview

This guide explains the `AllowedHosts` setting, why it matters and how to set it for deployments.

`AllowedHosts` is a built-in ASP.NET Core security feature that validates the `Host` header in incoming HTTP requests. It prevents host header injection attacks and cache poisoning by ensuring requests only come from trusted hostnames.

**Why AllowedHosts exists:**

- Prevents host header injection attacks
- Protects against cache poisoning
- Ensures requests are routed to the correct application
- Validates that requests are intended for your application

**Implementation Location:**

- Configuration: `appsettings.json` (`"*"`), overridden per deployment by the `AllowedHosts` environment variable
- Framework: Built into ASP.NET Core (`WebApplication.CreateBuilder` registers it); the project adds no code for it
- Middleware: `HostFilteringMiddleware`, added by a startup filter ahead of the app's own pipeline

## What is AllowedHosts?

`AllowedHosts` validates the `Host` HTTP header value against a whitelist of allowed hostnames. When a request arrives with a `Host` header that doesn't match the allowed list, ASP.NET Core returns a `400 Bad Request` response.

### How It Works

1. Client sends HTTP request with `Host` header: `Host: api.example.com`
2. ASP.NET Core checks if `api.example.com` is in the `AllowedHosts` list
3. If allowed: Request proceeds normally
4. If not allowed: Returns `400 Bad Request` with a small HTML page ("Bad Request - Invalid Hostname"). It runs before the ApiPipeline.NET phases, so this response is not the `Response<ApiError>` envelope and has no correlation or security headers

### Example Request

```http
GET /api/v1/mis/divisions HTTP/1.1
Host: api.example.com
Authorization: Bearer <token>
```

If `api.example.com` is not in `AllowedHosts`, the request is rejected.

## Security Risks

### 1. Host Header Injection

**Attack Scenario:**

- Attacker sends request with malicious `Host` header: `Host: evil.com`
- If `AllowedHosts` is `"*"`, the request is accepted
- Application may generate URLs or redirects using the malicious host
- Users could be redirected to attacker's site

**Impact:**

- Phishing attacks
- Session hijacking
- Password reset token theft
- Open redirect vulnerabilities

### 2. Cache Poisoning

**Attack Scenario:**

- Attacker sends request with `Host: victim.com`
- Response is cached by proxy/CDN with wrong host
- Legitimate users receive poisoned cache entries
- Sensitive data may be exposed

**Impact:**

- Data leakage
- XSS attacks via cached responses
- Cache manipulation

### 3. Password Reset Poisoning

**Attack Scenario:**

- Attacker requests password reset with `Host: attacker.com`
- Application generates reset link using `Host` header
- Reset link points to attacker's domain
- Attacker intercepts password reset tokens

**Impact:**

- Account takeover
- Unauthorized access
- Data breach

## Guidelines

From Microsoft's host filtering documentation and this project's deployment practice:

### ✅ **DO:**

1. **Always specify exact hostnames in production**
   - Never use `"*"` in production
   - List all valid hostnames explicitly
   - Include variations (with/without www, subdomains); a subdomain wildcard such as `*.example.com` is allowed

2. **Use environment-specific configuration**
   - Development: Can use `"*"` for convenience
   - Production: Must specify exact hostnames

3. **Include all valid hostname variations**
   - `api.example.com`
   - `www.api.example.com`
   - `api-staging.example.com` (if applicable)
   - Internal hostnames (if behind load balancer)

4. **Store configuration in secure vault**
   - Set the deployment's environment variables from Vault
   - Don't commit production hostnames to source control

### ❌ **DON'T:**

1. **Never use `"*"` in production**
   - Security vulnerability
   - Violates Microsoft security guidelines
   - Allows any host header

2. **Don't rely on default behavior**
   - Explicitly configure `AllowedHosts`
   - Don't assume it's secure by default

3. **Don't include untrusted domains**
   - Only include domains you control
   - Don't allow third-party domains

## Configuration

### Configuration Format

`AllowedHosts` can be configured as:

1. **Single hostname** (string):

   ```json
   "AllowedHosts": "api.example.com"
   ```

2. **Multiple hostnames** (semicolon-separated string):

   ```json
   "AllowedHosts": "api.example.com;www.api.example.com;api-staging.example.com"
   ```

3. **Wildcard** (development only):

   ```json
   "AllowedHosts": "*"
   ```

### Configuration Files

#### Base Configuration (`appsettings.json`)

```json
{
  "AllowedHosts": "*"
}
```

**Note:** `appsettings.json` is the only settings file and is development-flavoured, so `"*"` suits local work (localhost, 127.0.0.1, etc.). There are no `appsettings.{Environment}.json` files; deployments override the value with an environment variable.

#### Deployed Environments (environment variable)

```bash
AllowedHosts="api.example.com;www.api.example.com;api-staging.example.com"
```

**Rationale:** Production must specify exact hostnames. Multiple hostnames are separated by semicolons.

## Environment-Specific Settings

### Local Development

**Configuration:**

```json
{
  "AllowedHosts": "*"
}
```

**Rationale:**

- Allows testing with `localhost`, `127.0.0.1`, `localhost:5000`, etc.
- Convenient for local development
- No security risk in isolated development environment

### QA/UAT/Production

**Configuration** (environment variable per deployment, value kept in Vault):

```bash
# QA
AllowedHosts="api-qa.example.com"
# Production
AllowedHosts="api.example.com;www.api.example.com"
```

**Rationale:**

- Each environment has specific hostnames
- Must specify exact hostnames (no wildcards)
- Store in Vault for security and environment-specific management

### Behind Load Balancer / Reverse Proxy

**Configuration:**

```json
{
  "AllowedHosts": "api.example.com;internal-api.example.com;10.0.0.5"
}
```

**Important Notes:**

- Include internal hostnames if load balancer forwards original host
- Include IP addresses if direct IP access is needed (no port)
- The API does not apply `X-Forwarded-Host` (`ApiPipeline:ForwardedHeaders:ProcessHost` is false); the `Host` header the ingress sends is the one filtered

## Best Practices

### 1. **Never Use Wildcard in Production**

❌ **Bad:**

```json
{
  "AllowedHosts": "*"
}
```

✅ **Good:**

```json
{
  "AllowedHosts": "api.example.com;www.api.example.com"
}
```

### 2. **Include All Valid Hostname Variations**

✅ **Good:**

```json
{
  "AllowedHosts": "api.example.com;www.api.example.com;api-staging.example.com"
}
```

**Include:**

- Primary domain: `api.example.com`
- WWW variant: `www.api.example.com`
- Staging/QA variants: `api-staging.example.com`
- Internal hostnames (if applicable)

### 3. **Use Environment-Specific Configuration**

✅ **Good:**

- Local Development: `"*"` from `appsettings.json`
- QA/UAT/Production: specific hostnames in the `AllowedHosts` environment variable (its value kept in Vault)

### 4. **Store Production Config in Vault**

✅ **Good:**

- Store production `AllowedHosts` in Vault
- Don't commit production hostnames to source control
- Use environment variables or secure configuration management

### 5. **Document Allowed Hostnames**

✅ **Good:**

- Document why each hostname is allowed
- Review and update regularly
- Remove unused hostnames

### 6. **Test Configuration Changes**

✅ **Good:**

- Test in staging before production
- Verify all valid hostnames work
- Ensure invalid hostnames are rejected

### 7. **Monitor Rejected Requests**

✅ **Good:**

- Watch the `Microsoft.AspNetCore.HostFiltering` log category, which records each rejected host
- Monitor for attack patterns
- Alert on suspicious activity

## Troubleshooting

### Error: "Bad Request - Invalid Hostname"

**Symptom:**

```
HTTP 400 Bad Request
Bad Request - Invalid Hostname (HTML body)
```

**Causes:**

1. `Host` header doesn't match `AllowedHosts`
2. Missing hostname in configuration
3. Typo in hostname configuration

**Solutions:**

1. Verify the `Host` header in the request
2. Check `AllowedHosts` configuration
3. Ensure hostname matches (case-insensitive; the port is ignored)
4. Add missing hostname to configuration

### Requests Work in Development but Fail in Production

**Symptom:**

- Requests work locally but fail in production

**Causes:**

1. Development uses `"*"` (allows any host)
2. Production has specific hostnames
3. Production hostname not in `AllowedHosts`

**Solutions:**

1. Verify production hostname matches `AllowedHosts`
2. Check the deployment's `AllowedHosts` environment variable
3. Ensure the Vault value it is set from is correct
4. Test with exact production hostname

### Load Balancer / Reverse Proxy Issues

**Symptom:**

- Requests fail behind load balancer
- Internal hostnames not recognized

**Causes:**

1. Load balancer forwards different `Host` header
2. Internal hostname not in `AllowedHosts`
3. The ingress rewrites `Host` and the original host is only in `X-Forwarded-Host`, which the API ignores

**Solutions:**

1. Include internal hostname in `AllowedHosts`
2. To filter on `X-Forwarded-Host` instead, set `ApiPipeline:ForwardedHeaders:ProcessHost=true`. It needs a real `AllowedHosts` list (not `*`) outside Development, and only a host on that list is applied. See [API Request Pipeline](api-pipeline.md#forwarded-headers-and-https).

3. Verify load balancer configuration

### Health Check Endpoints Failing

**Symptom:**

- Health check endpoints (`/internal/health/*`) return `400 Bad Request`

**Causes:**

1. Kubernetes `httpGet` probes send the pod IP as `Host` by default, which is not in `AllowedHosts`
2. Load balancer health checks use an IP address or internal hostname

**Solutions:**

1. Set the probe's `httpHeaders` to send an allowed `Host` (Kubernetes)
2. Configure load balancer health checks to use an allowed hostname
3. Avoid listing pod IPs: they change on every reschedule

## Implementation Details

### How ASP.NET Core Uses AllowedHosts

`WebApplication.CreateBuilder` (the web host defaults) binds `HostFilteringOptions.AllowedHosts` from the `AllowedHosts` key, split on `;`, and adds `HostFilteringMiddleware` through a startup filter, so it runs before every middleware in `MiddlewareExtensions.ConfigureMiddleware`:

1. **Configuration Binding:** the `AllowedHosts` value, reloaded if configuration changes
2. **Middleware Registration:** automatic; the project never calls `UseHostFiltering()`
3. **Validation:**
   - Compares the request's `Host` header against the allowed list
   - Case-insensitive comparison, port ignored
   - Returns `400 Bad Request` if not allowed; `"*"` allows any host

### Configuration Priority

Configuration is loaded in this order (later overrides earlier):

1. `appsettings.json`
2. Environment variables (deployments set these, e.g. from Vault)
3. Command-line arguments

### Case Sensitivity

Hostname comparison is **case-insensitive**:

- `api.example.com` matches `API.EXAMPLE.COM`
- `Api.Example.Com` matches `api.example.com`

### Port Numbers

Port numbers are **ignored**: list host names without a port. `api.example.com` matches `api.example.com`, `api.example.com:443` and `api.example.com:5001`.

## Related Documentation

- [Microsoft Documentation: Host Filtering](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/host-filtering)
- [OWASP: Host Header Injection](https://owasp.org/www-community/attacks/HTTP_Host_Header_Injection)
- [API Request Pipeline](api-pipeline.md) - Forwarded headers and CORS
