# API Request Pipeline

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Pipeline Order](#pipeline-order)
- [Environment and Startup Guards](#environment-and-startup-guards)
- [Configuration](#configuration)
- [Forwarded Headers and HTTPS](#forwarded-headers-and-https)
- [Correlation IDs](#correlation-ids)
- [Security Headers and CSP](#security-headers-and-csp)
- [CORS](#cors)
- [Rate Limiting](#rate-limiting)
- [Response Compression](#response-compression)
- [Request Limits](#request-limits)
- [HTTP Caching Headers](#http-caching-headers)
- [Error Responses From the Pipeline](#error-responses-from-the-pipeline)
- [Telemetry](#telemetry)
- [Deployment Checklist](#deployment-checklist)
- [Not Enabled Yet](#not-enabled-yet)
- [Related Documentation](#related-documentation)

---

## Overview

The API's inbound middleware comes from the internal **ApiPipeline.NET** package (private `baps-apps-packages` feed): forwarded headers, correlation IDs, security headers (with HSTS), CORS, rate limiting, response compression and Kestrel request limits. Everything is configured in one `ApiPipeline` section, registered with one `AddApiPipeline(configuration)` call and composed with one `UseApiPipeline(...)` call. The package checks the phase order at startup and refuses unsafe settings outside Development.

The project carries no code of its own for correlation IDs, security headers, CORS, rate limiting, response compression, forwarded headers or Kestrel limits. It owns exception handling, status-code pages, HTTPS-only enforcement and Swagger UI, and plugs them into the pipeline with `WithMiddleware(...)`. API version deprecation headers come from the companion `ApiPipeline.NET.Versioning` package (see below).

Package documentation: `https://github.com/baps-apps/apipipeline-net` (README, migration guide, changelog). Offline, `~/.nuget/packages/apipipeline.net/<version>/lib/net10.0/ApiPipeline.NET.xml` documents every option and default.

## Pipeline Order

Built in `MiddlewareExtensions.ConfigureMiddleware()` ([MiddlewareExtensions.cs](../../src/GSS.Api/Extensions/Middleware/MiddlewareExtensions.cs)). Phases run in the order written; nothing is reordered.

| # | Phase | Source | Why here |
| --- | --- | --- | --- |
| 1 | `WithForwardedHeaders()` | Package | Restores client scheme and IP before anything reads them. Routing is added right after it. |
| 2 | `WithCorrelationId()` | Package | Every later log line and response carries the IDs. |
| 3 | `WithSecurityHeaders()` | Package | Headers are written in `OnStarting`, so short-circuited and error responses get them too. |
| 4 | `UseExceptionHandler()` | Framework + project (`ApiExceptionHandler`, an `IExceptionHandler`) | Inside the correlation scope, so error logs carry `CorrelationId`. Writes the `Response<ApiError>` envelope. See [Exception Handling](exception-handling.md). |
| 5 | `UseStatusCodePages()` | Framework | Gives every empty-bodied 4xx/5xx (unmatched route 404, 405, 415, failed route constraint) the `Response<ApiError>` envelope through `ApiErrorProblemDetailsService`. Existing response bodies are left alone; `SuppressMapClientErrors` prevents MVC from writing ProblemDetails. |
| 6 | `UseHttpsOnly` | Project | Answers plain HTTP with a 400 error envelope, except `/internal/health/*`. Skipped in `Testing`. |
| 7 | `UseSwaggerUiIfEnabled` | Project (Swashbuckle) | Serves `/swagger` outside Production, with its own CSP (see [below](#security-headers-and-csp)). Middleware, so it must precede authorization. |
| 8 | `WithCors()` | Package | Before authentication, so preflights are answered without a token. |
| 9 | `WithAuthentication()` | Package (runs authentication; the `SSOv1` scheme is the project's) | Before rate limiting, so limits partition per user. |
| 10 | `WithRateLimiting()` | Package | Before authorization and any endpoint work. |
| 11 | `WithResponseCompression()` | Package | Wraps everything that writes a body. |
| 12 | `WithAuthorization()` | Package | Immediately before the endpoints it protects. |

Version deprecation is not a pipeline phase: `ApiPipeline.NET.Versioning` (`AddApiPipelineVersionDeprecation`, section `ApiPipeline:VersionDeprecation`) configures Asp.Versioning, which writes `Deprecation`/`Sunset`/`Link` on endpoint responses. See [API Versioning Guidelines](../standards/api-versioning-guidelines.md#deprecation).

At startup the package logs `ApiPipeline.NET pipeline composed, in execution order: [...]`. A phase whose `Enabled` flag is false logs event 15; a feature enabled with no phase logs a warning; an order that breaks a package rule logs an `ApiPipeline.NET pipeline order:` warning (a broken security rule fails startup outside Development). None should appear.

`RequireSecurityFeaturesEnabled()` is on in every environment except `Testing`: outside Development, a security phase left disabled (for example forwarded headers, security headers, rate limiting or request limits) fails startup instead of warning. Integration tests turn rate limiting off, so `Testing` is exempt.

Do not add `UseAuthentication`, `UseAuthorization`, `UseCors`, `UseRateLimiter`, `UseForwardedHeaders`, `UseHsts` or `UseResponseCompression` anywhere else: the middleware would run twice or outside the checked order.

## Environment and Startup Guards

The package decides "lenient or strict" from `IHostEnvironment`: only `Development` is lenient (warnings); every other name fails startup on unsafe settings. The project's single environment switch is still `AppSettings:Environment`, and nothing sets `ASPNETCORE_ENVIRONMENT`, so `Program` calls `app.UseConfiguredEnvironment()` right after `Build()`. It copies `AppSettings:Environment` into both `IHostEnvironment` and `IWebHostEnvironment` (they are separate instances). App code still reads configuration, never the host environment.

Consequences:

- `Development` (local, Compose): misconfigurations are warnings.
- `Testing`, `QA`, `UAT`, `Production`: misconfigurations fail startup with a message naming the setting. Fix the setting; do not relax the guard.
- `WebAppFactory` runs as `Testing`, so the integration suite exercises the strict guards. `MiddlewarePipelineOrderTests` builds the real pipeline as `QA` and asserts that an untrusted proxy setup fails startup.

## Configuration

All settings live under `ApiPipeline` in [appsettings.json](../../src/GSS.Api/appsettings.json); override with environment variables, for example `ApiPipeline__ForwardedHeaders__KnownNetworks__0=10.244.0.0/16`. Durations are `TimeSpan` strings (`"00:01:00"`). A configured list **replaces** the package default, and `[]` means empty. Settings are read once at startup.

```json
"ApiPipeline": {
  "ForwardedHeaders": { "Enabled": true, "KnownProxies": [], "KnownNetworks": [], "ClearDefaultProxies": false, "ForwardLimit": 1 },
  "Cors": {
    "Enabled": true,
    "AllowAllInDevelopment": true,
    "AllowedOrigins": [ "https://your-production-domain.com", "https://www.your-production-domain.com" ],
    "AllowedMethods": [ "GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS" ],
    "AllowedHeaders": [ "*" ],
    "AllowCredentials": false,
    "PreflightMaxAge": "01:00:00"
  },
  "RateLimiting": {
    "Enabled": true,
    "DefaultPolicy": "global",
    "GlobalPolicy": "global",
    "Policies": [
      { "Name": "global", "Kind": "FixedWindow", "PermitLimit": 100, "Window": "00:01:00" },
      { "Name": "strict", "Kind": "FixedWindow", "PermitLimit": 10, "Window": "00:01:00" },
      { "Name": "renew", "Kind": "FixedWindow", "PermitLimit": 10, "Window": "00:01:00" }
    ],
    "ExcludedPaths": [],
    "ProbeNetworks": []
  },
  "ResponseCompression": { "Enabled": true, "EnableForHttps": false, "ExcludedPaths": [ "/internal/health", "/health", "/healthz" ] },
  "RequestLimits": { "Enabled": true, "MaxRequestBodySize": 1048576 },
  "VersionDeprecation": { "Enabled": false, "DeprecatedVersions": [] }
}
```

`SecurityHeaders` and `CorrelationId` are not listed: their defaults apply. `VersionDeprecation` is read by ApiPipeline.NET.Versioning, not by a pipeline phase. A feature runs only when its `With*()` phase is in the pipeline **and** `Enabled` is true.

## Forwarded Headers and HTTPS

TLS ends at the ingress, so the API must trust the ingress's `X-Forwarded-Proto` and `X-Forwarded-For`. Without that, HTTPS enforcement rejects every request and rate limiting puts every client in the ingress's bucket.

- **Outside Development, startup fails** until `KnownNetworks` or `KnownProxies` names the ingress. An empty or loopback-only trust set, or a range containing every client (`0.0.0.0/0`), is refused.
- Set `KnownNetworks` to the range the ingress controller's pods get, `ClearDefaultProxies: true`, and `ForwardLimit` to the number of proxy hops. With AKS kubenet each node has its own pod `/24`; trust the whole pod CIDR, or a rescheduled controller falls outside the range.
- `X-Forwarded-Host` is not applied (`ProcessHost: false`); see [AllowedHosts](allowed-hosts.md).
- **HTTPS-only**: `UseHttpsOnly` answers plain HTTP with a 400 `Response<ApiError>` envelope in every environment except `Testing`. Only `/internal/health/*` is allowed over HTTP (kubelet probes hit the container port); the `/health*` and `/healthz*` aliases are not.
- **HSTS** (`max-age` one year, `includeSubDomains`) comes from the security headers phase: every environment except Development, HTTPS requests only, never for `localhost`.

## Correlation IDs

| Header | Value |
| --- | --- |
| `X-Correlation-Id` | The caller's ID when it matches `[A-Za-z0-9_.-]{1,128}`, otherwise a new one. |
| `X-Trace-Id` | The W3C trace ID of the request (`Activity.Current`). |

The correlation ID is no longer the trace ID: a caller can pass its own and see it echoed and logged. Logs carry `CorrelationId` (log scope, exported by OpenTelemetry.NET as `correlation.id`) and `TraceId`. Read them in code with `HttpContext.GetCorrelationId()` / `GetTraceId()` (`using ApiPipeline.NET.Correlation;`).

Outbound calls (SSO, MIS, ASM) propagate `traceparent` automatically. They do not forward `X-Correlation-Id`; see [Not Enabled Yet](#not-enabled-yet).

## Security Headers and CSP

Package defaults, written on every response unless the endpoint already set the header:

| Header | Value |
| --- | --- |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `no-referrer` |
| `Cross-Origin-Resource-Policy` | `same-origin` (does not affect CORS requests) |
| `Cache-Control` | `no-store`, only when the response sets none |
| `Strict-Transport-Security` | See [above](#forwarded-headers-and-https) |
| `Server` | Removed (`RequestLimits:SuppressServerHeader`, on by default) |

`default-src 'none'` would blank Swagger UI, so `UseSwaggerUiIfEnabled` sets a looser policy on `/swagger` only (`default-src 'self'`, inline styles, `data:` images; no inline scripts). Swagger UI and the OpenAPI document exist only outside Production; see [Swagger UI](../guides/swagger-ui.md). Do not loosen `SecurityHeaders:ContentSecurityPolicy` globally; the API serves JSON, which needs no CSP sources.

## CORS

- `AllowAllInDevelopment: true`: in Development any origin, method and header is allowed (startup logs a warning). Ignored in every other environment.
- Elsewhere the configured policy is the default policy: the exact origins in `AllowedOrigins` (`scheme://host[:port]`, no trailing slash or wildcard pattern; a bad entry fails startup). Replace the placeholder origins per environment with environment variables.
- `AllowedHeaders: ["*"]` because clients send `Authorization`, `api-version` and the `x-app-position-id`/`-role-id`/`-division-id`/`-department-id` headers. Narrow it to an explicit list if you prefer.
- `AllowCredentials: false`: the SSO token travels in `Authorization`, not a cookie.
- Do not add `AddCors(o => o.AddDefaultPolicy(...))`: it conflicts with the package's policy and fails startup outside Development. Named policies with `RequireCors("name")` still work.

## Rate Limiting

| Policy | Limit | Applies to |
| --- | --- | --- |
| `global` | 100 requests/minute | Every endpoint (`DefaultPolicy` **and** `GlobalPolicy`). |
| `strict` | 10 requests/minute | `[EnableRateLimiting("strict")]`: `SsoController` logout, on top of `global`. |
| `renew` | 10 requests/minute per bearer token | `[EnableRateLimiting(RenewTokenPartitionKeyResolver.PolicyName)]`: `SsoController` renew-token, on top of `global`. |

- **Partition** (automatic, first match): an application resolver, then authenticated user (`pid`), then client IP (IPv6 by `/64`). Authentication runs first, so a signed-in user has their own bucket. A request with no client IP gets 429 (`AnonymousFallback: Reject`).
- **Renew is keyed by its token** (`Api/RateLimiting/RenewTokenPartitionKeyResolver`, registered with `AddApiPipelineRateLimitPartitionKeyResolver`). Renew usually carries an expired token, which authentication rejects, so the built-in chain would key it by IP and every user behind one NAT, VPN or carrier address would share 10 renewals a minute. The resolver keys the `renew` policy by SHA-256 of the bearer token (never the raw token), so each client has its own bucket; nobody can spend another client's without its token. Minting tokens for fresh buckets is still capped per IP by `global`. No bearer token: the request falls back to IP. Every other policy is left to the built-in chain.
- **Policy names must exist in config**: an `[EnableRateLimiting]` name missing from `ApiPipeline:RateLimiting:Policies` only fails when a request reaches the endpoint, so `ConfigurationFileTests.RateLimitPolicies_UsedByEndpoints_AreConfigured` checks them.
- **Per pod.** 100/minute on 3 replicas allows 300/minute per user. For fleet-wide limits add `ApiPipeline.NET.RateLimiting.Redis`.
- **Health probes** use `.DisableRateLimiting()` on their endpoints, so `ExcludedPaths` and `ProbeNetworks` are `[]`: no path is exempted by source address.
- **Rejection**: 429 with `Retry-After` and the `Response<ApiError>` envelope (see [Error Responses](#error-responses-from-the-pipeline)). `RateLimit`/`RateLimit-Policy` headers are off.
- Turning it off: `ApiPipeline:RateLimiting:Enabled=false`. Outside Development and `Testing` that fails startup by design.
- New policy: add it to `Policies` (`Kind`: `FixedWindow`, `SlidingWindow`, `TokenBucket` or `Concurrency`) and reference it with `[EnableRateLimiting("name")]`.

## Response Compression

Brotli and Gzip at `Fastest`, for the framework's text MIME types plus `application/problem+json`. Probe routes are excluded.

`EnableForHttps: false`: forwarded headers make every ingress request HTTPS, so **API responses are not compressed by the app**. That avoids BREACH (responses can mix secrets with reflected input). Compress at the ingress instead, or set `EnableForHttps: true` only after confirming no response mixes secrets with caller input (startup then logs a warning).

## Request Limits

`RequestLimits` caps request bodies at 1 MB (Kestrel default is 30 MB; also applies to form limits). Larger requests get 413. Other Kestrel limits keep their defaults; set `RequestHeadersTimeout`, `KeepAliveTimeout`, `MaxConcurrentConnections` or `MinRequestBodyDataRateBytesPerSecond` here if needed. For one large endpoint use `[RequestSizeLimit]`.

## HTTP Caching Headers

No controller currently ships with `[ClientCache]`. When a GET returns slow-changing reference data read by browsers, use `[ClientCache]` ([ClientCacheAttribute.cs](../../src/GSS.Api/Filters/ClientCacheAttribute.cs)) to make a successful response browser-cacheable with revalidation: a 200 gets `Cache-Control: private, no-cache` and a weak `ETag` (hash of the JSON body), and a request whose `If-None-Match` matches gets `304 Not Modified` with no body. `private` keeps shared caches (proxies, CDNs) out: every request is authenticated, and RFC 9111 §3.5 lets a shared cache store an `Authorization` response marked `public` and serve it to another user. `no-cache` makes the browser ask every time, so it never shows a value older than the last write; an unchanged value costs a 304 instead of the body. Any other status (404, 400, 403) keeps `no-store`, so a "not found" is never remembered.

The body is serialized once: the filter hashes the UTF-8 JSON and writes those same bytes itself, so MVC's formatter never runs on these responses (a "shallow" ETag, as in Spring's `ShallowEtagHeaderFilter`). A 304 still runs the query and the mapping; it saves the transfer, not the work. Skipping the query on a match would need a cheap per-table version (for example `max(updatedat)` plus a count, or a version bumped on write) checked before the read.

`[ResponseCache]` is allowed only as `NoStore` (`StructureTests.ResponseCache_IsOnlyNoStore`): with a duration it sets `max-age` on every result, 404s included, and a browser then shows stale data after a write until `max-age` expires. The ETag is computed after the action runs, so a 304 saves bandwidth, not the database read; server-side caching of data belongs in `ICacheService`.

There is no server-side response cache: every request carries `Authorization`, which the framework's response and output caches never serve from cache, so the project does not use `UseResponseCaching`. `VaryByQueryKeys` needs that middleware and must not be used. Server-side caching of data belongs in `ICacheService` (see [Hybrid Caching](../guides/hybrid-caching.md)).

Responses without `[ClientCache]` get `Cache-Control: no-store`.

## Error Responses From the Pipeline

The package writes its own error responses (429 from rate limiting) through `IProblemDetailsService` when one is registered. `ApiErrorProblemDetailsService` ([ApiErrorProblemDetailsService.cs](../../src/GSS.Api/Middleware/ApiErrorProblemDetailsService.cs)) is that service: it writes the `Response<ApiError>` envelope instead of `application/problem+json`, so the [error contract](error-contract.md) holds. It is registered directly; `AddProblemDetails()` is still not used.

```json
{
  "succeeded": false,
  "data": null,
  "message": "The rate limit was exceeded. Retry after 60 seconds.",
  "errors": [ { "errorId": "1bf0cc45-…", "statusCode": 429, "message": "The rate limit was exceeded. Retry after 60 seconds." } ]
}
```

## Telemetry

The package's meter (`ApiPipeline.NET`: rate-limit rejections, CORS rejections, replaced correlation IDs) is exported through `OpenTelemetryOptions:Meters: ["ApiPipeline.NET"]`. It creates no spans; the correlation ID is a tag on the request activity.

## Deployment Checklist

Per environment (QA, UAT, Production), set with environment variables:

1. `ApiPipeline__ForwardedHeaders__KnownNetworks__0` = the ingress pod range, `ApiPipeline__ForwardedHeaders__ClearDefaultProxies=true`.
2. `ApiPipeline__Cors__AllowedOrigins__0..n` = the real front-end origins.
3. Review `ApiPipeline__RateLimiting__Policies__*__PermitLimit` against the replica count.
4. Boot once and confirm the composed-pipeline log line, with no `pipeline order` warning and no "phase never added" or event 15 message.
5. `curl -sD - -o /dev/null https://<host>/api/v1/mis/divisions`: expect `X-Correlation-Id`, `X-Trace-Id`, `Content-Security-Policy`, `Strict-Transport-Security`, no `Server`.

## Not Enabled Yet

Package features the API did not have before; candidates to turn on deliberately:

- **Request timeouts** (`RequestTimeouts` + `WithRequestTimeouts()`): default 30 s, 504 on timeout.
- **Edge rate limiting** (`EdgeRateLimiting` + `WithEdgeRateLimiting()`): IP limit before token validation, which matters because every SSO v1 token check is a remote call.
- **Output caching** (`OutputCaching` + `WithOutputCaching()`): server-side caching that can be configured to work with authenticated requests.
- **Correlation propagation**: `.AddApiPipelineCorrelationIdPropagation()` on the SSO/MIS/ASM `AddHttpClient` registrations (requires an Infrastructure reference to the package).

## Related Documentation

- [Error Contract](error-contract.md)
- [Exception Handling](exception-handling.md)
- [SSO Authentication](sso-authentication.md)
- [Health Checks](health-checks.md)
- [AllowedHosts vs CSP vs CORS](security-configuration-comparison.md)
- [OpenTelemetry Integration](opentelemetry-integration.md)
