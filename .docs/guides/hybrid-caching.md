# Caching Implementation Guide

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Why Caching.NET?](#why-cachingnet)
- [How It Works](#how-it-works)
- [Architecture & Design](#architecture--design)
- [Configuration](#configuration)
- [DI Registration](#di-registration)
- [ICacheService API](#icacheservice-api)
- [CacheEntryOverrides (Per-Call Overrides)](#cacheentryoverrides-per-call-overrides)
- [Project Usage](#project-usage)
- [Cache Key Strategy](#cache-key-strategy)
- [Invalidation](#invalidation)
- [Telemetry & Observability](#telemetry--observability)
- [Testing](#testing)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)
- [Related Documentation](#related-documentation)

---

## Overview

The GSS API uses **[Caching.NET](https://github.com/baps-apps/caching-net)** (v3) — a shared .NET caching package with three modes: **InMemory**, **Redis**, and **Hybrid** (in-memory L1 + Redis L2 + optional backplane). It exposes a single `ICacheService` interface across all modes, so the cache topology changes via configuration without touching application code.

Consumers reference only `Caching.NET`. The cache engine underneath is an internal detail: never add an engine package, `using` an engine namespace, or name an engine type in app code. The analyzer shipped in the package (`CACHENET001`) flags it, and with `TreatWarningsAsErrors` that fails the build.

---

## Why Caching.NET?

### The Problem with Traditional Caching

| Problem | Description |
|---------|-------------|
| **Single-Tier Only** | `IMemoryCache` is process-local with no distributed support |
| **No Stampede Protection** | Concurrent cache misses trigger the same expensive factory N times |
| **Manual Coordination** | Developers must handle invalidation and tier coordination manually |
| **No Graceful Disable** | Disabling cache requires code changes or feature flags |

### The Solution

Caching.NET provides:

- **Three cache modes** selectable via configuration
- **Stampede protection** in every mode (one factory call per key; concurrent callers wait)
- **Tags** in every mode, for group invalidation
- **Fail-safe**: an expired entry can be served if the factory fails (on by default, opt out per call)
- **Disabled mode** when `Enabled: false` — a null cache is registered; factories run on every call
- **Single `ICacheService` interface** — one tested implementation shared across applications

---

## How It Works

### Cache Modes

| Mode | Layers | Use Case |
|------|--------|----------|
| **InMemory** | L1 (process memory) only | Development, single instance |
| **Redis** | L2 (Redis) is authoritative; no values served from local memory | Multi-instance where no pod may serve a value Redis has not confirmed |
| **Hybrid** | L1 memory + L2 Redis + backplane for cross-pod invalidation | Production, multi-pod (recommended) |

### Hybrid Mode Flow (Two-Tier)

```text
Request for cached data
    |
1. Check in-memory cache (L1)
    |
1a. HIT  -> Return immediately
    |
1b. MISS -> Check distributed cache (L2, Redis)
    |
2a. HIT  -> Store in L1, return to caller
    |
2b. MISS -> Execute factory (ONE caller only; others wait)
         -> Store in both layers
         -> Backplane notifies other pods to drop their L1 copy
         -> Return to all waiting callers
```

### Stampede Protection

```text
Request 1: Cache miss -> Executes factory
Request 2: Cache miss -> Waits for Request 1
Request 3: Cache miss -> Waits for Request 1
    |
Request 1: Completes -> Stores result -> Returns to all
```

Only one database/service call instead of three.

### Cache Expiration

An entry has two independent lifetimes:

- **`LocalExpiration`** — L1 (in-process memory) lifetime. Ignored in Redis mode.
- **`DistributedExpiration`** — L2 (Redis) lifetime. Only meaningful for Redis and Hybrid.

Either falls back to `DefaultExpiration` when unset. In Hybrid mode, `LocalExpiration` may not exceed `DistributedExpiration`. For a uniform TTL, set both to the same value.

---

## Architecture & Design

### Clean Architecture Placement

```text
Caching.NET package
    |
    ICacheService, CacheEntryOverrides (abstraction)
    |
GSS.Core (Domain)
    |
    CacheKeys (centralized key builders)
    |
GSS.Infrastructure (Registration)
    |
    services.AddCaching(configuration)  // binds "CacheOptions"
    |
GSS.Business (Usage)
    |
    Services inject ICacheService (SsoService, MisService, AsmService)
```

| Concern | Layer | Why |
|---------|-------|-----|
| Cache **registration** (`AddCaching`) | Infrastructure | Depends on Caching.NET package |
| Cache **usage** (deciding what to cache) | Business | Application-level decision |
| **HTTP response caching** (Cache-Control headers) | Api | Transport-level concern |

---

## Configuration

### appsettings.json

```json
{
  "CacheOptions": {
    "Enabled": false,
    "Mode": "Hybrid",
    "ApplicationPrefix": "gss-api",
    "EnvironmentPrefix": "dev",
    "DefaultExpiration": "00:10:00",
    "Entry": {
      "LocalExpiration": "00:05:00",
      "DistributedExpiration": "00:10:00"
    },
    "Redis": {
      "Configuration": "localhost:6379,abortConnect=false"
    },
    "Backplane": {
      "Enabled": true
    },
    "Serialization": {
      "MaximumPayloadBytes": 1048576
    },
    "Security": {
      "MaximumKeyLength": 1024
    }
  }
}
```

### Configuration Options

| Property | Default | Description |
|----------|---------|-------------|
| **`Enabled`** | `true` | Master switch, read once at startup. `false` registers a null cache and skips startup validation |
| **`Mode`** | `InMemory` | `InMemory`, `Redis`, or `Hybrid` |
| **`ApplicationPrefix`** | — | **Required.** Namespaces every key. Must be unique per app sharing a Redis database; no `:` |
| **`EnvironmentPrefix`** | `null` | Appended after the application prefix (e.g. `dev`, `prod`). Vary this per environment rather than baking the environment into `ApplicationPrefix` |
| **`DefaultExpiration`** | `00:10:00` | Lifetime when a call does not set its own |
| **`Entry.LocalExpiration`** | `DefaultExpiration` | Default L1 lifetime |
| **`Entry.DistributedExpiration`** | `DefaultExpiration` | Default L2 lifetime |
| **`Redis.Configuration`** | — | StackExchange.Redis connection string. Required for Redis and Hybrid |
| **`Redis.UseTls`** | `false` | Require TLS regardless of the connection string |
| **`Backplane.Enabled`** | on for Hybrid | Cross-pod L1 invalidation. Set `true` explicitly for anything with more than one replica |
| **`Resilience.FailSafeEnabled`** | `true` | Serve an expired entry when the factory fails. `FailSafeMaxDuration` (default 2 h) must be ≥ `DefaultExpiration` |
| **`Serialization.MaximumPayloadBytes`** | `1048576` | Max serialized value size in bytes |
| **`Security.MaximumKeyLength`** | `512` | Max physical key length, prefix included (the tracked `appsettings.json` sets `1024`) |

Startup validation is strict and the error message names the fix — for example `Redis.Configuration` set while `Mode: InMemory`, or `Entry.LocalExpiration` greater than `Entry.DistributedExpiration` in Hybrid.

### Mode Configuration Examples

**InMemory only** (development):

```json
{
  "CacheOptions": {
    "Enabled": true,
    "Mode": "InMemory",
    "ApplicationPrefix": "gss-api",
    "DefaultExpiration": "00:10:00"
  }
}
```

**Hybrid** (production — multi-pod):

```json
{
  "CacheOptions": {
    "Enabled": true,
    "Mode": "Hybrid",
    "ApplicationPrefix": "gss-api",
    "EnvironmentPrefix": "prod",
    "DefaultExpiration": "00:10:00",
    "Entry": { "LocalExpiration": "00:03:00", "DistributedExpiration": "00:10:00" },
    "Redis": {
      "Configuration": "redis:6379,abortConnect=false",
      "UseTls": true,
      "StrictCertificateValidation": true
    },
    "Backplane": { "Enabled": true }
  }
}
```

### Redis Connection String Formats

- Simple: `"localhost:6379,abortConnect=false"`
- With password: `"localhost:6379,password=yourpassword,abortConnect=false"`
- Cloud: `"your-cache.redis.cache.windows.net:6380,password=key,ssl=True,abortConnect=False"`

### Disabling Caching

Set `Enabled: false`. Caching.NET registers a null cache:

- `GetOrSetAsync` runs the factory on every call
- Reads always miss; writes, removals, and tag invalidation are discarded
- No memory cache, Redis connection, or backplane is created
- Application code is unchanged — no feature flags or null checks needed

Use cases: troubleshooting, integration tests (`WebAppFactory` sets `CacheOptions:Enabled=false`), local development without Redis.

---

## DI Registration

In [`Infrastructure/DependencyInjection.cs`](../../src/GSS.Infrastructure/DependencyInjection.cs):

```csharp
using Caching.NET.Extensions;

// Caching.NET: InMemory, Redis, or Hybrid (configuration-bound via CacheOptions)
services.AddCaching(configuration);
```

Startup validation in [`Api/Program.cs`](../../src/GSS.Api/Program.cs):

```csharp
// Fail fast if Caching.NET registration is misconfigured
app.Services.ValidateCachingRegistration();
```

---

## ICacheService API

Namespaces: `using Caching.NET;` for `ICacheService`, `using Caching.NET.Options;` for `CacheEntryOverrides`, `using Caching.NET.Extensions;` for the extension methods.

### Core Verbs

`ICacheService` methods return `ValueTask` / `ValueTask<T>`. The cancellation parameter is named **`token:`**.

| Method | Purpose |
|--------|---------|
| `GetOrSetAsync<T>(key, factory, options?, tags?, token)` | **Primary read.** Returns the cached value; on a miss runs `factory` once and stores the result |
| `GetOrDefaultAsync<T>(key, defaultValue?, options?, token)` | Returns the cached value or the default. Does not run a factory |
| `TryGetAsync<T>(key, options?, token)` | Returns `CacheValue<T>` — distinguishes a cached `null` from a miss |
| `SetAsync<T>(key, value, options?, tags?, token)` | Writes a value |
| `RemoveAsync(key, options?, token)` | Removes an entry |
| `ExpireAsync(key, options?, token)` | Marks an entry expired but keeps the in-process copy as a fail-safe fallback (the Redis copy is always deleted, so Redis mode behaves like `RemoveAsync`) |
| `RemoveByTagAsync(tag, options?, token)` | Removes every entry carrying the tag |
| `ClearAsync(allowFailSafe, options?, token)` | Invalidates every entry in this cache |

Each also has a synchronous counterpart (`GetOrSet`, `Set`, …).

```csharp
List<ItemDto>? items = await cacheService.GetOrSetAsync(
    $"items-category-{categoryId}",
    async cancel => await repository.GetByCategoryAsync(categoryId, cancel),
    new CacheEntryOverrides
    {
        DistributedExpiration = TimeSpan.FromMinutes(10),
        LocalExpiration = TimeSpan.FromMinutes(1)
    },
    tags: [$"category:{categoryId}"],
    token: cancellationToken);
```

`GetOrSetAsync<T>` returns `T?`. For reference types, handle `null` (the project uses `?? []` for lists).

### Extension Methods (`CacheExtensions`)

These return `Task` / `Task<T>`.

| Method | Purpose |
|--------|---------|
| `ExistsAsync<T>(key, options?, token)` | `true` when a usable value is cached. Does not run a factory |
| `RefreshAsync<T>(key, factory, options?, tags?, token)` | Runs the factory unconditionally and overwrites the entry |
| `GetManyAsync` / `SetManyAsync` / `RemoveManyAsync` | Batch variants |

There is no multi-tag overload of `RemoveByTagAsync` — loop the tags.

---

## CacheEntryOverrides (Per-Call Overrides)

`CacheEntryOverrides` is additive: every property starts `null`, meaning "use the configured default". Set only what the call needs.

| Property | Description |
|----------|-------------|
| **`LocalExpiration`** | L1 lifetime for this entry |
| **`DistributedExpiration`** | L2 lifetime for this entry |
| **`FailSafe`** | `false` disables fail-safe for this call — the factory error surfaces instead of a stale value |
| **`FailSafeMaxDuration`** | Total time the entry is kept for fail-safe. Must be longer than the entry's expiration, or the fail-safe window is dropped |
| **`FactorySoftTimeout`** / **`FactoryHardTimeout`** | Factory timeouts for this call |
| **`EagerRefreshThreshold`** | Refresh in the background once this fraction of the lifetime has elapsed |
| **`JitterFraction`** / **`JitterMaxDuration`** | Randomize the lifetime to avoid synchronized expiry |
| **`SkipBackplaneNotification`** | Don't notify other pods for this write |

There is **no `expiration:` parameter**. Porting a single v2 `expiration` to `DistributedExpiration` alone in Hybrid mode leaves L1 on `DefaultExpiration` — set both.

Per-call overrides are **not validated** at startup (only `CacheOptions` is). A negative expiration or a `FailSafeMaxDuration` shorter than the expiration is accepted silently.

To choose the lifetime from the factory's result (for example, a shorter lifetime for a negative answer), use the context-taking overload and set `factoryContext.Overrides`. It requires an explicit type argument: `GetOrSetAsync<bool>(key, async (factoryContext, cancel) => ...)`.

---

## Project Usage

### SSO Token Validation Caching ([SsoService.cs](../../src/GSS.Business/Services/SsoService.cs))

`SsoService` is the only owner of token-validation entries; the authentication handler has no cache of its own. A valid token is cached until its `exp`; an SSO rejection is cached for five seconds (never past `exp`); outages and other errors propagate and are never cached. Without an expiry (or with one second or less left), nothing is cached. `GetOrSetAsync` is single-flight, so concurrent first requests with one token share one SSO call. `FailSafe = false` prevents stale authentication decisions.

`LogoutAsync` removes the token's validation and ASM entries and the user's MIS positions whenever SSO answers. `RenewTokenAsync` removes the old access token's validation and ASM entries.

### MIS Reference Data Caching ([MisService.cs](../../src/GSS.Business/Services/MisService.cs))

Caches organizational data with long TTLs. Reference data keeps fail-safe on; person positions are authorization data and use `FailSafe = false`.

Fail-safe keeps an entry physically for `FailSafeMaxDuration` and treats it as stale after its expiration. The configured default is 2 hours. When an entry's expiration is longer than that, the engine discards the fail-safe window and the entry disappears on expiry, leaving nothing to fall back on. The 24-hour and 12-hour entries therefore set `FailSafeMaxDuration` explicitly (48 hours and 24 hours).

| Data | Key | Lifetime | Fail-safe |
|------|-----|----------|-----------|
| All departments (filtered and looked up in memory) | `CacheKeys.AllDepartments` | 24 h | 48 h |
| Role types per division | `CacheKeys.RoleTypes(divisionId)` | 24 h | 48 h |
| Roles per division or department | `CacheKeys.Roles(divisionId)`, `CacheKeys.RolesByDepartment(departmentId)` | 12 h | 24 h |
| One role | `CacheKeys.RoleById(roleId)` | 12 h; missing role 5 min | 24 h; 10 min |
| Person positions | `CacheKeys.PersonPositions(personId)` | Until token expiry; uncached without expiry | Off |
| Positions by role | — | Not cached | — |

Cached lists are frozen as `ImmutableArray<T>` so callers cannot mutate the shared L1 instance.

### ASM Authorization Caching ([AsmService.cs](../../src/GSS.Business/Services/AsmService.cs))

Caches all of the caller's ASM entries under the token hash until the token expires. The caller passes that expiry, so the service never parses the token. Without an expiry (or with one second or less left) ASM is called without caching. Fail-safe is off so stale permissions are never served.

```csharp
var cacheExpiration = JwtTokenHelper.GetRemainingLifetime(tokenExpiresAtUtc, timeProvider.GetUtcNow());
if (cacheExpiration == null)
{
    return await FetchAsync(token, cancellationToken).ConfigureAwait(false);
}

var result = await cacheService.GetOrSetAsync(
    CacheKeys.AsmSecurity(token),
    cancel => FetchAsync(token, cancel),
    new CacheEntryOverrides
    {
        DistributedExpiration = cacheExpiration,
        LocalExpiration = cacheExpiration,
        FailSafe = false
    },
    token: cancellationToken).ConfigureAwait(false);

return result.IsDefaultOrEmpty ? Array.Empty<AsmResponseDto>() : result;
```

---

## Cache Key Strategy

The project uses centralized cache key builders in [`Core/Helpers/CacheKeys.cs`](../../src/GSS.Core/Helpers/CacheKeys.cs) to avoid magic strings:

| Builder | Key |
|---------|-----|
| `TokenValidation(token)` | `token-validation-{sha256 of token}` |
| `AsmSecurity(token)` | `asm-security-{sha256 of token}` |
| `PersonPositions(personId)` | `person-{personId}-positions` |
| `AllDepartments` | `departments` |
| `RoleTypes(divisionId)` | `role-types-division-{divisionId}` |
| `Roles(divisionId)` | `roles-division-{divisionId}` |
| `RoleById(roleId)` | `role-{roleId}` |
| `RolesByDepartment(departmentId)` | `roles-department-{departmentId}` |

Builders reject blank person IDs/tokens and IDs below 1.

Caching.NET prepends `ApplicationPrefix` and `EnvironmentPrefix` to every key, so application keys never need an app or environment prefix of their own.

**Key naming conventions:**

- Use lowercase with hyphens: `"role-types-division-{id}"`, `"roles-department-{id}"`
- Use centralized builders (`CacheKeys.X()`) instead of inline strings
- Never include raw user input or tokens in keys — hash them
- Include the entity type for clarity: `"asm-security-"`, `"person-"`
- Include **every** parameter that changes the result. A key that omits one serves one caller's data to another (for example, roles cached without the division)

---

## Invalidation

There are no cache-administration endpoints. Invalidate from code, in the service that owns the data:

- **Entity updates**: `RemoveAsync` the specific entity key (built with `CacheKeys`)
- **Bulk updates**: `RemoveByTagAsync`
- **Configuration changes**: remove the affected reference data, or let it expire
- **Token revocation**: `SsoService.LogoutAsync` removes token validation, ASM and person-position entries; renewal removes the old token's validation and ASM entries

In Hybrid mode with the backplane enabled, removals reach every pod.

---

## Telemetry & Observability

Caching.NET emits metrics and traces under a single name, exposed as constants on `Caching.NET.Telemetry.CacheTelemetry`:

- `CacheTelemetry.MeterName`
- `CacheTelemetry.ActivitySourceName`

OpenTelemetry.NET (2.x) registers the Caching.NET meter automatically, so no extra `AddMeter` call is needed in this project.

| Metric | Meaning |
|--------|---------|
| `caching.net.operations` | Cache operations by result |
| `caching.net.hits` | Reads served from a cached value |
| `caching.net.misses` | Reads with no usable cached value |
| `caching.net.errors` | Cache errors by layer |
| `caching.net.factory.executions` | Factory delegate executions |
| `caching.net.layer.duration` | Per-layer operation duration (histogram, ms; on by default, `Observability.EnableLayerMetrics`) |
| `caching.net.payload.size` | Serialized payload size (histogram, bytes) |

Dimensions: `cache.result`, `cache.layer`, `cache.error.type`. Spans carry `cache.key.fingerprint` rather than the raw key unless `Security.AllowRawKeysInTelemetry` is enabled.

Dashboards or alerts built on v2 names (`cache.hits`, `cache.misses`, `cache.operation.duration`, …) must be repointed — they fail silently.

---

## Testing

Unit tests use a real in-memory cache from [`TestCache`](../../tests/GSS.UnitTests/Common/TestCache.cs) instead of `Mock<ICacheService>`. The contract has many optional parameters, and a stubbed cache only tests the stub.

```csharp
private readonly ICacheService cacheService = TestCache.Create();

[Fact]
public async Task GetAllDepartmentsAsync_SameDivision_SecondCallServedFromCache()
{
    await service.GetAllDepartmentsAsync(divisionId);
    await service.GetAllDepartmentsAsync(divisionId);

    mockCoreService.Verify(s => s.GetAllDepartmentsAsync(divisionId, It.IsAny<CancellationToken>()), Times.Once);
}
```

Mock `ICacheService` only to inject a failure a real cache cannot produce or to record requested expirations. Caching.NET uses the real clock, so never wait for an entry to expire; assert the lifetime computed from the fake `TimeProvider` instead.

Integration tests run with `CacheOptions:Enabled=false`.

---

## Best Practices

### 1. Use GetOrSetAsync for Read Operations

```csharp
// Good: stampede protection and a single round trip
ItemDto? item = await cacheService.GetOrSetAsync(
    $"item-{id}",
    async cancel => await repository.GetByIdAsync(id, cancel),
    token: cancellationToken);

// Avoid: manual get-then-set loses stampede protection
ItemDto? cached = await cacheService.GetOrDefaultAsync<ItemDto>($"item-{id}", token: cancellationToken);
if (cached is null)
{
    cached = await repository.GetByIdAsync(id, cancellationToken);
    await cacheService.SetAsync($"item-{id}", cached, token: cancellationToken);
}
```

### 2. Choose Appropriate Expiration Times

| Data type | TTL | Example |
|-----------|-----|---------|
| Tied to a token | Until token `exp` | Token validation, ASM security, person positions |
| Negative answers | Seconds to minutes | Rejected token (5 s), missing role (5 min) |
| Rarely changing | 12-24 hours | Departments and role types (24 h), roles (12 h) |

### 3. Decide on Fail-Safe Per Call

Fail-safe is on by default. Keep it for data where stale beats unavailable (reference data). Set `FailSafe = false` for security decisions (token validation, permissions) and anything where a stale value is wrong.

### 4. Use CacheKeys Helpers

Always use `CacheKeys` static methods/constants instead of inline strings.

### 5. Don't Wrap Cache Calls

Never wrap a cache call in try/catch to swallow an error or call the source directly. Redis, serialization and backplane errors already degrade to a miss while the `Resilience.ThrowOn*` switches stay false. An exception escaping `GetOrSetAsync` is the factory's own failure; retrying the factory doubles load on the failing dependency. Let it propagate and use fail-safe only where stale reference data is acceptable.

### 6. Don't Cache Sensitive Data

- Never cache passwords or raw tokens — cache validation results under hashed keys
- The project hashes tokens before using them as cache keys: `CacheKeys.TokenValidation(token)` and `CacheKeys.AsmSecurity(token)` take the raw token and build the key from its SHA-256

---

## Troubleshooting

### Cache Not Working (Always Hitting the Source)

1. Verify `CacheOptions:Enabled` is `true`. It is read once at startup — restart after changing it
2. Read the startup summary log line: mode, prefix, and backplane on/off must match intent
3. Verify `services.AddCaching(configuration)` is called in `DependencyInjection.cs`
4. Confirm `app.Services.ValidateCachingRegistration()` passes at startup

### Startup Fails With an Options Validation Error

The `CacheOptions` section is in the wrong shape or violates a rule. The message names the fix. Common causes:

- Missing `ApplicationPrefix`
- `Entry.LocalExpiration` greater than `Entry.DistributedExpiration` in Hybrid
- `Redis.Configuration` set while `Mode` is `InMemory`
- v2 keys (`RedisConnectionString`, `HybridLocalCacheExpiration`, `KeyPrefix`) still present

### Stale Data

1. Remove entries when data changes (`RemoveAsync` or `RemoveByTagAsync`)
2. Reduce expiration times
3. Use `RefreshAsync` to overwrite a hot key
4. In multi-pod Hybrid, confirm `Backplane.Enabled` is `true` — without it, other pods keep their L1 copy until it expires
5. Stale values during an outage may be fail-safe at work; set `FailSafe = false` where that is wrong

### Distributed Cache Not Working

1. Verify `Mode` is `Redis` or `Hybrid` and `Redis.Configuration` is set
2. Check Redis is reachable: `redis-cli --scan --pattern 'gss-api*'` should list entries after a cached call

---

## Related Documentation

- [Caching.NET Package](https://github.com/baps-apps/caching-net) — Source and detailed docs
- [CacheKeys Helper](../../src/GSS.Core/Helpers/CacheKeys.cs)
- [DI Registration](../../src/GSS.Infrastructure/DependencyInjection.cs)
- [API Request Pipeline: HTTP Caching Headers](../architecture/api-pipeline.md#http-caching-headers) — `[ClientCache]` client caching headers (distinct from server-side caching)
