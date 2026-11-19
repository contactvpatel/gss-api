# Testing Guide

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Test Projects](#test-projects)
- [Commands](#commands)
- [Unit Tests](#unit-tests)
- [Integration Tests and Isolation](#integration-tests-and-isolation)
- [Coverage](#coverage)
- [Writing Tests](#writing-tests)
- [References](#references)

---

## Overview

GSS separates fast isolated behavior tests, PostgreSQL-backed integration tests, and repository-wide architecture rules into three projects. Passing tests prove only the scenarios and invariants they execute; this guide does not label the system fully compliant, production-ready, or covered to an unmeasured percentage.

## Test Projects

| Project | Scope | External dependency |
| --- | --- | --- |
| `tests/GSS.UnitTests/GSS.UnitTests.csproj` | Business, Api, Util, and Infrastructure behavior with mocks/fakes or in-memory collaborators. | None beyond package restore. |
| `tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj` | Real API pipeline and persistence/migration behavior. | Local PostgreSQL test database. |
| `tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj` | Layering, build/configuration, naming, source patterns, migrations, scaffold, and docs. | `dotnet` (MSBuild evaluation), plus `git` and `pwsh` for `PreCommitHookTests`. |

Tests use xUnit, FluentAssertions, Moq, `Microsoft.AspNetCore.Mvc.Testing`, and NetArchTest. Integration tests use a local PostgreSQL database, not Testcontainers.

## Commands

```bash
dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj --no-restore
dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj --no-restore
dotnet test tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj --no-restore
```

Repository wrappers:

```bash
pwsh scripts/run-tests.ps1 -TestType Unit          # unit, then architecture
pwsh scripts/run-tests.ps1 -TestType Architecture  # architecture only
pwsh scripts/run-tests.ps1 -TestType Integration   # sequential integration run
```

Each test project has its own category trait (`Unit` or `Integration`). A solution-level `dotnet test` includes integration tests and therefore needs PostgreSQL.

## Unit Tests

Unit tests should isolate one behavior and use deterministic collaborators. Mock interfaces to inject failures or observe calls. For ordinary caching behavior, use the real in-memory `ICacheService` from `tests/GSS.UnitTests/Common/TestCache.cs`; mock it only when the cache boundary itself is under test or a requested expiration must be captured.

Use fake `TimeProvider` instances for time-driven behavior. Do not wait for a cache entry, timer, or token to expire in real time. Prefer controllable handlers/connections/tasks over sleeps.

Mapster service tests initialize the production strict configuration plus explicit test mappings. A test-only mapping must not hide a production mapping gap.

## Integration Tests and Isolation

`TestDatabaseSettings` reads `tests/GSS.IntegrationTests/appsettings.Testing.json` and optional `INTEGRATION_TEST_DB_HOST`, `_PORT`, `_NAME`, `_USER`, and `_PASSWORD` overrides. It refuses a database name that does not contain `test` because resets truncate tables.

`WebAppFactory` and `TestDatabaseFixture` share the `IntegrationDatabase` xUnit collection, so database integration tests run serially. The factory:

- creates the test database if missing (test infrastructure only);
- points both application Read and Write settings at the test database;
- starts the host so startup migrations create/update the schema;
- replaces the Business SSO service with `FakeSsoService` and wraps the connection factory in `SwitchableConnectionFactory` so a test can simulate a database outage;
- disables caching, rate limiting, and exported OpenTelemetry signals;
- adds test-only probe controllers for authentication, paging, and idempotency scenarios.

Tests that write database state MUST call `ResetDatabaseAsync()` before the write/scenario. Read-only tests need not reset unless they depend on an empty database. Do not state that every test resets: the fixture exposes reset, and writing tests use it where isolation requires it.

`TestDatabaseReset` truncates every table in the `gss` schema except `schemaversions`; it does not maintain a hard-coded feature-table list. A writing test must not depend on execution order or data left by a previous test.

## Coverage

```bash
pwsh scripts/run-coverage.ps1 -TestType Unit -ReportType Summary
pwsh scripts/run-coverage.ps1 -TestType Unit -ReportType Html
pwsh scripts/run-coverage.ps1 -TestType Integration -ReportType Html
pwsh scripts/run-coverage.ps1 -TestType All -ReportType Html
```

Unit coverage uses `tests/CodeCoverage.runsettings`; integration coverage uses `tests/runsettings.integration.xml`. Architecture tests are excluded because reflection/source rules do not meaningfully measure production-line execution.

The repository does not define a universal coverage percentage gate in these scripts. Review line and branch coverage to find untested risk, but do not substitute a percentage for scenario quality. If CI adds a threshold, document the active pipeline source and keep the claim synchronized with it.

## Writing Tests

- Name tests by behavior and observable outcome.
- Assert public results, state, calls, or logs; avoid private implementation details.
- Cover success, invalid input, missing data, dependency failure, cancellation, and concurrency where relevant.
- For repository/migration behavior, use PostgreSQL integration tests rather than a fake SQL dialect.
- For framework metadata/pipeline behavior, use `WebApplicationFactory` integration tests.
- Add an architecture test when the invariant must hold across many future files.
- Run the smallest project/filter while iterating, then the full relevant project before completion.
- Never claim an integration pass when the database was unavailable; report the environmental failure separately.

## References

- [xUnit documentation](https://xunit.net/)
- [ASP.NET Core integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests)
- [Architecture testing guide](architecture-testing-guide.md)
- [Repository scripts](../../scripts/README.md)
