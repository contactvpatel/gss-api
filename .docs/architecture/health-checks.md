# Health Checks

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Probe Model](#probe-model)
- [Endpoints](#endpoints)
- [Registered Checks](#registered-checks)
- [Startup Gate](#startup-gate)
- [Response Format](#response-format)
- [Implementation](#implementation)
- [Adding a Check](#adding-a-check)
- [Kubernetes Integration](#kubernetes-integration)
- [Troubleshooting](#troubleshooting)
- [Related Documentation](#related-documentation)

---

## Overview

The GSS API uses the internal **HealthChecks.NET** package (private `baps-apps-packages` feed) for its health endpoints. One `AddApiHealthChecks` call gives the API the same Kubernetes probe contract as other BAPS services: three tag-filtered probes, per-check severity, a timeout on every check, sanitized output and result caching shared across probes. The package is built on `Microsoft.Extensions.Diagnostics.HealthChecks`.

## Probe Model

| Probe | Answers | Runs | On failure |
|-------|---------|------|------------|
| **Liveness** | Is the process responsive? | Process-only check, no I/O | Kubernetes restarts the pod |
| **Startup** | Has warm-up finished? | Startup gate | Kubernetes keeps waiting, then restarts |
| **Readiness** | Can this pod serve traffic? | Startup gate, database, Redis, disk, memory | Pod removed from rotation, **not** restarted |

Rules that keep a dependency outage from becoming a restart storm:

- **Liveness never touches a dependency.** If a database blip failed liveness, Kubernetes would restart every pod at once. Dependency checks carry the `ready` tag only.
- **`Degraded` is not down.** Redis and the disk and memory checks report `Degraded`, so the probe still returns 200 and the pod stays in rotation. Only `Unhealthy` (database, startup gate) returns 503.

## Endpoints

`app.MapHealthCheckEndpoints()` maps eleven fixed routes: three main routes and eight aliases. An alias behaves exactly like its main route. Point probes at the `/internal` routes.

| Main route | Aliases | Tag |
|------------|---------|-----|
| `/internal/health/liveness` | `/internal/health/live`, `/health/live`, `/healthz/live` | `live` |
| `/internal/health/startup` | `/health/startup`, `/healthz/startup` | `startup` |
| `/internal/health/readiness` | `/internal/health/ready`, `/health/ready`, `/healthz/ready` | `ready` |

The `/health` and `/healthz` aliases exist for orchestrators already configured with those paths and cannot be turned off. The response body names every registered check, so **block `/health*` and `/healthz*` at the ingress** to keep the dependency list off the public edge.

The old `/health`, `/health/detailed` and `/health/db` routes no longer exist.

All probe routes are:

- excluded from HTTP server metrics (`DisableHttpMetrics`), so probe traffic does not dominate `http.server.request.duration`;
- exempt from rate limiting (`DisableRateLimiting`), so a 429 cannot fail readiness;
- excluded from response compression.

## Registered Checks

| Check | Source | Tags | Failure status | Timeout |
|-------|--------|------|----------------|---------|
| `liveness` | Built in | `live` | Unhealthy | 1s |
| `core-services` | Built in (startup gate) | `startup`, `ready` | Unhealthy | 1s |
| `database-read` | `AddCustomCheck` + `DatabaseConnectionHealthCheck` | `ready` | Unhealthy | 3s |
| `database-write` | `AddCustomCheck` + `DatabaseConnectionHealthCheck` | `ready` | Unhealthy | 3s |
| `disk` | `AddDiskCheck` (200 MB free minimum) | `ready` | Degraded | 1s |
| `memory` | `AddMemoryCheck` (1024 MB working-set maximum) | `ready` | Degraded | 1s |
| `redis` | `AddRedisCheck`, only when caching is enabled in `Redis` or `Hybrid` mode and `CacheOptions:Redis:Configuration` is not blank | `ready` | Degraded | 3s |

The database, disk, memory and Redis results are cached for 5 seconds (`DefaultCacheTtlSeconds`), so frequent probes do not add database load. `liveness` and `core-services` are not cached.

**Why the database checks are custom:** `AddPostgreSqlCheck` allows only one check, but the API has separate read and write connections. Both checks run `SELECT 1` (`DatabaseConnectionValidator`) through `IDapperConnectionFactory`, so they use the same connection strings, pool and `ApplicationName` as the repositories.

There is no configuration-keys check: SSO/MIS/ASM settings are validated at startup, so a bad value fails the host instead.

**Redis:** the check reuses `CacheOptions:Redis:Configuration`, appends `ssl=true` when `CacheOptions:Redis:UseTls` is set, and mirrors `CacheOptions:Redis:StrictCertificateValidation`. It uses its own connection, separate from the Caching.NET client.

## Startup Gate

Startup and readiness report `Unhealthy` until `IStartupReadinessMarker.MarkReady()` is called. `ConfigureHealthCheckEndpoints` calls it from `IHostApplicationLifetime.ApplicationStarted`, which fires only after:

1. `DatabaseConnectionValidationHostedService` has opened the read and write connections.
2. `DatabaseMigrationInitFilter` has applied DbUp migrations and seed scripts.

Both throw on failure, so the host never starts and a failed warm-up never marks the API ready.

## Response Format

Each probe returns 200 (`Healthy` or `Degraded`) or 503 (`Unhealthy`) with `Cache-Control: no-store` and a JSON body:

```json
{
  "status": "Degraded",
  "totalDuration": "00:00:00.0004700",
  "entries": {
    "core-services": { "status": "Healthy", "description": "core services started", "duration": "00:00:00.0000147", "tags": ["startup", "ready"] },
    "database-read": { "status": "Healthy", "description": "database reachable", "duration": "00:00:00.0001279", "tags": ["ready"] },
    "database-write": { "status": "Healthy", "description": "database reachable", "duration": "00:00:00.0001091", "tags": ["ready"] },
    "disk": { "status": "Healthy", "description": "disk headroom ok", "duration": "00:00:00.0000979", "tags": ["ready"] },
    "memory": { "status": "Healthy", "description": "memory ok", "duration": "00:00:00.0001037", "tags": ["ready"] },
    "redis": { "status": "Degraded", "description": "redis check failed", "duration": "00:00:00.0001128", "tags": ["ready"] }
  }
}
```

A failed or timed-out check shows only `"<name> check failed"`. Exception messages, connection strings and stack traces are never included in the body. The underlying exception is logged server-side (`Health check failed: ...`).

The body does not include the application version or build number. Swagger UI's title still shows both.

## Implementation

| File | Role |
|------|------|
| `src/GSS.Api/Extensions/Features/HealthCheckExtensions.cs` | `ConfigureHealthChecks` (registration) and `ConfigureHealthCheckEndpoints` (routes and startup gate) |
| `src/GSS.Api/HealthChecks/DatabaseConnectionHealthCheck.cs` | `SELECT 1` against a read or write connection; lets failures throw so the package sanitizes them |
| `src/GSS.Api/appsettings.json` | `ApiPipeline:ResponseCompression:ExcludedPaths` excludes probe routes from compression |
| `src/GSS.Api/Extensions/Middleware/MiddlewareExtensions.cs` | `UseHttpsOnly` lets `/internal/health/*` through over plain HTTP |
| `Directory.Packages.props`, `nuget.config` | `HealthChecks.NET` and `HealthChecks.NET.Redis` versions; `HealthChecks.NET*` mapped to the private feed |

Registration is called from `ServiceExtensions.ConfigureApiLayerServices`; endpoints are mapped in `Program.Main` after `ConfigureMiddleware`.

**Tests.** `HealthApiIntegrationTests` covers the healthy probes and aliases, and the probe model itself: with reads failing (`WebAppFactory.Connections.FailReads`, backed by `Fixtures/SwitchableConnectionFactory`), readiness returns 503 with `database-read` `Unhealthy`, liveness stays 200, and the body contains no connection details. Results are cached for 5 seconds, so that test polls until the outage shows and again until readiness recovers (about 10 s).

## Adding a Check

Use the builder returned by `AddApiHealthChecks` in `ConfigureHealthChecks`. Never add a raw `IHealthCheck` to the framework's `IHealthChecksBuilder`: that skips the timeout, sanitizing and caching.

```csharp
// Outbound HTTP dependency (Degraded by default, ready tag)
healthChecks.AddOutboundHttpCheck("sso", "https://sso.example.org/health");

// Illustrative feature-specific signal (Unhealthy by default, ready tag)
healthChecks.AddCustomCheck("assignments-queue", serviceProvider =>
    new AssignmentsQueueHealthCheck(serviceProvider.GetRequiredService<IAssignmentsQueue>()));
```

- Never tag a dependency check `live`.
- Choose the severity on purpose: `Unhealthy` only when the pod cannot serve traffic without the dependency.
- Check names must be unique; a duplicate throws `ArgumentException` at startup.
- HealthChecks.NET has no configuration switch. To register a check conditionally, wrap it in an `if`, as the Redis check does.
- A custom check's `Description` is returned as written. Never put secrets in it; throw instead and let the package sanitize the failure.

## Kubernetes Integration

```yaml
livenessProbe:
  httpGet:
    path: /internal/health/liveness
    port: 8080
  periodSeconds: 10
  timeoutSeconds: 3
  failureThreshold: 3
startupProbe:
  httpGet:
    path: /internal/health/startup
    port: 8080
  periodSeconds: 5
  failureThreshold: 60   # allows up to 5 minutes for migrations
readinessProbe:
  httpGet:
    path: /internal/health/readiness
    port: 8080
  periodSeconds: 10
  timeoutSeconds: 5
  failureThreshold: 3
```

`UseHttpsOnly` (project middleware added with `WithMiddleware`) answers plain HTTP with 400 outside the `Testing` environment, **except** on `/internal/health/*`. Kubelet probes the pod port directly, often without TLS, so the probe routes accept plain HTTP; their responses are sanitized. The `/health*` and `/healthz*` aliases remain HTTPS-only, which is another reason to probe `/internal`. The Dockerfile `HEALTHCHECK` probes `/internal/health/liveness` over plain HTTP on `127.0.0.1:8080`; `compose.yaml` overrides it to probe `/internal/health/readiness` the same way, on a listener bound to the container's loopback.

## Troubleshooting

| Symptom | Cause | Action |
|---------|-------|--------|
| Liveness returns 503 during a database or Redis outage | A dependency check was tagged `live` | Move it to readiness. Liveness must stay process-only. |
| Startup and readiness stay 503 after boot | `MarkReady` never ran: startup did not complete | Check logs for connection validation or migration failures |
| Host fails to start with `OptionsValidationException` | A required setting is missing or invalid (e.g. an SSO/MIS/ASM `Url` that is not HTTPS to an external host, or an `Endpoint` path with a leading slash) | The exception names each failing key. Settings are validated at startup, so there is no config check in readiness. |
| Readiness 200 but `status` is `Degraded` | Redis, disk or memory check failing | Expected; the pod stays in rotation. See the logged `Health check failed` entry for the cause. |
| Restore fails with 404 for `HealthChecks.NET.*` | Package source mapping missing | Confirm `<package pattern="HealthChecks.NET*" />` under `baps-apps-packages` in `nuget.config` |
| Probe returns 400 | Plain HTTP to a `/health*` or `/healthz*` alias | Probe the `/internal/health/*` route, or use HTTPS (see [Kubernetes Integration](#kubernetes-integration)) |

## Related Documentation

- [Docker Compose Guide](../guides/docker-compose.md)
- [API Request Pipeline](api-pipeline.md) (rate limiting, compression, HTTPS-only)
- [OpenTelemetry Integration](opentelemetry-integration.md)
- [Hybrid Caching Guide](../guides/hybrid-caching.md)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)
