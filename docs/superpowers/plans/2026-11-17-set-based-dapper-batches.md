# Set-Based Dapper Batches Implementation Plan

> **Archived implementation record (2026-11-17).** This plan records the set-based Dapper batch-write implementation. It is not current guidance, and unchecked boxes do not indicate current implementation status. See the [current documentation](../../../.docs/README.md) and [repository guidance](../../../CLAUDE.md).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-row SQL concatenation with stable PostgreSQL `unnest()` batch commands while preserving repository APIs, atomicity, audit fields, cancellation, and input-order generated IDs.

**Architecture:** `DapperRepositoryBase<T>` continues to own validation, transaction lifetime, execution, affected-row checks, and ID assignment. Concrete repositories supply entity-specific insert/update batch SQL and typed-array parameters through protected command hooks; base-owned soft delete uses a shared set-based statement.

**Tech Stack:** .NET 10, C#, Dapper 2.1.89, Npgsql 10.0.3, PostgreSQL, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-11-17-set-based-dapper-batches-design.md`

## Global Constraints

- Public `IRepository<T>` signatures and business/controller behavior remain unchanged.
- Maximum batch size is exactly 1,000 at both business and repository boundaries.
- A batch is one set-based PostgreSQL statement with SQL text independent of row count.
- Ambient transactions remain caller-owned; autonomous batches retain commit/rollback behavior.
- Insert results map generated IDs by explicit one-based ordinal, never implicit `RETURNING` order.
- No SQL Server, `COPY`, staging-table, chunking, or migration work is included.
- Do not alter the user's unrelated `JwtTokenHelper.cs` edit without explicit permission.

## Review Focus

- A 1,001-row insert/update/delete must fail before obtaining a connection or opening a transaction.
- Insert results with a missing, duplicate, zero, or out-of-range ordinal must fail and roll back without assigning partial IDs.
- Duplicate or missing update/delete IDs must affect fewer rows than inputs, fail, and roll back atomically.
- Empty batches must remain no-ops and never request a connection.
- Cancellation during execution must propagate while autonomous transaction disposal rolls back.

---

### Task 1: Base batch contract, limit, and ordinal validation

**Files:**
- Modify: `src/GSS.Infrastructure/Repositories/Base/DapperRepositoryBase.cs:213-354`
- Modify: `tests/GSS.UnitTests/Common/Fakes/FakeDbConnection.cs`
- Modify: `tests/GSS.UnitTests/Common/Fakes/FakeDbCommand.cs`
- Modify: `tests/GSS.UnitTests/Infrastructure/Repositories/DapperRepositoryBaseTests.cs`

**Interfaces:**
- Consumes: existing `WriteBatchAsync(Func<IDbConnection, IDbTransaction, Task>, CancellationToken)` transaction seam.
- Produces: protected nested `DapperBatchCommand(string Sql, object Parameters)`, `BuildInsertBatchCommand(IReadOnlyList<T>)`, and `BuildUpdateBatchCommand(IReadOnlyList<T>)`; insert execution expects rows with `Ordinal` and `Id`.

- [ ] **Step 1: Extend the fake reader result without changing production code**

Add a configurable `DataTable? ReaderResult` to `FakeDbConnection`; make `FakeDbCommand.ExecuteDbDataReader` return it when supplied and retain the current single-value default otherwise.

- [ ] **Step 2: Write failing repository tests**

Add tests named:

- `RangeWrites_MoreThanRepositoryLimit_ThrowBeforeCreatingConnection` for insert, update, and delete with 1,001 entities.
- `AddRangeAsync_ReturnedIdsOutOfOrder_AssignsThemByOrdinal` using reader rows `(Ordinal: 2, Id: 22)` and `(Ordinal: 1, Id: 11)`.
- `AddRangeAsync_InvalidOrdinal_ThrowsAndDoesNotCommit` covering duplicate, zero, out-of-range, and missing ordinals.
- `UpdateRangeAsync_DifferentBatchSizes_UseTheSameCommandText` with two and three entities.
- `RangeWrite_CanceledToken_PropagatesAndDoesNotCommit` using an already-cancelled token.
- `RangeWrites_Empty_DoNothing` remains green.

The test repository's wished-for hooks return fixed SQL and array parameters; no production helper is added to make tests pass.

- [ ] **Step 3: Run the focused tests and verify RED**

Run: `dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj --no-restore --configuration Release --filter FullyQualifiedName~DapperRepositoryBaseTests`

Expected: after the unrelated compile blocker is resolved, FAIL because the protected batch hooks, repository-level limit, and ordinal mapping do not exist. If `JwtTokenHelper.cs` still fails compilation, stop and request permission rather than modifying it.

- [ ] **Step 4: Implement the minimum base behavior**

In `DapperRepositoryBase<T>`:

- Add a private `MaxBatchSize = 1000` and one validator used by all three range methods.
- Add protected abstract `DapperBatchCommand BuildInsertBatchCommand(IReadOnlyList<T> entities)` and `BuildUpdateBatchCommand(IReadOnlyList<T> entities)`.
- Execute insert once with `QueryAsync` and validate a complete, unique one-based ordinal map before assigning any IDs.
- Execute update once and preserve the exact affected-row check.
- Leave delete temporarily on the legacy helper for Task 2.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the Task 1 command. Expected: all `DapperRepositoryBaseTests` pass with no warnings.

### Task 2: Set-based PostgreSQL insert, update, and soft delete

**Files:**
- Modify: `src/GSS.Infrastructure/Repositories/Base/DapperRepositoryBase.cs:267-354`
- Modify: `tests/GSS.UnitTests/Infrastructure/Repositories/DapperRepositoryBaseTests.cs`
- Modify: `tests/GSS.IntegrationTests/Persistence/TransactionScopeIntegrationTests.cs:220-390`

**Interfaces:**
- Consumes: Task 1's `DapperBatchCommand` hooks and ordinal/ID contract.
- Produces: stable `unnest()` SQL for test/probe insert and update; base-owned set-based soft delete.

- [ ] **Step 1: Write failing SQL-shape and integration tests**

Add or strengthen tests to assert:

- Insert and update command text contains one `unnest(` and no repeated per-row statement delimiter.
- Delete command text uses `unnest(@Ids, @UpdatedAts, @UpdatedBys)` and is identical for different batch sizes.
- Existing PostgreSQL integration tests still prove IDs follow input order, all rows update/delete, missing rows roll back every change, and ambient transactions are not committed by the repository.
- Add an integration case with duplicate update/delete IDs that fails atomically.

- [ ] **Step 2: Run focused tests and verify RED**

Run unit command from Task 1 and:

`dotnet test tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj --no-restore --configuration Release --filter FullyQualifiedName~TransactionScopeIntegrationTests`

Expected: unit SQL-shape tests fail while delete still uses the legacy statement builder; integration tests requiring the configured PostgreSQL test database may pass, fail, or be unavailable and must be reported exactly.

- [ ] **Step 3: Implement concrete set-based commands**

In both test repositories:

- Insert hook projects typed arrays and returns stable SQL using `unnest(... WITH ORDINALITY)`, a materialized ID-allocation CTE, one `INSERT ... SELECT`, and final ordered `(Ordinal, Id)` rows.
- Update hook projects typed arrays and returns one `UPDATE ... FROM unnest(...)` statement.

In the base repository, replace soft-delete batching with one `UPDATE ... FROM unnest(@Ids, @UpdatedAts, @UpdatedBys)` command and retain exact affected-row validation.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run both Task 2 commands. Expected: unit tests pass; integration tests pass when PostgreSQL is available.

### Task 3: Remove the obsolete concatenation path and verify the repository

**Files:**
- Delete: `src/GSS.Infrastructure/Helpers/SqlStatementBatch.cs`
- Delete: `tests/GSS.UnitTests/Infrastructure/Helpers/SqlStatementBatchTests.cs`
- Modify: `docs/superpowers/specs/2026-11-17-set-based-dapper-batches-design.md` only if implementation-required wording changed.

**Interfaces:**
- Consumes: Task 2's complete set-based execution path.
- Produces: no remaining production reference to `SqlStatementBatch` or suffixed per-row parameters.

- [ ] **Step 1: Prove the legacy helper is unused**

Run: `rg -n "SqlStatementBatch|@[A-Za-z_][A-Za-z0-9_]*_[0-9]+" src tests`

Expected: only the helper and its dedicated tests remain.

- [ ] **Step 2: Remove the helper and its tests**

Delete the two listed files with `apply_patch`; do not perform unrelated cleanup.

- [ ] **Step 3: Run static and focused verification**

Run:

- `rg -n "SqlStatementBatch" src tests` — expected no matches.
- `git diff --check` — expected no output.
- Task 1 unit-test command — expected PASS.
- Task 2 integration-test command — expected PASS when PostgreSQL is available.

- [ ] **Step 4: Run the full solution gate**

Run: `dotnet test GSS.slnx --no-restore --configuration Release`

Expected: PASS. If the pre-existing `JwtTokenHelper.cs` compile failure remains, report the suite as blocked by that exact user-owned edit; do not claim a pass.

- [ ] **Step 5: Review the final diff**

Run: `git diff -- src/GSS.Infrastructure/Repositories/Base/DapperRepositoryBase.cs tests/GSS.UnitTests/Common/Fakes tests/GSS.UnitTests/Infrastructure/Repositories tests/GSS.IntegrationTests/Persistence docs/superpowers`

Expected: only the approved set-based batch change, its tests, and design/plan documentation are present.
