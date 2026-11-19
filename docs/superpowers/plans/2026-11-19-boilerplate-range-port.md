# Boilerplate Commit Range Port Implementation Plan

> **Archived implementation record (2026-11-19).** This plan records a completed source-template commit-range audit and port. It is not current guidance, and unchecked boxes do not indicate current implementation status. See the [current documentation](../../../.docs/README.md) and [repository guidance](../../../CLAUDE.md).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Revalidate and complete the GSS-specific port of every commit in the inclusive `boilerplate-net` range `e4e2a82673b3069bccfa969b99ca11d4f450f486^..55c2caabb1bfe27e85204e6f7bbcfa384ff0dc38`.

**Architecture:** Treat each source commit as the specification, map shared source-template paths and concepts to their `GSS` equivalents, and preserve GSS-specific behavior where the source commit is domain-only. The current branch already carries most of the range; patch only omissions proven by commit-level forward/reverse checks and semantic review.

**Tech Stack:** .NET 10, C# 14, xUnit, FluentAssertions, PostgreSQL/Npgsql, DbUp, Markdown documentation.

**Spec:** `/Users/vishalpatel/Projects/boilerplate-net` commits `e4e2a82673b3069bccfa969b99ca11d4f450f486` through `55c2caabb1bfe27e85204e6f7bbcfa384ff0dc38`, inclusive.

## Global Constraints

- Include both endpoint commits and every commit between them (26 commits total).
- Preserve GSS-specific entities, schema names, paths, and test fixtures; do not import source-template-only domain features.
- Preserve existing user changes and the clean current worktree.
- Do not edit the dirty source-repository template-review document.
- Unit and architecture suites must remain green; integration verification must distinguish application failures from the known local PostgreSQL credential failure.

## Review Focus

- A source change marked applied by text can still be semantically absent after GSS adaptation; verify behavior and its test together.
- Domain-only source-template migrations and entity services must not introduce nonexistent GSS concepts.
- Migration connections must be unpooled and release both sessions and advisory locks after success and failure.
- Documentation must describe current GSS behavior, not copy source-template paths or entity examples blindly.
- The final inclusive audit must account for the first commit, last commit, and all 24 commits between them.

---

### Task 1: Complete migration lifecycle coverage

**Files:**
- Modify: `tests/GSS.IntegrationTests/Persistence/DatabaseMigrationIntegrationTests.cs`

**Interfaces:**
- Consumes: `DatabaseMigrationInitFilter.ForMigrations(string, int)` and the existing test database helpers.
- Produces: `AfterStartup_NoMigrationSessionOrAdvisoryLockRemains(bool)` plus its polling helper.

- [x] **Step 1: Port the missing source integration test**

Add the source commit's success/failure theory using the GSS schema and current helper signatures. Assert that `pg_stat_activity` and `pg_locks` both reach zero after startup.

- [x] **Step 2: Build the integration test project**

Run: `dotnet build tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj --no-restore`

Expected: build succeeds with zero warnings and zero errors.

- [x] **Step 3: Run the focused test when the database is available**

Run: `dotnet test tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AfterStartup_NoMigrationSessionOrAdvisoryLockRemains"`

Expected: both theory cases pass; if PostgreSQL rejects the configured credentials before the test body, record the same environmental baseline limitation.

### Task 2: Port remaining source refactors and documentation

**Files:**
- Modify: `tests/GSS.IntegrationTests/API/TestDatabaseIsolationTests.cs`
- Modify: `tests/GSS.IntegrationTests/Fixtures/WebAppFactory.cs`
- Modify: `.docs/architecture/validation-filter.md`
- Modify: `.docs/testing/architecture-testing-guide.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: existing GSS test fixtures and current documentation structure.
- Produces: the omitted readability edits and GSS-adapted descriptions of validation logging, API dependency enforcement, and cache lifetime testing.

- [x] **Step 1: Apply the two source test readability edits**

Use inferred local types in the two exact locations changed by source commit `8feb1753`; preserve behavior.

- [x] **Step 2: Apply the validation logging documentation update**

Document `RequestLog.ValidationFailed` at Information, `RequestLog.BindingFailed` at Debug, bounded field-name logging, and generic client binding messages.

- [x] **Step 3: Apply the API dependency documentation update**

Document `ApiLayerDependencyTests`, its negative fixtures, composition-root exceptions, and the Core/repository interfaces prohibited from ordinary API code.

- [x] **Step 4: Apply the cache lifetime guidance**

Document that caching tests may use forwarding mocks to capture requested expiration and must not wait for the real-clock cache to expire.

- [x] **Step 5: Verify formatting and documentation references**

Run: `dotnet format GSS.slnx --verify-no-changes --no-restore`

Expected: exit code 0.

### Task 3: Revalidate the complete inclusive range

**Files:**
- Verify: all files touched by the mapped source range.

**Interfaces:**
- Consumes: the source commit ledger and current GSS tree.
- Produces: an explicit 26-commit coverage audit and fresh verification evidence.

- [x] **Step 1: Re-run commit-level forward/reverse patch classification**

Expected: no cleanly applicable shared production-code hunk remains; any divergence is explained by GSS-specific adaptation or a later preserving improvement.

- [x] **Step 2: Run portable suites**

Run: `dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj --no-restore`

Expected: all unit tests pass.

Run: `dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj --no-restore`

Expected: all architecture tests pass.

- [x] **Step 3: Build the whole solution**

Run: `dotnet build GSS.slnx --no-restore`

Expected: zero warnings and zero errors.

- [x] **Step 4: Inspect the final diff**

Run: `git diff --check` and `git status --short`.

Expected: no whitespace errors; only the planned GSS files are modified.
