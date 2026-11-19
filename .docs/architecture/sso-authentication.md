# SSO Authentication

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Pipeline Position](#pipeline-position)
- [How a Request Is Authenticated](#how-a-request-is-authenticated)
- [User Context](#user-context)
- [Anonymous Endpoints](#anonymous-endpoints)
- [Responses](#responses)
- [Caching](#caching)
- [Testing](#testing)
- [Known Limitations](#known-limitations)
- [References](#references)

---

## Overview

The API authenticates callers with SSO v1 bearer tokens through a standard ASP.NET Core authentication scheme, `SSOv1` ([SsoAuthenticationHandler.cs](../../src/GSS.Api/Authentication/SsoAuthenticationHandler.cs)). SSO v1 tokens are **unsigned**, so they cannot be validated locally: every token is validated by calling SSO, and the result is cached. A fallback authorization policy makes every endpoint require an authenticated user unless the endpoint opts out.

## Pipeline Position

Registered in [AuthenticationExtensions.cs](../../src/GSS.Api/Extensions/Features/AuthenticationExtensions.cs). The ApiPipeline.NET pipeline in [MiddlewareExtensions.cs](../../src/GSS.Api/Extensions/Middleware/MiddlewareExtensions.cs) runs (see [API Request Pipeline](api-pipeline.md#pipeline-order)):

```text
ForwardedHeaders → CorrelationId → SecurityHeaders → ExceptionHandling → StatusCodePages
→ HTTPS-only → Swagger UI → CORS → Authentication → RateLimiting → Compression
→ Authorization → Endpoints
```

`UseAuthentication` only identifies the caller and never rejects a request. Rejection happens at `UseAuthorization`, through the fallback policy. Because authentication runs before rate limiting, limits partition per authenticated user and fall back to the client IP for anonymous calls.

## How a Request Is Authenticated

| Step | Outcome |
| --- | --- |
| No `Authorization: Bearer` header | `NoResult` (anonymous) |
| Not `header.payload.signature` with three non-empty parts, or not a parseable JWT | `Fail("Invalid token format")` |
| No `exp` claim | `Fail("Token has no expiry")` |
| `exp` has passed (scheme `TimeProvider`) | `Fail("Token has expired")` |
| No `pid` claim | `Fail("Token has no user id")` |
| `pid` present but not numeric | `Fail("Invalid token format")` |
| SSO rejects the token | `Fail("Token validation failed")` |
| SSO accepts the token | `Success` with a `ClaimsPrincipal` |

All local checks run before the SSO call, so these tokens never cost one. The JWT's claims are trusted **only after** SSO accepts the token.

A local rejection is logged at Information (`RequestLog.TokenRejectedLocally`, EventId 2100), never Warning: it is the client's mistake and needs no action, and authentication runs before rate limiting, so a Warning per request would let anyone flood alerting. Only an SSO rejection is a Warning (`RequestLog.TokenRejectedBySso`, EventId 2101), since a well-formed, unexpired token that SSO refuses may be forged or revoked.

## User Context

On success, the handler:

- sets `HttpContext.User`: authentication type `SSOv1`, all JWT claims, plus `pid` copied to `ClaimTypes.NameIdentifier`, so `User.Identity.Name` is the user id;
- writes the token to `HttpContext.Items[HttpContextItemKeys.UserToken]` (key in [HttpContextItemKeys.cs](../../src/GSS.Util/Security/HttpContextItemKeys.cs), shared by the handler and `UserContext`);
- tags the request span with `user.id` (the `pid`), when the span is recorded. OpenTelemetry.NET reads the user when the request starts, before authentication, so it cannot set it.

Services and repositories read both through `IUserContext` ([UserContext.cs](../../src/GSS.Infrastructure/Services/Internal/UserContext.cs)). `GetUserId()` reads only the authenticated `NameIdentifier` claim; `GetToken()` reads `Items`. The raw token is never stored as a claim.

## Anonymous Endpoints

| Endpoint | Opt-out |
| --- | --- |
| Health probes (`/internal/health/*`, `/health*`, `/healthz*`) | `.AllowAnonymous()` in `HealthCheckExtensions` |
| OpenAPI document | `.AllowAnonymous()` in `OpenApiExtensions` |
| Swagger UI | Middleware before authentication (`UseSwaggerUiIfEnabled`), so authorization never sees it |
| `SsoController` renew-token and logout actions | `[AllowAnonymous]` |

Any new endpoint is protected by default. Opt out explicitly with `[AllowAnonymous]` or `.AllowAnonymous()`.

## Responses

- **401:** `WWW-Authenticate: Bearer`, plus the standard `Response<T>` envelope whose single error carries the failure reason (default `Token is missing or invalid`).
- **403:** the standard envelope with `Access denied`.
- **500:** validation hit an internal error (a bug, a bad SSO payload, bad configuration), so the token was not judged. The standard envelope with `Internal server error` and a generic message (the exception is logged once, never returned), and no `WWW-Authenticate`: a 401 would make clients log every user out. Only on endpoints that require authentication.
- **503:** SSO could not answer (5xx, 408, 429, timeout, open circuit, connection failure, or 404/405, which mean a wrong URL or endpoint), so the token was not judged. The standard envelope with `Authentication unavailable`, no `WWW-Authenticate`, and `Retry-After` when the upstream failure carries a wait. Only on endpoints that require authentication.

## Caching

The Business `SsoService` ([SsoService.cs](../../src/GSS.Business/Services/SsoService.cs)) is the only owner of token-keyed cache entries; the handler keeps no cache of its own. Validation results are cached under `CacheKeys.TokenValidation(token)` (`token-validation-{sha256}`, never the raw token) with `FailSafe = false`:

- a valid token is cached until its `exp`; a token without `exp`, or with one second or less left, is not cached;
- a rejected token is cached for 5 seconds (at most until its `exp`): a burst of requests with a bad token costs one SSO call, and SSO decides again soon after;
- validation is single-flight (`GetOrSetAsync`): concurrent first requests with the same token share one SSO call;
- an SSO outage (503) and an internal validation error (500) are not cached: only SSO's own verdict is.

If the cache is unavailable, Caching.NET treats it as a miss and SSO is called; the service has no cache try/catch of its own. If SSO cannot answer, the request is rejected with 503; any other validation error is rejected with 500. Both fail closed without a 401, so a server fault never logs users out. Enable caching in every deployed environment; otherwise each request costs a remote SSO call.

### Renew

`POST /api/v1/sso/renew-token` (`[AllowAnonymous]`, `renew` rate limit: 10/minute per bearer token, not per IP, since an expired token leaves the caller anonymous; see [API Pipeline](api-pipeline.md#rate-limiting)) takes the refresh token in the body and the access token as the `Authorization: Bearer` header. The access token may be expired, which is the usual reason to renew: SSO accepts that and checks both tokens. So the action reads the raw header (`JwtTokenHelper.ExtractBearerToken`), not `IUserContext.GetToken()`, which is set only for a token authentication accepted (unexpired and validated by SSO). The token must still be JWT-shaped, so garbage gets 401 without an SSO call. The action carries `[ReadsBearerToken]`, so the OpenAPI document lists a bearer-only requirement and Swagger UI sends the token.

When SSO issues new tokens, or the client disconnects mid-call (SSO may already have renewed), `SsoService.RenewTokenAsync` removes the old access token's validation and ASM security entries (with `CancellationToken.None`; a refusal or an outage keeps them), so the old token is no longer trusted from cache: its next use is re-checked with SSO, which decides whether it is still valid. The user's MIS positions stay cached (they are keyed by user, not token). The eviction has no try/catch: Caching.NET absorbs and logs a Redis failure, so the new tokens are still returned.

When SSO refuses the renewal (400, 401 or 403, e.g. an expired refresh token), the action answers 401 (`Token renewal failed`), so the client sends the user to log in again. Only an SSO fault answers 502/503/504 (via `ApiExceptionHandler`), which a client may retry: an outage, or a 2xx without both tokens (an empty or `null` body, `{}`, a blank token), which is a broken SSO answer, never a refusal that logs the user out.

### Logout

`POST /api/v1/sso/logout` is `[AllowAnonymous]` with the `strict` rate limit, so a missing, rejected or expired token gets the action's own 401 (`Logout failed`) instead of the authentication challenge. The controller reads the token and user ID from `IUserContext`, which only returns values the handler stored after SSO validated the token, so an unvalidated `pid` claim can never reach logout. `SsoService.LogoutAsync(token, userId)` ends the SSO session and, whenever SSO answers (also when it refuses, so a token SSO no longer accepts is not trusted from cache until its `exp`; and when the client disconnects mid-call, since SSO may already have ended the session; an outage clears nothing; the removal runs with `CancellationToken.None`, because the request's token is cancelled by then), removes the token's validation and ASM security entries (`CacheKeys.TokenValidation`, `CacheKeys.AsmSecurity`) and the user's MIS positions (`CacheKeys.PersonPositions`). When SSO refuses the logout (400, 401 or 403), the action answers 401 (`Logout failed`); only an SSO outage answers 502/503/504.

## Testing

- Unit: [SsoAuthenticationHandlerTests.cs](../../tests/GSS.UnitTests/API/Authentication/SsoAuthenticationHandlerTests.cs) (handler) and [SsoServiceTests.cs](../../tests/GSS.UnitTests/Business/Services/SsoServiceTests.cs) (caching and logout cleanup).
- Integration: `WebAppFactory` replaces only the Business `ISsoService` with [FakeSsoService.cs](../../tests/GSS.IntegrationTests/Fixtures/FakeSsoService.cs), and every client sends `FakeSsoService.ValidToken`. [AuthenticationIntegrationTests.cs](../../tests/GSS.IntegrationTests/API/AuthenticationIntegrationTests.cs) clears the header to exercise anonymous calls.

## Known Limitations

- **SSO load before rate limiting.** Unsigned tokens can be forged freely, so each new forged token costs one SSO call before the rate limiter runs. A rejection is cached for 5 seconds, so a repeated bad token reaches SSO at most about once per 5 seconds per token (per pod when the cache is not shared). Enforce IP-based limits at the gateway or ingress, or enable ApiPipeline.NET's edge rate limiting (`WithEdgeRateLimiting()`), which runs before authentication.
- **Authorization** is separate: see [ASM Authorization](asm-authorization.md).
- **Future signed SSO.** If a later SSO version signs its tokens, replace the handler with `AddJwtBearer` (local JWKS validation). Policies, controllers and `IUserContext` stay unchanged.

## References

- [ASP.NET Core authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [ASM authorization](asm-authorization.md)
