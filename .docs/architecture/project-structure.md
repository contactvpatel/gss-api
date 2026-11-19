# Project Structure

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Solution Projects](#solution-projects)
- [Tracked Source Layout](#tracked-source-layout)
- [Dependency Direction](#dependency-direction)
- [Where New Feature Code Goes](#where-new-feature-code-goes)
- [Persistence and Transactions](#persistence-and-transactions)
- [Tests and Enforcement](#tests-and-enforcement)
- [Related Documentation](#related-documentation)

---

## Overview

GSS is a domain-neutral Clean Architecture scaffold with five source projects and three test projects. It includes enterprise platform behavior—SSO, ASM, MIS, persistence, caching, resilience, observability, health, and security—but no sample business feature.

## Solution Projects

| Project | Responsibility |
| --- | --- |
| `src/GSS.Core/GSS.Core.csproj` | Entities, repository and Unit of Work contracts, external-service contracts, and shared domain models. |
| `src/GSS.Business/GSS.Business.csproj` | Use-case services, DTOs, FluentValidation validators, Mapster mappings, and `Result` outcomes. |
| `src/GSS.Infrastructure/GSS.Infrastructure.csproj` | Dapper, DbUp, caching registration, external HTTP clients, and internal infrastructure services. |
| `src/GSS.Api/GSS.Api.csproj` | HTTP host, controllers, authentication/authorization, filters, middleware, health checks, and OpenAPI. |
| `src/GSS.Util/GSS.Util.csproj` | Configuration models, validation attributes, JWT helpers, security constants, and UTC helpers. |
| `tests/GSS.UnitTests/GSS.UnitTests.csproj` | Fast behavior tests with substituted dependencies. |
| `tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj` | API and persistence tests against PostgreSQL. |
| `tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj` | Dependency, naming, configuration, scaffold, migration, and documentation rules. |

`GSS.slnx` is expected to contain exactly these eight projects.

## Tracked Source Layout

The following tree lists the real top-level source folders (abridged: root files such as `Program.cs` and the `DependencyInjection.cs` files are omitted), not hypothetical feature files:

```text
src/
├── GSS.Api/
│   ├── Authentication/  Authorization/  Controllers/
│   ├── Extensions/      Filters/        HealthChecks/
│   ├── Helpers/         HostedServices/ Middleware/
│   ├── Models/          Properties/     RateLimiting/
│   ├── Serialization/   Validators/
├── GSS.Business/
│   ├── DTOs/       Helpers/   Mappings/
│   ├── Models/     Services/  Validators/
├── GSS.Core/
│   ├── Entities/   Exceptions/ Helpers/
│   ├── Interfaces/ Models/
├── GSS.Infrastructure/
│   ├── DbUp/       Extensions/ Helpers/
│   ├── Interfaces/ Repositories/ Services/
└── GSS.Util/
    ├── Models/ Security/ Time/ Validation/
```

Generated `bin/` and `obj/` directories are not part of the source layout and must not be tracked.

## Dependency Direction

The approved project references are:

```text
GSS.Api → GSS.Business → GSS.Core
GSS.Api → GSS.Infrastructure → GSS.Core
GSS.Api, GSS.Business, GSS.Infrastructure → GSS.Util
```

Core and Util reference no other GSS project. Business and Infrastructure do not reference one another. Api references Infrastructure at the composition root; controllers and filters use Business abstractions rather than concrete Infrastructure types.

## Where New Feature Code Goes

The names below are roles, not shipped sample files:

- **Core:** the feature entity, repository interface, and domain-facing contracts.
- **Business:** request/response DTOs, validators, explicit Mapster configuration, service interface, and service implementation.
- **Infrastructure:** repository implementation plus DbUp migration and optional seed scripts.
- **Api:** versioned controller, authorization metadata, and OpenAPI request examples where useful.
- **Tests:** Business and infrastructure unit tests plus API/persistence integration tests in the matching test project.

Keep a feature slice coherent across these layers. Do not place business rules in controllers or SQL helpers, and do not introduce an Infrastructure reference into Business.

## Persistence and Transactions

`DapperRepositoryBase<T>` implements common active-row reads and single/range writes. A feature repository supplies table-specific SQL and columns. The scaffold does not contain a dynamic query builder.

Single writes and range writes execute immediately. Range methods use one set-based PostgreSQL statement and own their transaction when no Unit of Work exists. Use `IUnitOfWork` only when several operations must commit or roll back as one business operation. Database calls inside one Unit of Work are sequential because they share one connection.

DbUp scripts live under `src/GSS.Infrastructure/DbUp/Migrations`. The scaffold currently includes the generic idempotency-key storage migration. New scripts are append-only and must be added to `tests/GSS.ArchitectureTests/MigrationScriptTests.cs` with their SHA-256 hash.

## Tests and Enforcement

The architecture suite verifies both emitted type dependencies and project references. It also checks naming, DTO shape, repository SQL metadata, constructor injection, configuration, migration history, and direct wall-clock access. `scripts/verify-scaffold.ps1` separately checks the exact eight-project `GSS.slnx` inventory and rejects source-template/sample-commerce residue, unexpected feature SQL, generated artifacts, and reachable non-placeholder MIS/ASM credentials.

Run:

```bash
dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj --no-restore
pwsh scripts/verify-scaffold.ps1
```

## Related Documentation

- [Dapper repository architecture](dapper-hybrid-approach.md)
- [Service registration layering](service-registration-layering.md)
- [Architecture testing guide](../testing/architecture-testing-guide.md)
