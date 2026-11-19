# Docker Compose Guide

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Services](#services)
- [Configuration](#configuration)
- [Startup Sequence](#startup-sequence)
- [Common Tasks](#common-tasks)
- [Troubleshooting](#troubleshooting)
- [Limitations](#limitations)
- [References](#references)

---

## Overview

[`compose.yaml`](../../compose.yaml) runs the GSS API and all of its dependencies in containers: PostgreSQL, Redis and an OpenTelemetry dashboard. One command builds the API image, starts the dependencies, waits until they are healthy, applies any migrations and seed scripts, and serves the API over HTTPS. It is for local development only.

---

## Quick Start

Prerequisites: Docker with Compose v2, and a GitHub token with `read:packages` for the private NuGet feed (`ApiPipeline.NET`, `Caching.NET`, `CodeStyle.NET`, `HealthChecks.NET`, `HttpResilience.NET`, `OpenTelemetry.NET`).

```bash
export NUGET_AUTH_TOKEN=$(gh auth token)
docker compose up --build
```

| URL | What |
|-----|------|
| `https://localhost:7109/swagger` | Swagger UI (accept the self-signed certificate) |
| `https://localhost:7109/internal/health/readiness` | Readiness probe (all dependency checks) |
| `http://localhost:18888` | Aspire dashboard: traces, metrics and structured logs |

---

## Services

| Service | Image | Host port | Purpose |
|---------|-------|-----------|---------|
| `api` | built from [`Dockerfile`](../../Dockerfile) | `7109` → 8443 (HTTPS) | GSS API, `Development` environment |
| `postgres` | `postgres:17-alpine` | `5433` → 5432 | Database `gss`, data kept in the `postgres-data` volume |
| `redis` | `redis:7.4-alpine` | `6380` → 6379 | Caching.NET distributed layer and backplane, no persistence |
| `otel-dashboard` | `mcr.microsoft.com/dotnet/aspire-dashboard:9.5` | `18888` | OTLP receiver (gRPC 18889, internal) and UI |
| `certs` | `alpine:3.22` | — | Generates a self-signed certificate into the `certs` volume once, then exits |

All host ports bind to `127.0.0.1`. PostgreSQL and Redis use `5433` and `6380` on the host so they do not clash with native installations.

---

## Configuration

The API image uses the committed `appsettings.json`; `compose.yaml` overrides what differs through environment variables (`Section__Key`), which take precedence over JSON files:

| Setting | Value in Compose |
|---------|------------------|
| `DbConnectionSettings:Read/Write` | `postgres` host, compose credentials, `SslMode=Disable` |
| `CacheOptions` | `Enabled=true`, `Mode=Redis`, Redis at `redis:6379`, `EnvironmentPrefix=local` |
| `OpenTelemetryOptions:CollectorUrl` | `http://otel-dashboard:18889` |
| Kestrel | HTTPS on 8443 with the generated certificate, plus a loopback-only HTTP probe listener on 8080 for the healthcheck |

Override credentials or host ports by copying [`.env.example`](../../.env.example) to `.env`. Keep `NUGET_AUTH_TOKEN` in your shell, not in `.env`.

The token reaches the build as a BuildKit secret and is exposed to `dotnet restore` through the `NuGetPackageSourceCredentials_baps-apps-packages` variable for that single step. It is not written to any image layer.

---

## Startup Sequence

1. `postgres` and `redis` start and report healthy; `certs` creates the certificate if missing.
2. `api` starts and validates its read and write database connections (fail fast).
3. DbUp applies pending migrations under a PostgreSQL advisory lock, then reloads Npgsql's cached database types so enum columns created by those migrations are readable in this process.
4. Any `Seeds/Development` scripts are applied (the scaffold ships none).
5. The container reports healthy once `/internal/health/readiness` returns 200 on the loopback-only HTTP probe listener (`127.0.0.1:8080`).

---

## Common Tasks

```bash
docker compose up -d --build --wait      # start in the background and wait for health
docker compose logs -f api               # follow API logs
docker compose up -d --build api         # rebuild and restart only the API after code changes
docker compose exec postgres psql -U gss -d gss
docker compose exec redis redis-cli --scan --pattern 'gss-api:local*'
docker compose down                      # stop, keep database data
docker compose down -v                   # stop and delete database data and the certificate
```

Connect a local SQL client to `localhost:5433` (user `gss`, password `gss-local` by default).

---

## Troubleshooting

### Build Fails With 401 During Restore

`NUGET_AUTH_TOKEN` is missing or the token lacks `read:packages`. Compose does not warn when the variable is unset, and a rebuild can still succeed while the restore layer is cached, so the failure often appears only after a package change. Check with `gh auth status`, then export the token again.

### `ports are not available: address already in use`

Something on the host already uses that port. Set a different `*_HOST_PORT` in `.env`.

### Browser Warns About the Certificate

Expected: the certificate is self-signed. Accept it, or use `curl -k`. Delete the `certs` volume (`docker compose down -v`) to regenerate it.

### Endpoints Return 500 After Changing the Schema

Check the API logs for `Reloaded database types after migration.` If migrations ran but that line is missing, restart the API container.

---

## Limitations

- **Authentication is enforced.** Every endpoint except health probes, OpenAPI/Swagger UI and SSO `renew-token`/`logout` needs a bearer token that the UAT SSO accepts (there is no login action or fake SSO). See [SSO Authentication](../architecture/sso-authentication.md).
- **SSO, MIS and ASM are external.** They point at the UAT URLs in `appsettings.json` and are not containerised.
- **The Dockerfile `HEALTHCHECK` checks liveness; Compose overrides it with readiness.** Both use the plain-HTTP probe listener (8080, `/internal/health/*` only). Behind a TLS-terminating proxy, configure `ApiPipeline:ForwardedHeaders` (see [API Request Pipeline](../architecture/api-pipeline.md#forwarded-headers-and-https)).

## References

- [Docker Compose documentation](https://docs.docker.com/compose/)
- [Health checks](../architecture/health-checks.md)
