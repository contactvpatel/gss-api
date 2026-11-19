# GSS API

GSS is a domain-neutral .NET 10 Web API built with Clean Architecture. It ships the BAPS enterprise platform pieces (authentication, authorization, persistence, resilience, observability, security middleware) and no business feature. Add the first feature as a vertical slice across the existing layers.

## Table of Contents

- [Solution](#solution)
- [Platform Capabilities](#platform-capabilities)
- [Prerequisites](#prerequisites)
- [Quick Start with Docker Compose](#quick-start-with-docker-compose)
- [Run Locally](#run-locally)
- [Build and Test](#build-and-test)
- [Configuration](#configuration)
- [Adding a Feature](#adding-a-feature)
- [Documentation](#documentation)
- [License](#license)

---

## Solution

| Project | Responsibility |
| --- | --- |
| `GSS.Core` | Entities (`EntityBase`), repository and unit-of-work contracts, enterprise service contracts |
| `GSS.Business` | Use-case services (`EntityServiceBase`), DTOs, FluentValidation validators, Mapster mappings, `Result` |
| `GSS.Infrastructure` | Dapper repositories (`DapperRepositoryBase`), DbUp migrations, SSO/MIS/ASM HTTP clients |
| `GSS.Api` | HTTP host: controllers, authentication, ASM authorization, middleware, health checks, OpenAPI |
| `GSS.Util` | Configuration models, configuration keys, JWT and UTC helpers |
| `GSS.UnitTests` | Fast tests with mocked dependencies |
| `GSS.IntegrationTests` | PostgreSQL-backed API and persistence tests |
| `GSS.ArchitectureTests` | Layer, naming, structure, migration-script and scaffold rules |

Project references: Api → Business, Infrastructure, Util (Core transitively); Business → Core, Util; Infrastructure → Core, Util. Core and Util reference nothing.

## Platform Capabilities

| Capability | Implementation | Docs |
| --- | --- | --- |
| Authentication | `SSOv1` scheme (`SsoAuthenticationHandler`) with a fallback policy: every endpoint requires a valid SSO token unless it opts out with `[AllowAnonymous]` | [sso-authentication.md](.docs/architecture/sso-authentication.md) |
| Authorization | `[AsmAuthorize(ModuleCode, AccessType)]`, checked against the caller's selected position | [asm-authorization.md](.docs/architecture/asm-authorization.md) |
| Inbound pipeline | ApiPipeline.NET: forwarded headers, correlation ID, security headers/CSP, CORS, rate limiting, compression, request limits, with order checked at startup | [api-pipeline.md](.docs/architecture/api-pipeline.md) |
| Outbound HTTP | HttpResilience.NET: timeouts, retries, circuit breaker for SSO, MIS and ASM clients | [resilience.md](.docs/architecture/resilience.md) |
| Errors | `Response<ApiError>` envelope, `Result` to status mapping, sanitized exception handling | [error-contract.md](.docs/architecture/error-contract.md) |
| Persistence | PostgreSQL via Dapper (read/write connections, unit of work); page-number and ID-cursor reads (`GetPagedAsync`, `GetPagedAfterAsync`); DbUp migrations under an advisory lock | [dapper-hybrid-approach.md](.docs/architecture/dapper-hybrid-approach.md) |
| Caching | Caching.NET (in-memory, Redis or hybrid) | [hybrid-caching.md](.docs/guides/hybrid-caching.md) |
| Observability | OpenTelemetry.NET traces, metrics and logs over OTLP | [opentelemetry-integration.md](.docs/architecture/opentelemetry-integration.md) |
| Health | HealthChecks.NET liveness, startup and readiness probes under `/internal/health/*` | [health-checks.md](.docs/architecture/health-checks.md) |
| API surface | URL versioning, `Microsoft.AspNetCore.OpenApi` document, Swagger UI outside Production | [swagger-ui.md](.docs/guides/swagger-ui.md) |
| Time | UTC everywhere: JSON converter, model binder, `Timezone=UTC` connections | [utc-time-guidelines.md](.docs/standards/utc-time-guidelines.md) |
| Delivery | Dockerfile, Docker Compose, Jenkins, SonarQube, coverage and security scripts | [scripts/README.md](scripts/README.md) |

Enterprise endpoints included: `api/v1/sso` (token renewal, logout), `api/v1/mis` (divisions, departments, role types, roles, positions, person positions) and `api/v1/asm` (application security).

## Prerequisites

- .NET SDK 10.0.404 or a later 10.0.4xx patch (pinned in `global.json`)
- PowerShell 7 for repository scripts
- PostgreSQL 17 (or Docker) for local runs and integration tests
- A GitHub token with `read:packages` for the private `baps-apps` NuGet feed

```bash
export NUGET_AUTH_TOKEN=$(gh auth token)
```

## Quick Start with Docker Compose

```bash
export NUGET_AUTH_TOKEN=$(gh auth token)
docker compose up --build
```

| Service | Address |
| --- | --- |
| API (self-signed HTTPS) | `https://localhost:7109` |
| Swagger UI | `https://localhost:7109/swagger` |
| Readiness probe | `https://localhost:7109/internal/health/readiness` |
| PostgreSQL (`gss`, user `gss`) | `localhost:5433` |
| Redis | `localhost:6380` |
| Telemetry dashboard | `http://localhost:18888` |

Ports and local credentials can be overridden in a `.env` file (see `.env.example`). See [docker-compose.md](.docs/guides/docker-compose.md).

Use `docker compose up -d --build --wait` to wait for health checks and `docker compose logs -f api` to follow startup. The readiness probe uses a self-signed certificate, so local verification with curl requires `curl -k https://localhost:7109/internal/health/readiness`.

## Run Locally

1. Create the database (the API never creates databases):

   ```bash
   createdb -h localhost -U postgres gss
   ```

2. Provide credentials through environment variables:

   ```bash
   export DbConnectionSettings__Read__Password="<password>"
   export DbConnectionSettings__Write__Password="<password>"
   ```

3. Run the API:

   ```bash
   dotnet run --project src/GSS.Api
   ```

## Build and Test

```bash
dotnet restore GSS.slnx
dotnet format GSS.slnx --no-restore --verify-no-changes
dotnet build GSS.slnx --no-restore
dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj --no-restore
dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj --no-restore
pwsh scripts/verify-scaffold.ps1
```

Integration tests need PostgreSQL and use a separate `gss_test` database, never the developer database. Override the connection with `INTEGRATION_TEST_DB_HOST`, `_PORT`, `_NAME`, `_USER` and `_PASSWORD`:

```bash
export INTEGRATION_TEST_DB_PASSWORD="<local-password>"
pwsh scripts/run-tests.ps1 -TestType Integration
```

## Configuration

Settings live in `src/GSS.Api/appsettings.json`; secrets never do. Override with ASP.NET Core environment-variable syntax:

```bash
export DbConnectionSettings__Write__Password="<password>"
export MisService__Headers__AuthAppId="<app-id>"
export MisService__Headers__AuthAppSecret="<app-secret>"
export AsmService__Headers__AuthAppId="<app-id>"
export AsmService__Headers__AuthAppSecret="<app-secret>"
```

`AppSettings:Environment` is the single environment switch (`Development`, `Testing`, `QA`, `UAT`, `Production`). Outside Development, startup fails if a security phase of the pipeline is disabled.

## Adding a Feature

1. **Core**: entity deriving from `EntityBase` and a repository interface.
2. **Infrastructure**: a sealed repository deriving from `DapperRepositoryBase<T>`, registered in `DataAccessExtensions`, and a DbUp script under `DbUp/Migrations` (add it to `MigrationScriptTests.KnownScripts`).
3. **Business**: DTOs, validators, a Mapster `IRegister` config, and a service (usually deriving from `EntityServiceBase`), registered in `DependencyInjection`.
4. **Api**: a controller deriving from `ApiControllerBase`, protected with `[AsmAuthorize]` using a `ModuleCode` that matches ASM, and request examples in `RequestExampleSchemaTransformer`.
5. **Tests**: unit tests per layer and an integration test class deriving from `ApiIntegrationTestBase`.

## Documentation

Start at the [documentation index](.docs/README.md). Repository scripts are described in [scripts/README.md](scripts/README.md). Examples may use illustrative feature names, but paths, commands, configuration keys, and implementation claims must match this repository.

## License

Private repository: proprietary and confidential. No open-source license is granted; do not copy, share or publish this code outside the organization.
