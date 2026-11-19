# GSS Clean Enterprise API Scaffold

> **Archived design record (2026-11-06).** This document records the original GSS scaffold design and rejected alternatives. It is not current guidance, and any unchecked work described here does not indicate current implementation status. See the [current documentation](../../../.docs/README.md) and [repository guidance](../../../CLAUDE.md).

## Intent

Create a new .NET 10 Clean Architecture API repository named `GSS` in the
current `gss-api` directory. The repository will use
`/Users/vishalpatel/Projects/boilerplate-net` as its source template and retain
the template's enterprise platform capabilities while removing its commerce
sample domain.

The result is intended to be a production-oriented starting point: teams can
add GSS domain features without first deleting commerce examples or rebuilding
cross-cutting infrastructure.

## Goals

- Produce `GSS.slnx` and consistently named `GSS.*` projects, assemblies,
  namespaces, configuration, container assets, scripts, and documentation.
- Preserve the template's Clean Architecture dependency direction and
  architecture tests.
- Preserve enterprise integrations and platform capabilities, including SSO,
  MIS, ASM, caching, PostgreSQL/Dapper, DbUp, resilience, API versioning,
  validation, rate limiting, security headers, health checks, OpenTelemetry,
  Serilog, OpenAPI/Scalar, Docker Compose, Jenkins/Sonar configuration, package
  auditing, and test infrastructure.
- Preserve reusable primitives such as `BaseEntity`, generic repositories,
  unit of work, CRUD service base, response/pagination models, validation and
  exception middleware, database helpers, and external-service clients.
- Remove all source-template commerce features so the initial API has no sample
  business resource endpoints or schema.
- Leave a repository that restores, builds, and has meaningful retained tests
  when the private NuGet feed is available.

## Non-goals

- Define GSS business entities, use cases, endpoints, or database tables.
- Change the template's database technology, authentication protocol,
  observability stack, package choices, or deployment model.
- Redesign or modernize template behavior beyond changes required to make a
  domain-neutral scaffold compile and test.
- Copy build artifacts, test results, IDE state, `.DS_Store` files, secrets, or
  the template's Git history.

## Anti-goals

- A blind text replacement that leaves commerce types, dead registrations,
  invalid tests, source-template documentation, or broken migrations.
- A minimal demo API that silently drops enterprise integrations.
- A scaffold that can only compile by weakening analyzers, nullable checks,
  warnings-as-errors, architecture rules, or security auditing.
- Vendoring credentials or making the private package source public.

## Constraints

- Target .NET 10 and follow the source template's `global.json`, central package
  management, build properties, analyzers, and formatting conventions.
- Retain the template's five source-layer shape and names, renamed as:
  `GSS.Api`, `GSS.Business`, `GSS.Core`, `GSS.Infrastructure`, and `GSS.Util`.
  This is preferred over introducing `Application`/`Domain` names because the
  request is explicitly template-derived and preserving the established layer
  shape reduces unnecessary churn.
- Retain `GSS.UnitTests`, `GSS.IntegrationTests`, and
  `GSS.ArchitectureTests`.
- Project references must enforce:

  ```text
  GSS.Api -> GSS.Business -> GSS.Core
  GSS.Api -> GSS.Infrastructure -> GSS.Core
  GSS.Api, GSS.Business, GSS.Infrastructure -> GSS.Util
  ```

- `Caching.NET`, `CodeStyle.NET`, `HealthChecks.NET`, and `OpenTelemetry.NET`
  remain sourced from the authenticated `baps-apps` GitHub Packages feed.
  No credentials will be copied into the repository.
- The destination is currently empty and is not a Git worktree. Repository
  creation may initialize Git, but must not copy the template's `.git`
  directory.

## Approaches Considered

### 1. Clean enterprise scaffold — selected

Copy the template's reusable platform, rename it to GSS, and surgically remove
the commerce domain. This preserves production integrations and their proven
composition while yielding a neutral starting point. It requires careful
dependency cleanup and focused verification but best matches both “plain” and
“retain enterprise integrations.”

### 2. Exact renamed clone

Copy everything and globally replace the source application identity with `GSS`. This is fastest and
lowest risk for compilation, but retains products, orders, customers, stock,
commerce SQL, endpoints, and tests. It fails the requirement for a plain
starter repository.

### 3. Rebuild integrations in freshly generated projects

Generate new projects and port each cross-cutting feature independently. This
could produce a smaller result, but duplicates already-working composition,
increases regression risk, and makes it easier to omit subtle startup,
security, health-check, or test behavior.

## Repository Shape

```text
gss-api/
├── GSS.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── nuget.config
├── Dockerfile
├── compose.yaml
├── Jenkinsfile
├── README.md
├── .docs/
├── scripts/
├── src/
│   ├── GSS.Api/
│   ├── GSS.Business/
│   ├── GSS.Core/
│   ├── GSS.Infrastructure/
│   └── GSS.Util/
└── tests/
    ├── GSS.UnitTests/
    ├── GSS.IntegrationTests/
    └── GSS.ArchitectureTests/
```

Root operational files from the template will be retained when they are
generic and updated to use GSS paths, assembly names, container names,
database names, and documentation. Generated directories (`bin`, `obj`,
`TestResults`) and machine-local files will be excluded.

## Domain-neutral Boundary

The following commerce concepts and their direct tests will be removed:

- Address, Article, Color, Customer, Label, Order, OrderPosition, Product,
  Size, and Stock entities.
- Their repository interfaces and implementations.
- Their DTOs, validators, mappings, business services, controllers, unit tests,
  integration tests, and test-data builders.
- Commerce enums, cache keys, SQL migrations, development seed data, and
  schema diagrams.

The following reusable or enterprise-facing capabilities will remain:

- SSO authentication and token renewal, MIS lookup, and ASM authorization,
  including their core contracts, infrastructure clients, business wrappers,
  controllers, configuration models, caching behavior, and tests.
- Cache-management API and validation.
- Generic entity/repository/unit-of-work/CRUD primitives and their focused
  tests, adapted where they currently require a commerce entity.
- API response, pagination, batch-update, validation, exception handling,
  correlation, deprecation, compression, CORS, rate limiting, forwarded
  headers, security headers, OpenAPI, and health infrastructure.
- Dapper connection and transaction infrastructure and DbUp's migration
  engine. Migration/seed directories start empty and are ready for GSS SQL.
- PostgreSQL and Redis configuration, Compose services, observability, and
  readiness/startup behavior.

If a nominal sample type is required solely to test a generic primitive, it
will live in the relevant test project as a private test fixture rather than in
production code.

## Configuration and Operational Assets

- Rename application identity, solution/project paths, namespaces, assembly
  references, Docker artifacts, Sonar/Jenkins paths, database names, and log
  messages from the source application identity to GSS.
- Keep configuration sections and environment-variable contracts stable unless
  they contain a source-template-specific value.
- Use a neutral PostgreSQL schema/database identifier such as `gss` and
  `gss_test` where the template currently uses its source database names.
- Keep placeholders and example local values, but do not add real secrets.
- Rewrite README and retained documentation so commands and architecture
  describe GSS. Remove documents whose only subject is the deleted commerce
  schema; update the documentation index accordingly.
- Preserve scripts and CI/security configuration, changing names and paths only
  where needed.

## Testing and Acceptance

Verification will be proportional to the available package-feed and service
access:

1. Assert no product, operational, or current-state documentation file or path
   contains the source-template application identity. The design and implementation-plan records
   under `docs/superpowers/` are exempt because they intentionally identify the
   source template and rejected exact-clone approach.
2. Assert no commerce production types or endpoints remain.
3. Restore and build `GSS.slnx` with warnings as errors.
4. Run unit and architecture tests.
5. Run integration tests when PostgreSQL and required configuration are
   available; otherwise document the exact environmental blocker and retain
   compile validation for the integration-test project.
6. Validate solution contents and project-reference direction.
7. Run formatting verification and `git diff --check` when Git is initialized.

Private-feed authentication and external enterprise endpoints are environment
requirements, not values to mock globally or remove merely to make local
startup succeed. Focused tests will continue to use fakes/mocks where the
template already does.

## Builder Decisions

The builder may decide:

- Which domain-only documentation pages must be removed versus rewritten,
  provided the documentation index remains accurate and no commerce guidance
  survives as current GSS behavior.
- Which retained generic tests need local test fixtures after commerce test
  data is removed.
- Whether neutral empty directories are represented by project `<Folder>`
  entries or placeholder files.
- The exact local development ports and container names, provided collisions
  are avoided and all documentation/configuration agrees.
- Whether to initialize Git before or after file generation. The template's
  history must never be copied, and no remote will be configured.

## Completion Criteria

The work is complete when the destination is a coherent GSS repository, all
retained projects and operational assets use GSS naming, commerce code and SQL
are absent, architectural boundaries remain enforced, and available build/test
verification passes or any external credential/service blocker is reported
with exact evidence.
