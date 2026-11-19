# GSS repository guidance

GSS is a domain-neutral .NET 10 Clean Architecture API scaffold, kept in sync with the BAPS `boilerplate-net` template minus its sample domain. Preserve the enterprise integrations and keep dependencies pointing inward.

## Required checks

Run these before completing a change:

```bash
dotnet format GSS.slnx --no-restore --verify-no-changes
dotnet build GSS.slnx --no-restore
dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj --no-restore
dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj --no-restore
pwsh scripts/verify-scaffold.ps1
```

Run integration tests when PostgreSQL is available:

```bash
dotnet test tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj --no-restore
```

## Scaffold rules

- `GSS.Core` owns domain primitives and contracts. It has no project references.
- `GSS.Business` owns use cases, DTOs, mapping, and validation. It references Core and Util.
- `GSS.Infrastructure` implements persistence, caching, telemetry, and external clients. It references Core and Util.
- `GSS.Api` composes the application and owns HTTP concerns.
- Add business features as coherent slices. Do not reintroduce sample-domain artifacts; `scripts/verify-scaffold.ps1` and `ScaffoldCompositionTests` reject them.
- Add DbUp migrations only with the feature that needs them; keep scripts idempotent and reviewable.
- Keep SSO, ASM, MIS, caching, health, observability, and security middleware generic. Replace the placeholder `ModuleCode` values (`Application`, `Administration`) with the module codes configured in ASM.
- Doc examples use an illustrative domain (`Member`, `Item`, `Assignment`, ...) that is not shipped.

## Engineering conventions

- Use nullable reference types, async cancellation tokens, and immutable read models where practical.
- Add behavior tests first. Keep unit tests independent of infrastructure and reserve PostgreSQL for integration tests.
- Never place credentials in tracked files. Use environment variables, CI secrets, or Docker BuildKit secrets. Tracked settings hold placeholders only (`gss-local`, `set-via-environment`).
- Keep `GSS.slnx` at the approved eight-project inventory unless an architecture change is explicitly approved.

## Build & Run

```bash
dotnet build                                     # Build entire solution (GSS.slnx)
dotnet run --project src/GSS.Api             # Start API (https://localhost:7109, Swagger UI at /swagger)
dotnet format GSS.slnx                       # Auto-format (pre-commit hook verifies staged files)
export NUGET_AUTH_TOKEN=$(gh auth token) && docker compose up --build   # API + Postgres + Redis + OTel dashboard in containers
```

- `Directory.Build.targets` pins `LangVersion` 14.0 and `AnalysisLevel` 10.0 (= net10.0; bump both with `TargetFramework`). They live in targets because package props are imported after `Directory.Build.props`. `BuildSettingsTests` evaluates every project and checks that the Dockerfile copies every root `Directory.*` build file.
- CodeStyle.NET 2.0 supplies a global analyzer config, which `BuildSettingsTests` checks. The committed `.dockerignore` excludes host build outputs and local environment files from the Docker context.
- `Directory.Build.props` applies globally: .NET 10, `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, and NuGet vulnerability auditing (`NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low`). Every warning fails the build **except** NU1900-NU1904, which `WarningsNotAsErrors` keeps as warnings so a newly disclosed CVE or an unreachable feed cannot hard-break every build.
- **SDK version**: `global.json` `sdk.version`, the Dockerfile build image tag and the Jenkinsfile `dotnetVersion` must match; bump all three together (`SdkVersionAlignmentTests`).
- **Private NuGet feed**: `ApiPipeline.NET` (+ `.Versioning`), `Caching.NET`, `CodeStyle.NET` (analyzers), `HealthChecks.NET` (+ `.Redis`), `HttpResilience.NET` and `OpenTelemetry.NET` come from `baps-apps` GitHub Packages via `nuget.config` package source mapping. Restore needs credentials for that feed (a token with `read:packages`).
- **Compose** (`compose.yaml`, guide `.docs/guides/docker-compose.md`): API on `https://localhost:7109` (self-signed cert generated into a volume), Postgres on host port 5433, Redis 6380, Aspire dashboard `http://localhost:18888`. `NUGET_AUTH_TOKEN` is passed to the Docker build as a BuildKit secret. Compose does not warn when it is unset.

## Testing

```bash
dotnet test                                      # All tests: unit + integration + architecture
dotnet test tests/GSS.UnitTests              # Unit only (the project holds only unit tests)
dotnet test tests/GSS.IntegrationTests       # Integration (needs a local PostgreSQL, see below)
dotnet test tests/GSS.ArchitectureTests      # Layer-boundary compliance (NetArchTest)
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"   # Single test
pwsh scripts/run-tests.ps1 -TestType Unit        # Wrapper: -TestType Unit | Architecture | Integration, optional -CollectCoverage (Unit also runs the architecture tests)
pwsh scripts/run-coverage.ps1 -TestType All -ReportType Html     # Coverage (-TestType Unit | Integration | All, -ReportType Summary | Html)
```

- Each test project holds one kind of test (unit, integration, architecture), so no run settings filter is needed: a project-level `dotnet test` runs its own kind and a solution-level one runs everything.
- Tests carry `[Trait("Category", TestCategories.Unit | Integration)]`.
- Test stack: xUnit, Moq, FluentAssertions, Microsoft.AspNetCore.Mvc.Testing, NetArchTest. **No Testcontainers** — it is neither declared in `Directory.Packages.props` nor referenced by any project.
- Unit tests that touch caching use a real in-memory `ICacheService` from `tests/GSS.UnitTests/Common/TestCache.cs`; mock `ICacheService` only to inject failures or to record the expiration a service requests (a mock forwarding to `TestCache`, e.g. `SsoServiceTests.CaptureLifetimesAsync`). The cache runs on the real clock (Caching.NET takes no `TimeProvider`), so never wait for an entry to expire: assert the requested lifetime, computed from the fake `TimeProvider`.

### Integration Test Database

- One source for the test database: `Fixtures/TestDatabaseSettings.cs` reads `tests/GSS.IntegrationTests/appsettings.Testing.json` (default `gss_test` on `localhost:5432`), overridden by `INTEGRATION_TEST_DB_HOST/_PORT/_NAME/_USER/_PASSWORD`. It never reads `appsettings.json` and refuses any database whose name lacks `test` (tests TRUNCATE every table).
- `WebAppFactory` always writes those settings into the app's Read and Write connection config, so the API under test, `ResetDatabaseAsync()` and the repository `TestDatabaseFixture` all hit the same database. `TestDatabaseIsolationTests` guards this. The factory also disables caching, rate limiting and OpenTelemetry, fakes SSO, and trusts `10.0.0.0/8` as a proxy (`Testing` is strict for ApiPipeline.NET's guards).
- `WebAppFactory.EnsureTestDatabaseExists()` and `TestDatabaseFixture.InitializeAsync()` create the test database (test-only; the API never creates databases). `ResetDatabaseAsync()` starts the host first, so its startup migrations create the schema on a brand-new test database. Both fixtures reset through `Fixtures/TestDatabaseReset`: every `gss` table except `schemaversions`, with no hard-coded feature-table list. Tests share the `IntegrationDatabase` collection.
- A failing migration throws from host startup, so the first test touching the factory fails with the failing script's name; check the DbUp log lines in the test output.

## Code Quality

```bash
pwsh scripts/security-audit.ps1 -Fast            # NuGet vulnerability check
pwsh scripts/security-audit.ps1                  # Full audit (credentials + secrets + NuGet)
pwsh scripts/sonarqube-scan.ps1                  # SonarCloud SAST (requires SONAR_TOKEN)
pwsh scripts/install-pre-commit-hook.ps1         # Install format-check hook
```

The pre-commit hook runs `dotnet format --verify-no-changes --include <staged .cs files>` against every solution file in the repo root and blocks the commit on any needed change or warning. It never formats or stages files (partial `git add -p` stages stay intact); run `dotnet format` yourself and re-stage. Vulnerability scanning is NOT in the hook.

## Architecture

Clean Architecture, 5 source projects + 3 test projects (`GSS.slnx`):

```text
GSS.Api → GSS.Business → GSS.Core
GSS.Api → GSS.Infrastructure → GSS.Core
GSS.Api, GSS.Business, GSS.Infrastructure → GSS.Util
```

- **Core**: Entities (none ship yet; they extend `EntityBase`: Id, CreatedAt, UpdatedAt, IsActive, CreatedBy, UpdatedBy), repository interfaces, external-service interfaces, `CacheKeys`. No package dependencies besides analyzers. MIS models (`PersonPositionModel`, `DepartmentModel`, `RoleModel`, `RoleTypeModel`, `PositionModel`) carry `[JsonPropertyName]` with MIS's wire names.
- **Business**: Services extend `EntityServiceBase<TEntity, TDto, TCreateDto, TUpdateDto>`, which implements the shared `IEntityService<TDto, TCreateDto, TUpdateDto>` CRUD contract; feature interfaces add only feature-specific queries. DTOs are `sealed record`s with `init`; Mapster `IRegister` configs compile at startup in strict mode; `Result<T>` represents explicit success/failure.
- **Infrastructure**: Dapper repositories (`DapperRepositoryBase<T>` for writes), DbUp migrations, external HTTP services, Unit of Work / transactions, Caching.NET registration.
- **Api**: Controllers (extend `ApiControllerBase`), `Authentication/` (`SsoAuthenticationHandler`), `Authorization/` (`[AsmAuthorize]`, `AsmPermissionHandler`), filters (`ValidationFilter`), middleware (`ApiExceptionHandler`, `ApiErrorProblemDetailsService`), HostedServices, `Extensions/` classes that orchestrate DI and the pipeline.
- **Util**: Configuration models (`Util/Models`), `ConfigurationKeys`, security helpers (`JwtTokenHelper`, reading tokens with `JsonWebTokenHandler`; not the legacy `JwtSecurityTokenHandler`). No database provider: connection strings are built in Infrastructure (`DatabaseConnectionStrings`), and `DependencyTests` fails if Core, Business or Util references Npgsql.

`GSS.ArchitectureTests` enforces layer boundaries, naming and structure with NetArchTest; violations fail CI. Api references Infrastructure for composition only; controllers, filters, handlers and middleware use Business services (`ApiLayerDependencyTests`).

### External Services (SSO, MIS, ASM) Are Two Layers With the Same Interface Names

`Core/Interfaces/Services/I{Sso,Mis,Asm}Service` is implemented by `Infrastructure/Services/External/*` (typed HTTP clients registered in `Infrastructure/Extensions/HttpClientRegistrationExtensions.cs` as `AddHttpClient<ICore, Impl>("Name")`: transient, so never inject one into a singleton, and no separate `AddScoped`). Infrastructure services extend `HttpServiceBase`, which has one send path (`GetAsync`, `PostAsync`, `PostWithoutBodyAsync`) and the MIS/ASM envelope helpers `GetListAsync<T>` / `GetSingleOrNullAsync<T>` (shared `ExternalApiResponse<T>`, registered per `T` in `JsonContext`; no body or `succeeded: false` throws 502, never an empty list; the single lookup matches the id itself and maps a 404 to null). Service URLs are `[ExternalHttpsUrl]` (HTTPS, no loopback/private IP; `Util/Validation/UrlValidator`), so a bad one fails startup naming the key; registration re-checks it. Endpoints are relative to the client's `BaseAddress` (given a trailing slash at registration); endpoint options carry `[RelativePath]` (no leading slash, never absolute), validated at startup through the options class's `IValidatableObject` (`NestedValidation`, since `ValidateDataAnnotations()` skips nested objects). App credentials (`x-app-auth-*`, `x-baps-auth-app-*`; `{Mis,Asm}Service:Headers:AuthAppId`/`AuthAppSecret`, `[Required]` and validated at startup through the same `NestedValidation`, so a missing one fails startup naming the key, and registration throws on a blank one instead of leaving the header out) are `DefaultRequestHeaders` set once at registration; services add only per-call headers such as the bearer token (`HttpClientRegistrationTests`). `Business/Services/Interfaces/I{Sso,Mis,Asm}Service` is a *different* interface implemented by `Business/Services/*`, which wraps the Core service, maps models to DTOs and adds caching. Controllers and filters depend on the Business interface. See `.docs/architecture/service-registration-layering.md`.

### Request Pipeline and Security State

- Inbound middleware is ApiPipeline.NET (`.docs/architecture/api-pipeline.md`): `AddApiPipeline(configuration)` in `ServiceExtensions`, one `UseApiPipeline(...)` in `MiddlewareExtensions.ConfigureMiddleware`, all settings in the `ApiPipeline` section. Order: ForwardedHeaders → CorrelationId → SecurityHeaders → exception handling → status-code pages → HTTPS-only → Swagger UI → CORS → Authentication → RateLimiting → Compression → Authorization. `UseStatusCodePages()` sends empty-bodied framework 4xx/5xx responses through `ApiErrorProblemDetailsService`; `SuppressMapClientErrors` prevents MVC from producing a second ProblemDetails contract. Project middleware goes in via `WithMiddleware(...)`.
- `UseHttpsOnly` (after forwarded headers and exception handling) answers plain HTTP with a 400 `Response<ApiError>` envelope (via `IProblemDetailsService`) in every environment except `Testing`; it sees the client's scheme via `X-Forwarded-Proto` from trusted proxies only. Only the `/internal/health/*` probe routes are allowed over HTTP (the `/health*` and `/healthz*` aliases are not).
- Authentication is the `SSOv1` scheme (`Api/Authentication/SsoAuthenticationHandler.cs`, registered in `Api/Extensions/Features/AuthenticationExtensions.cs`) with a fallback policy requiring an authenticated user; opt out with `[AllowAnonymous]` / `.AllowAnonymous()` (health probes, OpenAPI document, SSO renew and logout; Swagger UI is middleware before authentication). SSO renew accepts an expired access token, so it reads the raw `Authorization` header (JWT-shaped only; SSO checks both tokens), never `IUserContext.GetToken()`, and is marked `[ReadsBearerToken]` so OpenAPI documents a bearer-only requirement. SSO v1 tokens are unsigned, so each token is validated by a remote SSO call; the Business `SsoService` alone caches the result (`CacheKeys.TokenValidation`, single-flight `GetOrSetAsync`: valid until `exp`, a rejection for 5 s) and clears token-keyed entries on logout. The handler parses the token once; Business services that cache per token (`SsoService`, `AsmService`, `MisService`) take its expiry as a `tokenExpiresAtUtc` parameter (from the handler, or the `exp` claim via `IUserContext.GetTokenExpiresAtUtc()` / `JwtTokenHelper.GetTokenExpiration(principal)`) and never parse the token themselves. Only an SSO 400/401/403 rejects a token (401; a 404/405 means a wrong URL or endpoint and, like 408/429/5xx, is an outage), logged once as the handler's Warning (the SSO services log it at Debug); an SSO outage propagates as `ExternalServiceException` and the handler's challenge answers 503 instead, and any other validation error (a bug, a bad SSO payload) answers 500 with no `WWW-Authenticate` (never 401, which makes clients log users out; nothing cached, `[AllowAnonymous]` endpoints unaffected). On success: `HttpContext.User` (`pid` → `ClaimTypes.NameIdentifier`) plus the token in `Items[HttpContextItemKeys.UserToken]` (`Util/Security`, shared with Infrastructure's `UserContext`); `IUserContext.GetUserId()` reads only the authenticated `NameIdentifier` claim. Authentication runs before rate limiting, so limits partition per user. Integration tests swap `ISsoService` for `Fixtures/FakeSsoService` and send `FakeSsoService.ValidToken` by default. Module permissions use `[AsmAuthorize(ModuleCode, AccessType, ...)]` (multiple attributes = AND, extra access types = OR), checked by `AsmPermissionHandler` against the single position selected by `x-app-position-id`/`-role-id`/`-division-id`/`-department-id` (validated against MIS person positions, then the matching ASM entry); no on/off switch, no super-admin bypass. See `.docs/architecture/asm-authorization.md`. See `.docs/architecture/sso-authentication.md`.
- Rate limiting: policies `global` (default and global), `strict` (`[EnableRateLimiting("strict")]` on SSO logout) and `renew` (SSO renew, keyed per bearer-token hash by `Api/RateLimiting/RenewTokenPartitionKeyResolver`, because an expired token leaves the caller anonymous and IP keying would make a whole NAT share 10/min), per pod. Every `[EnableRateLimiting]` name must be in `ApiPipeline:RateLimiting:Policies` (`ConfigurationFileTests`). `ApiPipeline:RateLimiting:Enabled=false` turns it off, which fails startup outside Development and `Testing`.
- Validators from Business and Api assemblies are registered as singletons and run asynchronously by `ValidationFilter`. A collection argument is capped at `BatchLimits.MaxBatchSize` before item validation; null items are rejected and item errors are keyed `[i].Property`. `BatchUpdateRequestValidator<T>` is registered per closed type and chains `T`'s validator for `Data`. FluentValidation is the only request validator; MVC DataAnnotations validation is disabled.

### Configuration

Settings come from `src/GSS.Api/appsettings.json` only (development-flavoured: localhost DB, `CacheOptions:Enabled=false`), overridden by environment variables (`Section__Key`) and the command line. There are **no** `appsettings.{Environment}.json` files; `ConfigurationFileTests` fails if one appears under `src/`. Deployments and Compose override via environment variables.

**One environment switch: `AppSettings:Environment`** (`Development`, `Testing`, `QA`, `UAT`, `Production`; validated at startup). It drives DbUp seeds, HTTPS enforcement and Swagger UI/OpenAPI (off in Production) through `EnvironmentExtensions` / `AppSettingModel`. App code never reads `IHostEnvironment` or `ASPNETCORE_ENVIRONMENT`; `Program` calls `app.UseConfiguredEnvironment()` after `Build()` to copy `AppSettings:Environment` into `IHostEnvironment`, because ApiPipeline.NET (CORS allow-all, HSTS, startup guards) reads it and is lenient only in `Development`. `WebAppFactory` sets it to `Testing` with an in-memory override.

### Startup Sequence

1. `ValidateCachingRegistration()` resolves every cache (in `Program`, before `StartAsync`).
2. `DatabaseConnectionValidationHostedService` opens read and write connections (fail fast). The database must already exist: the API never creates one, and a missing database (SQLSTATE `3D000`) fails startup with its name and server (`DatabaseConnectionValidator`). A pool budget overrun logs a warning. `BehindConnectionPooler=true` skips the budget check.
3. `DatabaseMigrationInitFilter` (an `IStartupFilter`) runs DbUp migrations (each script in its own transaction; a `-NoTransaction.sql` script, e.g. `CREATE INDEX CONCURRENTLY`, outside one; every statement with `lock_timeout` `DatabaseMigration:LockTimeoutSeconds`, default 5, retried `LockTimeoutAttempts` times) under a PostgreSQL advisory lock (key derived from the journal identity by `MigrationJournal`; `AdvisoryLockTimeoutSeconds`, default 60), journaling into `gss.schemaversions` (not DbUp's default `public`; a legacy `public.schemaversions` is copied over once, so nothing re-runs) in the existing database (no `EnsureDatabase`, so the app identity needs no `CREATEDB`), throws on migration/seed failure or lock timeout (host fails to start; no `Environment.Exit`), then reloads Npgsql's cached types. The reload is required: step 1 caches the type catalog before migrations create enum types (`gender`, `category`), which otherwise breaks reads of those columns until restart.
4. Seed scripts for the current `AppSettings:Environment` run from `Infrastructure/DbUp/Seeds/{Environment}/` (the scaffold ships none; add them with the feature that needs them).
5. `ApplicationStarted` calls `IStartupReadinessMarker.MarkReady()`; until then the startup and readiness probes return 503.

## Database

- PostgreSQL with Dapper (not EF Core). Schema `gss`.
- Reads: hand-written SQL in each repository with quoted columns aliased to property names, mapped by Dapper. Each repository declares the column list once (`private const string Select = "SELECT ... FROM ..."`, returned by the base's abstract `SelectSql`); the base implements `GetByIdAsync`/`GetAllAsync`/`GetPagedAsync`/`FindByIdsAsync` from it (never copy them into a repository), and every other read is `Select + @" WHERE ..."`, run through the base helpers `QueryAsync<T>` / `QueryFirstOrDefaultAsync<T>(sql, param, ct)`. Repositories never open a connection: the helpers use a new read connection (disposed) outside a transaction and the Unit of Work transaction's connection, enlisted, inside one, so a read sees the transaction's uncommitted writes. List reads return `IReadOnlyList<T>`.
- Writes: shared `DapperRepositoryBase<T>` in `Infrastructure/Repositories/Base/`. Writes run immediately; use `IUnitOfWork` for multi-step transactions. A transaction has one connection, so its calls run one at a time: the base repository takes `IDapperTransactionManager.EnterCommand()` around each command on it, so parallel calls (`Task.WhenAll`) inside a Unit of Work fail at once naming the cause, and `ParallelCallTests` allows `Task.WhenAll`/`WhenAny`/`Parallel.*` in `src/` only in reviewed files (`MisService`: HTTP and cache). `HttpServiceBase` refuses to send while the scope's transaction is open (it would hold the connection and row locks across the network call): call external services before the Unit of Work. Batch writes are set-based `unnest` statements generated once per concrete repository type from `BatchColumns`, with SQL independent of batch size. The cap is `BatchLimits.MaxBatchSize`, enforced by the validation filter, entity service, and repository. `EntityServiceBase` calls range writes directly and takes no `IUnitOfWork`; use `unitOfWork.ExecuteInTransactionAsync(operation, logger, ct)` for several writes or services. The batch UPDATE first locks the active rows in id order (a CTE `FOR NO KEY UPDATE`, counted in a one-time filter before any write), so overlapping batches never deadlock. A batch update writes nothing unless every id is active and distinct (`UpdateRangeAsync` returns false; the service answers `NotFound` naming the rows deleted meanwhile); a batch delete skips rows already deleted. A single `UpdateAsync` on a row deleted by a concurrent request returns false (404), a single `DeleteAsync` is a no-op (success): a race is never a 500.
- Each concrete repository type's generated SQL is cached statically. `TableName`, `Schema`, `SelectSql`, `BatchColumns`, `BuildInsertSql` and `BuildUpdateSql` therefore must not depend on instance or mutable state.
- Use `GetPagedAfterAsync` for an indexed ID cursor and `GetPagedAsync` for page numbers. Both return `PageSlice<T>` and fetch one extra row to detect another page. Counts are opt-in. For composed writes returning `Result<T>`, use the typed transaction overload so a failed result rolls back earlier writes.
- Migrations: embedded SQL under `Infrastructure/DbUp/Migrations/`, named like `yyyyMMdd-HHmmss-TICKET-NNN-Description.sql` (the scaffold ships only the generic `Idempotency-Key` storage script); toggle with `DatabaseMigration:Enabled`.
  - DbUp journals by file name: never rename, delete or edit an applied migration or seed. Fix it with a new script. Add each script and its SHA-256 to `KnownScripts` in `tests/GSS.ArchitectureTests/MigrationScriptTests.cs` (append-only).
  - Scripts must be re-runnable. Catalog names (`pg_constraint.conname`, `pg_type.typname`) are lower case, so guards compare with `lower('FK_…')`. Old pods still serve during a rolling deploy, so each script must keep the old code working (expand/contract; see `.docs/guides/dbup-migrations.md`).
- Money amounts are `numeric(19,4)` (`decimal` in C#), never `MONEY`: MONEY parses and prints through `lc_monetary`, so its text form is locale-dependent (`DatabaseMigrationIntegrationTests.NoColumn_UsesTheMoneyType` fails on any MONEY column in `gss`). Seed amounts are plain numbers (`147.00`, not `'$147.00'`).
- Soft delete via `IsActive`.
- Connection settings: one section, `DbConnectionSettings` (`Read`/`Write`), bound to `IOptions<DbConnectionModel>` and validated at startup by `DbConnectionModelValidator` (`ValidateOnStart`; a missing host, database or user, a bad port, an unknown `SslMode`, an out-of-range pool/timeout value or `MinPoolSize` above `MaxPoolSize` fails startup naming the key; a mistyped `SslMode` never falls back to another mode). Never read it from raw `IConfiguration`.
- Pool sizing defaults to 20 maximum and 5 minimum when the settings are omitted. Configure `MaxReplicas` for peak rollout capacity. Startup warns if the combined pool limits exceed available PostgreSQL connections. Set `BehindConnectionPooler` when connecting through a pooler.
- Read/write connection separation via `IDapperConnectionFactory`, which returns `DbConnection`s from one `NpgsqlDataSource` per connection string (identical read/write settings share one data source and pool), built with `UseLoggerFactory` so Npgsql logs go through the app's logging; parameter logging stays off. Npgsql's spans are exported (`OpenTelemetryOptions:EnableNpgsqlInstrumentation`); Unit of Work via `IDapperTransactionManager`. Enum columns stay strings with `CAST(@X AS gss.<enum>)`: Dapper sends C# enum parameters as integers even with `MapEnum` or a type handler.

## Key Patterns

- **Responses**: every endpoint returns `Response<T>` (`Succeeded`, `Message`, `Data`, `Errors`), built only with `Response<T>.Success`/`.Failure` (no public constructors, init-only properties); errors use `ApiError` (`ErrorId`, `StatusCode`, `Message`). `ErrorId` is the request's correlation id (`ApiErrorId.For(HttpContext)`, = `X-Correlation-Id` header and `CorrelationId` log property); never `Guid.NewGuid()`. ProblemDetails is deliberately not used (`.docs/architecture/error-contract.md`); don't add `AddProblemDetails`.
  - Services return `Result<T>` (single and batch creates, batch updates and batch deletes included; a batch update or delete is all or nothing: duplicate IDs → `Failure`, any missing ID → `NotFound` naming them, and batch delete treats an already-deleted ID as deleted, like single delete): `NotFound` → 404, `Failure` → 400, `Conflict` → 409 (e.g. duplicate email), mapped only by `ApiControllerBase.ErrorResponse`. `default(Result<T>)` is no outcome and throws.
  - `EntityServiceBase`'s CRUD methods are not virtual; never hide one with `new`. Entity rules go in its hooks, which every single and batch path runs (a single call is a batch of one; check the batch in as few queries as possible, e.g. `FindMissingAsync(repository, ids)` for referenced rows):
    - `ValidateCreateAsync(dtos)` before any insert;
    - `ValidateUpdateAsync((Current, Dto) pairs)` for update, patch and batch update, after the rows are loaded once and before any change (a null DTO field keeps the current value);
    - `ValidateDeleteAsync(entities)` before soft delete, including batch delete. Use `RuleViolation.ConflictIfAny` when active dependencies block deletion;
    - `OnCreating(entity, dto)` for server-owned fields.

    A hook returns a `RuleViolation` (`Invalid` → 400, `Conflict` → 409) or null. Business-rule failures (e.g. a referenced record that does not exist) return `Result.Failure`, never `throw new ArgumentException`: `ApiExceptionHandler` maps `ArgumentException` to 500 (a failed guard is a bug). Reject bad input at the edge with validators or route constraints (`{id:int:min(1)}` on every `int` route value, `RouteConstraintTests`, `{name:regex(\\S)}` for string route values).
  - Exceptions: the framework `UseExceptionHandler()` runs `ApiExceptionHandler` (an `IExceptionHandler`, registered with `AddExceptionHandler`), which logs once and writes the envelope with the app's HTTP JSON options; don't add a custom exception middleware. Its `Classify` picks status, client message and log level from the exception type, never message text. A Kestrel `BadHttpRequestException` (truncated/malformed or too-slow body, too large) is the client's fault: its own 4xx (413 Warning, others Information), never a 500. Exception messages are never returned; expected outcomes are `Result<T>` (a missing resource is `Result.NotFound`, never an exception); an `InvalidOperationException`, `ArgumentException`, `UnauthorizedAccessException`, `KeyNotFoundException` (a dictionary-indexer bug) or a cancellation the client did not cause is always 500. A `DbException` with SQLSTATE `23505` (a unique index caught a race) is 409 at Information; `40P01`/`40001` (deadlock, serialization failure) are 409 at Warning, after `DapperRepositoryBase.WriteBatchAsync` retried a batch in its own transaction once (never inside a Unit of Work). Outbound (SSO/MIS/ASM) failures reach it as Core `ExternalServiceException` (anywhere in the inner-exception chain), thrown by `HttpServiceBase` via `HttpErrorHandler.ToExternalServiceException` (HttpResilience.NET's `HttpResilienceFailure.Classify`; the original exception stays as `InnerException`, and the upstream's sanitized error body is in a non-2xx `HttpRequestException`'s message). `HttpErrorHandler` and `HttpServiceBase` never log a failure; it is logged once where it is handled. The handler maps `Timeout` 504, `Unavailable` (circuit open / rate-limited) 503 with `Retry-After` when known, `Failed` 502. Only Infrastructure may use Polly or HttpResilience.NET types (`DependencyTests`).
- **API versioning**: deprecation and sunset dates live in `ApiPipeline:VersionDeprecation` (ApiPipeline.NET.Versioning, `AddApiPipelineVersionDeprecation` in `ApiVersioningExtensions`); Asp.Versioning writes the RFC 9745/8594 headers, so keep `ReportApiVersions` on and add no deprecation middleware. Every route template is `api/v{version:apiVersion}/...`, so the URL segment is always required; the `api-version` header must match it. `AssumeDefaultVersionWhenUnspecified` is off. API Explorer groups as `v1` for the OpenAPI document.
- **Empty results**: a list endpoint returns 200 with `[]` when nothing matches; 404 only when a resource named in the route does not exist.
- **Pagination/OpenAPI**: `MultipleOkResponsesOperationTransformer` documents list-or-page 200 responses as `oneOf`. `ApiSecurityOperationTransformer` adds the bearer requirement to every non-anonymous action; the four `x-app-*` headers appear only on actions marked `[RequiresCallerHeaders]` or `[AsmAuthorize]`. It also adds the applicable 401/403/429/500/502/503/504 responses without replacing action-declared responses.
- **OpenAPI text**: every `src/` project sets `GenerateDocumentationFile` (`Directory.Build.props`; CS1591/CS1573 off), so .NET 10 copies controller and DTO `///` comments into the document. They are public API text: no doc links, C# signatures or implementation notes; developer notes go in `//` comments. `OpenApiIntegrationTests` requires a summary on every operation and rejects `.docs`, `.md`, `<` and `&lt;` in public text.
- **HTTP caching**: make a GET browser-cacheable with `[ClientCache]` (`Api/Filters`): a 200 gets `private, no-cache` and a weak ETag of the JSON body (serialized once; the filter writes those bytes itself, JSON only), and a matching `If-None-Match` gets 304, so nothing is stale after a write and 404s/errors keep `no-store`. `[ResponseCache]` only as `NoStore` (`StructureTests.ResponseCache_IsOnlyNoStore`): it applies to every status and `max-age` serves stale data. Never `public` (all responses are authenticated). Responses without either get `no-store` from ApiPipeline.NET.
- **Idempotent POST**: every create route a feature adds (single and `.../batch`) carries `[Idempotent]` (`Api/Filters`, a resource filter; `.docs/architecture/idempotency-analysis.md`); the scaffold has none yet, and the integration tests use a probe controller. A request with an `Idempotency-Key` header (1-255 printable ASCII, else 400; absent = runs as before unless `Required = true`) runs once per user and key: Business `IdempotencyService` opens the request's Unit of Work transaction, takes a per-key advisory lock and replays a stored 2xx response (`Idempotent-Replayed: true`) for the same request hash, 422 for a different one; the create and the stored response (`gss.idempotencykey`) commit together, any non-2xx rolls back and frees the key. Never on an action that calls SSO/MIS/ASM (`HttpServiceBase` refuses to send inside a Unit of Work). `IdempotencyCleanupHostedService` deletes rows older than 24 hours.
- **Concurrent updates are last write wins**: PUT, PATCH and DELETE have no version, `ETag` or `If-Match` (decided against; `.docs/architecture/idempotency-analysis.md`, "Concurrent Updates", has what already protects writes, why, and the design to follow if a UI or an absolute-value field such as a quantity needs it). Do not add it unasked.
- **Caching**: `ICacheService` comes from the `Caching.NET` package (there is no project-owned cache abstraction), registered with `AddCaching(configuration)` from the `CacheOptions` section.
  - Never wrap cache calls in try/catch (no swallow, no "cache down: call directly" fallback). Caching.NET already degrades Redis/serialization/backplane failures to a miss while its `ThrowOn*` switches stay off (`ConfigurationFileTests` fails on `ThrowOn*` or `UseDevelopmentDefaults`), so an exception from `GetOrSetAsync` is the factory's own failure, and a fallback would repeat the external call.
  - Build keys with `CacheKeys` and include every parameter that changes the result.
  - Set `FailSafe = false` for authentication and permission data.
  - Set `FailSafeMaxDuration` longer than the expiration, or fail-safe has no effect.
  - To choose a lifetime from the result (e.g. short TTL for negative results), use the context-taking `GetOrSetAsync<T>(key, (ctx, ct) => ...)` and set `ctx.Overrides`.
- **Resilience**: HttpResilience.NET (private feed) for outbound HTTP: `AddHttpResilience(configuration)` once plus `AddHttpResilienceHandler()` per client, all settings in the `HttpResilience` section (`.docs/architecture/resilience.md`). Never add `AddStandardResilienceHandler`, Polly v7 handlers, `client.Timeout` or a hand-built `SocketsHttpHandler`. Only GET/HEAD/OPTIONS/TRACE are retried, so SSO (all POST) is not. Size limits: 1 MB request body (`ApiPipeline:RequestLimits:MaxRequestBodySize`), 10 MB response buffer per client (`MaxResponseContentBufferSize`, set from the `MaxResponseBytes` constant).
- **Health checks**: HealthChecks.NET (`Api/Extensions/Features/HealthCheckExtensions.cs`). Probes are `/internal/health/{liveness,startup,readiness}` plus fixed `/health*` and `/healthz*` aliases. Liveness must stay process-only: add dependency checks through the `AddApiHealthChecks()` builder (`ready` tag), never on the framework `IHealthChecksBuilder`. Database read/write use `AddCustomCheck` because `AddPostgreSqlCheck` allows only one check. Redis is registered only when caching runs in Redis/Hybrid mode and reports `Degraded`.
- **Not-found logging**: a controller 404 is logged once, at Debug, by `ApiControllerBase.HandleNotFound` (`Api/Helpers/ControllerLog.NotFound`, `[LoggerMessage]` EventId 2000, one template for every entity) with the base's `Logger` (the concrete controller's category, resolved from the request). Controllers never log not-found themselves, and the `*OrNotFoundAsync` helpers take no logging callback.
- **Service write logging**: `EntityServiceBase` logs each write once at Information through `EntityServiceLog` (EventIds 3000+), with one `Entity` property plus `EntityId` or `Count`.
- **Observability**: OpenTelemetry.NET (tracing, metrics, logs over OTLP to `OpenTelemetryOptions:CollectorUrl`); `X-Correlation-Id` (caller's when well-formed, else generated; log scope `CorrelationId`) and `X-Trace-Id` come from ApiPipeline.NET; its meter is exported via `OpenTelemetryOptions:Meters`.

## Conventions

- `CancellationToken` on async methods; `ConfigureAwait(false)` on awaits in Business and Infrastructure is a review convention (CA2007 is not build-enforced).
- **UTC everywhere** (`.docs/standards/utc-time-guidelines.md`): never `DateTime.Now`/`ToLocalTime()`; read "now" only from the injected `TimeProvider` (expiry/cache math, audit stamps), never `DateTime.UtcNow` (`ClockUsageTests` fails on `DateTime`/`DateTimeOffset` `.UtcNow`/`.Now`/`.Today` anywhere under `src/`). JSON and query `DateTime` without an offset are UTC (`UtcDateTimeJsonConverter`, `UtcDateTimeModelBinder`), and Npgsql sessions run with `Timezone=UTC`.
- Most files inherited from the template use **CRLF** line endings (`.gitattributes` treats CR as part of the line ending); preserve the existing ending when rewriting files with scripts, or the whole file shows as changed.
- Central Package Management: versions live in `Directory.Packages.props`; `.csproj` files reference packages without `Version`.
- Injected dependencies are required: no `= null` on a constructor parameter that DI fills (repositories, services, loggers, `TimeProvider`). The container passes null for an unregistered optional parameter, so a missing `IDapperTransactionManager` registration would silently run writes outside the Unit of Work. `ConstructorInjectionTests` enforces it.

## DI Registration

- `Infrastructure/DependencyInjection.cs` (`AddInfrastructure`): Caching.NET, data access (connections, repositories), DbUp startup filter, typed HTTP clients.
- `Business/DependencyInjection.cs`: Mapster configuration and services.
- `Api/Extensions/Core/ServiceExtensions.cs` (`ConfigureApiServices`): Api-layer services (rate limiting, security headers, versioning, compression, controllers/validators, OpenAPI, health checks, filters), CORS, then the DB validation hosted service, `AddInfrastructure` and `AddBusinessServices`.

## Documentation

`.docs/` holds architecture, guides, standards and testing docs; `.docs/README.md` is the index and must list every topic doc. Docs follow `.docs/documentation-standards.md` (title, back link, conditional TOC, Overview, final references). Illustrative feature examples must be labeled; repository paths, commands, configuration keys, measurements, and status claims must be verified against the current GSS implementation.
