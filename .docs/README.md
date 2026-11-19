# Documentation Index

All technical documentation for the GSS API, organized by purpose.

**Examples**: code samples in standards and guides may use an illustrative domain (`Member`, `Item`, `Assignment`, `Location`, `Category`, ...). Those types are not part of GSS; substitute your feature's entities. Paths, commands, configuration keys, measurements, and statements about current behavior must match the repository implementation.

**Standard**: every doc follows the [documentation standards](documentation-standards.md) — a common template, one topic per file, cross-links instead of duplication.

## Table of Contents

- [Quick Start](#quick-start)
- [Folder Map](#folder-map)
- [Standards](#-standards)
- [Architecture](#-architecture)
- [Guides](#-guides)
- [Testing](#-testing)
- [Conventions for This Index](#conventions-for-this-index)

---

## Quick Start

| I want to… | Start here |
| --- | --- |
| Set up secrets for local development | [guides/local-secrets.md](guides/local-secrets.md) |
| Run everything in containers | [guides/docker-compose.md](guides/docker-compose.md) |
| Understand the project layout | [architecture/project-structure.md](architecture/project-structure.md) |
| Follow the API response contract | [standards/api-response-formats.md](standards/api-response-formats.md) |
| Add a database migration | [guides/dbup-migrations.md](guides/dbup-migrations.md) |
| Call an external HTTP service | [guides/httpclient-factory.md](guides/httpclient-factory.md) |
| Add caching to a service | [guides/hybrid-caching.md](guides/hybrid-caching.md) |
| Write tests | [testing/testing-comprehensive-guide.md](testing/testing-comprehensive-guide.md) |
| Change middleware, CORS or rate limits | [architecture/api-pipeline.md](architecture/api-pipeline.md) |
| Understand exception flow | [architecture/exception-handling.md](architecture/exception-handling.md) |
| Tune performance | [guides/performance-optimization-guide.md](guides/performance-optimization-guide.md) |

---

## Folder Map

| Folder | Purpose | Audience |
| --- | --- | --- |
| [`standards/`](#-standards) | Mandatory rules for how we write code. MUST follow. | All developers |
| [`architecture/`](#-architecture) | Design decisions and cross-cutting concerns. Explains *why*. | Architects, senior devs |
| [`guides/`](#-guides) | How-to implementation steps. Explains *how*. | Developers implementing features |
| [`testing/`](#-testing) | Testing strategy, coverage, and test patterns. | Developers, QA |

All folders are one level deep. Do not nest further. See [documentation-standards.md](documentation-standards.md) for the document template.

---

## 📂 Standards

**Mandatory guidelines that define how we write code. These ensure consistency, maintainability, and quality across the codebase.**

| Doc | Purpose |
| --- | --- |
| [api-response-formats.md](standards/api-response-formats.md) | Standard JSON shape (`Succeeded`, `Message`, `Data`, `Errors`) for every endpoint. |
| [api-versioning-guidelines.md](standards/api-versioning-guidelines.md) | URL-segment versioning (`api/v{version}`), `api-version` header, deprecation and sunset headers. |
| [cancellation-token-guidelines.md](standards/cancellation-token-guidelines.md) | Mandatory `CancellationToken` usage on async methods. |
| [collection-types-guidelines.md](standards/collection-types-guidelines.md) | Collection types per layer: `IReadOnlyList<T>` returns and batch parameters, `ImmutableArray<T>` cache values, `FrozenDictionary` lookups. |
| [database-connection-settings-guidelines.md](standards/database-connection-settings-guidelines.md) | Connection string configuration: timeouts, pooling, best practices. |
| [logging-strategy-recommendations.md](standards/logging-strategy-recommendations.md) | Log levels, structured logging, `[LoggerMessage]` EventId ranges, what each layer logs. |
| [naming-guidelines.md](standards/naming-guidelines.md) | Classes, interfaces, variables — follows Microsoft C# conventions. |
| [security-scanning.md](standards/security-scanning.md) | Local audit for NuGet vulnerabilities, credentials, secrets. |
| [utc-time-guidelines.md](standards/utc-time-guidelines.md) | UTC everywhere: clocks, JSON, query binding, PostgreSQL session. |
| [xml-comments-guidelines.md](standards/xml-comments-guidelines.md) | XML-doc requirements so Swagger/OpenAPI is generated correctly. |

---

## 📂 Architecture

**High-level design decisions and cross-cutting concerns. Explains why the system works the way it does.**

| Doc | Purpose |
| --- | --- |
| [allowed-hosts.md](architecture/allowed-hosts.md) | `AllowedHosts` config — prevents host header injection. |
| [api-pipeline.md](architecture/api-pipeline.md) | Inbound middleware from ApiPipeline.NET: order, forwarded headers, correlation IDs, security headers/CSP, CORS, rate limiting, compression, request limits. |
| [asm-authorization.md](architecture/asm-authorization.md) | `[AsmAuthorize]` module permissions, checked per selected position. |
| [dapper-hybrid-approach.md](architecture/dapper-hybrid-approach.md) | Direct Dapper reads + shared base repository for writes. |
| [error-contract.md](architecture/error-contract.md) | Why errors use the `Response<ApiError>` envelope, not ProblemDetails; Result and exception status mapping. |
| [exception-handling.md](architecture/exception-handling.md) | `UseExceptionHandler` + `ApiExceptionHandler`, exception mapping, ErrorId. |
| [health-checks.md](architecture/health-checks.md) | Kubernetes/container health probe endpoints. |
| [idempotency-analysis.md](architecture/idempotency-analysis.md) | HTTP-method idempotency (PUT, PATCH, DELETE, batches) and the `Idempotency-Key` header on POST creates. |
| [opentelemetry-integration.md](architecture/opentelemetry-integration.md) | Distributed tracing, metrics, structured logs. |
| [project-structure.md](architecture/project-structure.md) | Folder layout of the five source projects (Api, Business, Core, Infrastructure, Util) and where new code goes. |
| [resilience.md](architecture/resilience.md) | Outbound HTTP resilience with HttpResilience.NET: config, pipeline diagram, SSO/MIS/ASM failure scenarios. |
| [security-configuration-comparison.md](architecture/security-configuration-comparison.md) | AllowedHosts vs CSP vs CORS — when each applies. |
| [service-registration-layering.md](architecture/service-registration-layering.md) | Dual SSO/MIS/ASM registration across Infrastructure and Business. |
| [sso-authentication.md](architecture/sso-authentication.md) | SSO v1 authentication scheme, fallback policy, user context and anonymous endpoints. |
| [validation-filter.md](architecture/validation-filter.md) | Automatic FluentValidation pipeline for requests. |

---

## 📂 Guides

**Step-by-step how-to documentation for specific features.**

| Doc | Purpose |
| --- | --- |
| [database-migration-postgresql-to-sqlserver.md](guides/database-migration-postgresql-to-sqlserver.md) | Planning map for replacing PostgreSQL with SQL Server; SQL Server is not supported today. |
| [dbup-migrations.md](guides/dbup-migrations.md) | **Current** migration workflow (SQL-first with DbUp). |
| [docker-compose.md](guides/docker-compose.md) | Run the API with PostgreSQL, Redis and a telemetry dashboard in containers. |
| [httpclient-factory.md](guides/httpclient-factory.md) | Typed `IHttpClientFactory` clients for SSO/MIS/ASM: registration, `HttpServiceBase`, headers, failure mapping, transaction rule. |
| [hybrid-caching.md](guides/hybrid-caching.md) | Server-side caching (`Caching.NET`: InMemory, Redis, Hybrid). |
| [local-secrets.md](guides/local-secrets.md) | Manage secrets in local development: `.env.local` setup, configuration hierarchy, debugging. |
| [mapster-code-generation-guide.md](guides/mapster-code-generation-guide.md) | Mapster mapping guide: `IRegister` configs compiled in strict mode at startup (the file name is historical; no code generation). |
| [performance-optimization-guide.md](guides/performance-optimization-guide.md) | Measurement workflow and checklist for database, HTTP, caching and serialization tuning; no benchmark numbers are claimed. |
| [sonarqube-setup-guide.md](guides/sonarqube-setup-guide.md) | Local SonarQube SAST (SonarCloud or self-hosted). |
| [swagger-ui.md](guides/swagger-ui.md) | Swagger UI over the `Microsoft.AspNetCore.OpenApi` document: setup, CSP, version substitution. |

---

## 📂 Testing

**Test projects, commands, isolation rules and the architecture-rule inventory.**

| Doc | Purpose |
| --- | --- |
| [architecture-testing-guide.md](testing/architecture-testing-guide.md) | NetArchTest and repository rules (`GSS.ArchitectureTests`): layer boundaries, naming, configuration, migrations, scaffold and documentation checks, with a per-file inventory. |
| [testing-comprehensive-guide.md](testing/testing-comprehensive-guide.md) | Test projects, commands, unit and integration isolation rules, coverage scripts, writing tests. |

### Commands

```bash
pwsh scripts/run-tests.ps1 -TestType Unit              # Unit, then architecture tests
pwsh scripts/run-tests.ps1 -TestType Architecture      # Architecture rules only
pwsh scripts/run-tests.ps1 -TestType Integration       # Sequential; needs local PostgreSQL (gss_test)
pwsh scripts/run-coverage.ps1 -TestType Unit -ReportType Html  # Coverage report
```

---

## Conventions for This Index

- Every topic doc MUST appear in its folder's inventory table with a truthful one-line description. Quick Start may link to the same topic for navigation.
- Docs are listed alphabetically within each folder.
- When adding a doc: place it in the correct folder, add an entry here, and follow the [document template](documentation-standards.md#document-template).
