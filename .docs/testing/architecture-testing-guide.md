# Architecture Testing Guide

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Running the Suite](#running-the-suite)
- [Current Test Inventory](#current-test-inventory)
- [Adding a Rule](#adding-a-rule)
- [Interpreting Failures](#interpreting-failures)
- [References](#references)

---

## Overview

`GSS.ArchitectureTests` enforces repository-wide structure that ordinary unit tests cannot protect reliably: project dependencies, layer boundaries, naming, configuration, migrations, source patterns, scaffold neutrality, and documentation integrity. The project uses xUnit, FluentAssertions, NetArchTest, reflection, project/XML inspection, and targeted source scans.

## Running the Suite

```bash
dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj --no-restore
pwsh scripts/run-tests.ps1 -TestType Architecture
```

The default Unit wrapper also runs architecture tests after `GSS.UnitTests`:

```bash
pwsh scripts/run-tests.ps1 -TestType Unit
```

Use a fully qualified filter while developing one rule, then run the complete project before completion. No fixed duration is promised; MSBuild-evaluating rules are slower than metadata-only rules and machine/feed conditions vary.

## Current Test Inventory

Every current C# file in the architecture project is accounted for below:

| File | Responsibility |
| --- | --- |
| `ApiLayerDependencyTests.cs` | Prevents Api controllers/filters/handlers/middleware from depending on Infrastructure or persistence/Core service contracts outside approved composition types. |
| `BuildSettingsTests.cs` | Evaluates each project with MSBuild: language/analysis level pinned to the target framework, CodeStyle.NET global analyzer config, no redeclared central settings, Docker copies every root build file, `.dockerignore` committed. |
| `ClockUsageTests.cs` | Rejects direct static wall-clock reads under `src/`. |
| `ConfigurationFileTests.cs` | No `appsettings.{Environment}.json` under `src/`; every `[EnableRateLimiting]` policy is configured; Npgsql tracing stays enabled; cache `ThrowOn*` switches and `UseDevelopmentDefaults` stay off. |
| `ConstructorInjectionTests.cs` | Rejects optional/null DI constructor dependencies that could hide missing registrations. |
| `DependencyGuardTests.cs` | Keeps Core and Business (and Util for ASP.NET Core) free of Dapper, Npgsql, DbUp, StackExchange.Redis and ASP.NET Core. |
| `DependencyTests.cs` | Enforces Clean Architecture project/type dependencies and approved package ownership. |
| `DocumentationTests.cs` | Enforces topic inventory, index coverage, structure, links/paths, fences, standards precedence, and source-template residue. |
| `DtoShapeTests.cs` | DTOs are `sealed record`s with init-only properties. |
| `EntityServiceInterfaceTests.cs` | `EntityServiceBase` implements the shared `IEntityService<,,>` CRUD contract. |
| `Fixtures/ApiLayerFixtures.cs` | Deliberately violating types used to prove Api dependency rules. |
| `MigrationScriptTests.cs` | Enforces embedded migration inventory, hashes, ordering, and filename convention. |
| `NamingConventionTests.cs` | Enforces interface `I` prefixes, `Service`/`Repository`/`Validator`/`Controller` suffixes, and the Interfaces/DTOs namespaces. |
| `NetArchTestAssertionTests.cs` | Verifies custom NetArchTest assertion output/failure behavior. |
| `ParallelCallTests.cs` | Restricts parallel constructs in production code to reviewed locations. |
| `PreCommitHookTests.cs` | Verifies the installed-hook script's formatting-only contract. |
| `RepositoryPaths.cs` | Resolves the repository root for architecture tests. |
| `RepositorySqlTests.cs` | Repository SQL is built from constants only, and the rule flags instance or mutable state. |
| `RouteConstraintTests.cs` | Every `int` route value carries `{id:int:min(1)}`. |
| `ScaffoldCompositionTests.cs` | Enforces domain-neutral scaffold registrations and absence of sample feature composition. |
| `SchemaUsageTests.cs` | Repository SQL never names the shared `public` schema. |
| `SdkVersionAlignmentTests.cs` | Keeps `global.json`, Docker SDK image, and Jenkins SDK version aligned. |
| `StructureTests.cs` | Public Core/Business interfaces, sealed repositories that depend on Core, entities derive from `EntityBase`, validators and controllers inherit their bases, `[ResponseCache]` only as `NoStore`, no DataAnnotations on request DTOs. |
| `TestCategories.cs` | Local copy of the trait constants, so no other test project is referenced. |
| `TransactionApiTests.cs` | `IUnitOfWork` and `IDapperTransactionManager` expose only async operations and are `IAsyncDisposable` without `IDisposable`. |

## Adding a Rule

1. State the invariant and the failure it prevents.
2. Write a fixture or test that proves the rule fails for a known violation when practical.
3. Prefer semantic reflection/project inspection over brittle text matching.
4. If source scanning is necessary, exclude generated output and make the diagnostic include file/member context.
5. Add the Unit trait and keep the rule independent of PostgreSQL/network services.
6. Update this inventory and the owning standard/architecture document.

## Interpreting Failures

Treat an architecture failure as a design signal, not a nuisance to suppress. Determine whether the implementation violates an approved invariant or the invariant is obsolete. Change the test only with evidence that the architecture decision changed, and update documentation in the same change.

For documentation checks, fix the document or inventory. Do not weaken a path/link/residue assertion merely to preserve stale prose.

## References

- [NetArchTest](https://github.com/BenMorris/NetArchTest)
- [Project structure](../architecture/project-structure.md)
- [Documentation standards](../documentation-standards.md)
