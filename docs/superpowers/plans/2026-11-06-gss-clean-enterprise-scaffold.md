# GSS Clean Enterprise Scaffold Implementation Plan

> **Archived implementation record (2026-11-06).** This plan records how the GSS scaffold was created from its source template. It is not current guidance, and unchecked boxes do not indicate current implementation status. See the [current documentation](../../../.docs/README.md) and [repository guidance](../../../CLAUDE.md).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a domain-neutral .NET 10 Clean Architecture API repository named GSS from the `boilerplate-net` enterprise template.

**Architecture:** Mirror and rename the template's five source layers and three test projects, then remove the commerce vertical slice at its composition seams while retaining shared infrastructure and enterprise integrations. Lock the result down with architecture tests and a repository-level scaffold verification script so deleted sample concepts and source-template names cannot drift back in.

**Tech Stack:** .NET 10, ASP.NET Core, Dapper/Npgsql, DbUp, Caching.NET, SSO/MIS/ASM HTTP clients, OpenTelemetry.NET, Serilog, FluentValidation, Mapster, xUnit, Moq, FluentAssertions, NetArchTest, Docker Compose, PowerShell.

**Spec:** `docs/superpowers/specs/2026-11-06-gss-clean-enterprise-scaffold-design.md`

## Global Constraints

- Use `/Users/vishalpatel/Projects/boilerplate-net` as the read-only source template; never copy its `.git` directory or generated files.
- Target `net10.0` with SDK `10.0.100`, central package management, nullable references, analyzers, code-style enforcement, and warnings as errors unchanged from the template.
- Produce exactly five source projects: `GSS.Api`, `GSS.Business`, `GSS.Core`, `GSS.Infrastructure`, and `GSS.Util`.
- Produce exactly three test projects: `GSS.UnitTests`, `GSS.IntegrationTests`, and `GSS.ArchitectureTests`.
- Preserve dependency direction: `Api -> Business -> Core`, `Api -> Infrastructure -> Core`, and `Api|Business|Infrastructure -> Util`.
- Retain SSO, MIS, ASM, caching, PostgreSQL/Dapper, DbUp, resilience, versioning, validation, rate limiting, security headers, health checks, OpenTelemetry, Serilog, OpenAPI/Scalar, Docker, Jenkins/Sonar, auditing, and test infrastructure.
- Remove Address, Article, Color, Customer, Label, Order, OrderPosition, Product, Size, Stock, their SQL, and all direct production/test artifacts.
- Keep private package-source mapping and never write feed tokens or other secrets to tracked files.
- Preserve CRLF in copied C# files unless a focused edit requires otherwise; never copy `.DS_Store`, `bin`, `obj`, or `TestResults`.

## Review Focus

- A clean checkout without GitHub Packages credentials must fail restore with an actionable feed/authentication error, never because `nuget.config` contains a secret; Task 1 verifies tracked configuration contains no credentials.
- Removing the commerce slice must not remove SSO/MIS/ASM or cache-management types and registrations; Task 2 adds `EnterpriseCapabilities_ShouldRemainAvailable`.
- A future commerce type must trip an automated guard even if it compiles; Task 2 adds `CommerceDomainTypes_ShouldBeAbsent` and Task 3 adds repository text/path scanning.
- Empty DbUp migrations must remain a valid embedded-resource setup using schema `gss`; Task 2 adds `DapperRepositoryBase_ShouldDefaultToGssSchema` and compiles the migration project with empty migration/seed folders.
- Renamed build, container, CI, scripts, and docs must never point at source-template paths or databases; Task 3's `verify-scaffold.ps1` checks all current-state artifacts while exempting decision records under `docs/superpowers/`.

---

### Task 1: Create the Renamed Enterprise Baseline

**Files:**

- Create: `GSS.slnx`
- Create: `.dockerignore`, `.env.example`, `.gitignore`, `.runsettings`, `.vscode/settings.json`
- Create: `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `nuget.config`
- Create: `Dockerfile`, `compose.yaml`, `Jenkinsfile`, `sonar-config.json`
- Create: `README.md`, `CLAUDE.md`, `.docs/**`, `scripts/**`
- Create: `src/GSS.{Api,Business,Core,Infrastructure,Util}/**`
- Create: `tests/GSS.{UnitTests,IntegrationTests,ArchitectureTests}/**`
- Preserve: `docs/superpowers/**`

**Interfaces:**

- Consumes: the source template filesystem and the approved design.
- Produces: a compiling exact-name baseline with `GSS.slnx` and eight `GSS.*.csproj` files, before domain removal.

- [ ] **Step 1: Mirror only source-controlled template content**

Copy the root operational files, `.docs`, `scripts`, `src`, and `tests` while excluding `.git`, `.DS_Store`, `bin`, `obj`, and `TestResults`; do not overwrite `docs/superpowers/`.

- [ ] **Step 2: Rename paths and textual identity mechanically**

Rename every source-template directory/file to `GSS*`, rename the source solution to `GSS.slnx`, replace the source application identity with `GSS`, and replace its lowercase database identity with `gss` in copied text files. Do not apply these replacements inside `docs/superpowers/`.

- [ ] **Step 3: Verify the solution inventory**

Run: `dotnet sln GSS.slnx list`

Expected: exactly the five source projects and three test projects named in Global Constraints; no source-template path.

- [ ] **Step 4: Verify configuration does not contain credentials**

Inspect `nuget.config`, `.env.example`, `compose.yaml`, and `Dockerfile`; assert the private source uses environment/BuildKit authentication and no token value is tracked.

- [ ] **Step 5: Restore and build the renamed baseline**

Run: `dotnet restore GSS.slnx --locked-mode` only if lock files exist; otherwise run `dotnet restore GSS.slnx`.

Run: `dotnet build GSS.slnx --no-restore`

Expected: build succeeds with zero warnings. If the private feed is unavailable, capture the exact authentication/network error, verify no source/configuration error precedes it, and continue only after obtaining the required access or confirming compatible packages already exist in the local cache.

- [ ] **Step 6: Commit the renamed baseline**

```bash
git add .
git commit -m "chore: scaffold renamed GSS enterprise baseline"
```

### Task 2: Remove the Commerce Slice and Retain Enterprise Composition

**Files:**

- Create: `tests/GSS.ArchitectureTests/ScaffoldCompositionTests.cs`
- Create: `tests/GSS.UnitTests/Common/Models/TestEntity.cs`
- Create: `tests/GSS.UnitTests/Common/Models/TestEntityDto.cs`
- Create: `tests/GSS.UnitTests/Common/Models/CreateTestEntityDto.cs`
- Create: `tests/GSS.UnitTests/Common/Models/UpdateTestEntityDto.cs`
- Create: `tests/GSS.UnitTests/Business/Services/CrudServiceBaseTests.cs`
- Modify: `src/GSS.Business/DependencyInjection.cs`
- Modify: `src/GSS.Business/Mappings/MapperConfiguration.cs`
- Modify: `src/GSS.Infrastructure/Extensions/DataAccessExtensions.cs`
- Modify: `src/GSS.Infrastructure/Repositories/Base/DapperRepositoryBase.cs`
- Modify: `src/GSS.Infrastructure/GSS.Infrastructure.csproj`
- Modify: `tests/GSS.UnitTests/Infrastructure/Repositories/DapperRepositoryBaseTests.cs`
- Modify: `tests/GSS.IntegrationTests/Fixtures/TestDatabaseFixture.cs`
- Modify: `tests/GSS.ArchitectureTests/{DependencyTests,DependencyGuardTests,NamingConventionTests,StructureTests}.cs`
- Delete: `src/GSS.Api/Controllers/{Address,Article,Color,Customer,Label,Order,Product,Size,Stock}Controller.cs`
- Delete: commerce DTOs, validators, mappings, services, and service interfaces under `src/GSS.Business/`
- Delete: commerce entities, enums, and repository interfaces under `src/GSS.Core/`
- Delete: commerce repositories under `src/GSS.Infrastructure/Repositories/`
- Delete: `src/GSS.Infrastructure/DbUp/Migrations/*.sql` and `DbUp/Seeds/Development/*.sql`
- Delete: direct commerce unit/integration tests and `tests/GSS.UnitTests/Common/Builders/TestDataBuilder.cs`
- Preserve: SSO, MIS, ASM, cache management, generic primitives, cross-cutting API infrastructure, DbUp engine, data-access helpers, and their tests.

**Interfaces:**

- Consumes: the eight-project GSS baseline from Task 1.
- Produces: domain-neutral production assemblies, `DataAccessExtensions.AddInfrastructureDataAccess(IServiceCollection)`, `DependencyInjection.AddBusinessServices(IServiceCollection)`, generic CRUD/repository bases, and retained enterprise registrations.

- [ ] **Step 1: Add failing scaffold composition tests**

Add these `[Fact]` tests to `ScaffoldCompositionTests`:

- `CommerceDomainTypes_ShouldBeAbsent`: reflect over Api, Business, Core, and Infrastructure assemblies and assert no concrete/interface type has any forbidden resource name (`Address`, `Article`, `Color`, `Customer`, `Label`, `Order`, `OrderPosition`, `Product`, `Size`, `Stock`) or corresponding controller/service/repository/DTO prefix.
- `EnterpriseCapabilities_ShouldRemainAvailable`: assert `SsoController`, `MisController`, `AsmController`, `CacheManagementController`, the three business service interfaces/implementations, the three core service contracts, and the three infrastructure external clients exist.
- `CleanArchitectureProjectReferences_ShouldMatchApprovedGraph`: inspect referenced assembly names and assert only the dependency directions from Global Constraints.

- [ ] **Step 2: Run the new architecture tests and verify the intended failure**

Run: `dotnet test tests/GSS.ArchitectureTests --no-restore --filter FullyQualifiedName~ScaffoldCompositionTests`

Expected: `CommerceDomainTypes_ShouldBeAbsent` fails and names the existing commerce types; retained-capability and dependency-graph assertions pass.

- [ ] **Step 3: Remove commerce production code and registrations**

Delete the files listed above; remove their `AddScoped` calls from Business and Infrastructure registration; reduce `MapperConfiguration.cs` to the generic Mapster assembly scan/compile extension; retain enterprise `CacheKeys` because it contains only ASM/MIS keys; remove commerce-only `DomainEnums.cs`.

- [ ] **Step 4: Neutralize the reusable Dapper base**

Confirm `DapperRepositoryBase<T>.Schema` is exactly `"gss"` after Task 1's mechanical rename; update its examples/comments to generic `IEntityRepository` language. Keep the DbUp engine and empty migration/seed folder declarations in `GSS.Infrastructure.csproj`.

- [ ] **Step 5: Replace commerce-dependent generic tests with test-only models**

Define `TestEntity : BaseEntity` with `string Name`, `TestEntityDto` with `int Id` and `string Name`, `CreateTestEntityDto` with `string Name`, and `UpdateTestEntityDto` with nullable `string Name` in `tests/GSS.UnitTests/Common/Models/`. Update `DapperRepositoryBaseTests.TestRepository` to inherit `DapperRepositoryBase<TestEntity>` with table `test_entities`. Add `CrudServiceBaseTests` using `TestCrudService : CrudServiceBase<TestEntity, TestEntityDto, CreateTestEntityDto, UpdateTestEntityDto>` and mocked `IRepository<TestEntity>`/`IUnitOfWork`; `ApplyPatch` changes `Name` only when the update value is non-null. Cover null create input, not-found lookup, empty batch input, and idempotent delete behavior.

- [ ] **Step 6: Add the schema regression test**

In `DapperRepositoryBaseTests`, expose `Schema` from `TestRepository` and add `DapperRepositoryBase_ShouldDefaultToGssSchema`, asserting exactly `"gss"`.

- [ ] **Step 7: Remove commerce tests and repair retained architecture tests**

Delete unit/integration tests whose subject is a removed commerce controller/service/validator/repository. Update remaining namespaces, assertion messages, and architecture expectations so empty concrete-entity/repository sets do not create false positives while `BaseEntity`, `IRepository<T>`, `DapperRepositoryBase<T>`, and layer boundaries remain guarded.

Update `TestDatabaseFixture.ResetDatabaseAsync()` to discover tables from `pg_tables` for schema `gss`, truncate the discovered quoted identifiers when present, and succeed without issuing `TRUNCATE` when the schema has no tables. This preserves isolation for future GSS migrations without hard-coding deleted commerce tables.

- [ ] **Step 8: Run focused tests**

Run: `dotnet test tests/GSS.ArchitectureTests --no-restore`

Run: `dotnet test tests/GSS.UnitTests --no-restore --filter "FullyQualifiedName~CrudServiceBaseTests|FullyQualifiedName~DapperRepositoryBaseTests|FullyQualifiedName~Services.External|FullyQualifiedName~Authentication"`

Expected: all selected tests pass, including the three review-focus tests.

- [ ] **Step 9: Build all projects**

Run: `dotnet build GSS.slnx --no-restore`

Expected: success with zero warnings; the integration-test project compiles even if PostgreSQL is not running.

- [ ] **Step 10: Commit the domain-neutral scaffold**

```bash
git add src tests GSS.slnx
git commit -m "refactor: remove source-template sample domain"
```

### Task 3: Align Operations, Documentation, and Repository Guards

**Files:**

- Create: `scripts/verify-scaffold.ps1`
- Modify: `README.md`, `CLAUDE.md`, `.docs/README.md`
- Modify: retained `.docs/architecture/**`, `.docs/guides/**`, `.docs/standards/**`, `.docs/testing/**`
- Modify: `Dockerfile`, `compose.yaml`, `Jenkinsfile`, `sonar-config.json`, `.env.example`, `.runsettings`, `scripts/**`
- Modify: `src/GSS.Api/appsettings.json`, `src/GSS.Api/Properties/launchSettings.json`
- Modify: `tests/GSS.IntegrationTests/appsettings.Testing.json`
- Delete: `.docs/architecture/db-schema-diagram.md`
- Delete or rewrite: template audit documents that describe the removed commerce implementation as current state.

**Interfaces:**

- Consumes: the domain-neutral GSS codebase from Task 2.
- Produces: consistent developer/CI/container instructions and `scripts/verify-scaffold.ps1` returning exit code 0 only for a clean GSS scaffold.

- [ ] **Step 1: Write the failing repository verification script**

Implement `scripts/verify-scaffold.ps1` with optional `-RepositoryRoot` defaulting to the parent of its script directory. It must accumulate and print all violations, then exit 1 when any exist. Assertions:

- `GSS.slnx` lists exactly the expected eight project paths.
- No file/directory name outside `.git` contains the source-template application identity.
- No current-state file outside `docs/superpowers/**` contains the source-template application identity.
- No production/test path contains a forbidden commerce resource name, except generic prose explicitly allowlisted in the script.
- No `.DS_Store`, `bin`, `obj`, or `TestResults` path is tracked.
- No committed SQL migration or seed remains.

- [ ] **Step 2: Run the verifier and confirm operational/docs residue fails**

Run: `pwsh scripts/verify-scaffold.ps1`

Expected: FAIL listing every remaining source-template or commerce reference in current-state docs/configuration; it must not flag the approved spec or this plan.

- [ ] **Step 3: Rewrite root and operational configuration**

Update all project paths, container/service identities, Sonar/Jenkins keys, database names (`gss`, `gss_test`), schema references (`gss`), commands, ports, and startup log messages consistently. Retain private-feed, PostgreSQL, Redis, OpenTelemetry, SSO/MIS/ASM, and security configuration behavior.

- [ ] **Step 4: Rewrite retained documentation as GSS current state**

Make `README.md` the GSS quick start; update `CLAUDE.md` and retained `.docs` pages to the five GSS layers and surviving capabilities. Remove commerce-schema documentation and commerce examples, update `.docs/README.md`, and ensure every indexed document exists and every retained document is indexed.

- [ ] **Step 5: Run repository verification**

Run: `pwsh scripts/verify-scaffold.ps1`

Expected: PASS with exit code 0 and a single success summary.

- [ ] **Step 6: Run formatting and focused verification**

Run: `dotnet format GSS.slnx --no-restore --verify-no-changes`

Run: `dotnet build GSS.slnx --no-restore`

Run: `dotnet test tests/GSS.UnitTests --no-build`

Run: `dotnet test tests/GSS.ArchitectureTests --no-build`

Expected: all commands pass with zero warnings/failures.

- [ ] **Step 7: Attempt integration tests when PostgreSQL is available**

Run: `dotnet test tests/GSS.IntegrationTests --no-build`

Expected when configured PostgreSQL is available: all retained authentication, health, cache, and OpenAPI integration tests pass. Otherwise capture the exact connection/configuration failure and confirm the project already compiled in Step 6.

- [ ] **Step 8: Check the final diff**

Run: `git diff --check`

Run: `git status --short`

Expected: no whitespace errors; only intended GSS scaffold changes are uncommitted.

- [ ] **Step 9: Commit operational and documentation alignment**

```bash
git add .
git commit -m "docs: align GSS enterprise scaffold operations"
```

### Task 4: Final Acceptance

**Files:**

- Verify: all tracked files
- Update only if verification exposes a scoped defect: the responsible file and its focused test.

**Interfaces:**

- Consumes: the complete GSS scaffold from Tasks 1–3.
- Produces: acceptance evidence for the requested repository.

- [ ] **Step 1: Run the complete non-integration acceptance suite**

Run: `pwsh scripts/verify-scaffold.ps1`

Run: `dotnet restore GSS.slnx`

Run: `dotnet build GSS.slnx --no-restore`

Run: `dotnet test tests/GSS.UnitTests --no-build`

Run: `dotnet test tests/GSS.ArchitectureTests --no-build`

Expected: all commands pass with zero build warnings and zero test failures.

- [ ] **Step 2: Validate project-reference edges explicitly**

Run each source-project reference inventory:

```bash
dotnet list src/GSS.Api/GSS.Api.csproj reference
dotnet list src/GSS.Business/GSS.Business.csproj reference
dotnet list src/GSS.Core/GSS.Core.csproj reference
dotnet list src/GSS.Infrastructure/GSS.Infrastructure.csproj reference
dotnet list src/GSS.Util/GSS.Util.csproj reference
```

Expected: only the approved graph from Global Constraints, with tests referencing source projects as required for their test scope.

- [ ] **Step 3: Record integration-test status**

If Task 3's integration run passed, record the count. If it was environmentally blocked, report the exact PostgreSQL/configuration prerequisite without altering production startup safeguards or deleting tests.

- [ ] **Step 4: Verify repository cleanliness and commit state**

Run: `git diff --check`

Run: `git status --short --branch`

Expected: clean working tree on the initialized repository, with no remote configured unless the user adds one.
