# Transaction Startup Lifecycle Implementation Plan

> **Archived implementation record (2026-11-17).** This plan records the transaction-startup lifecycle correction. It is not current guidance, and unchecked boxes do not indicate current implementation status. See the [current documentation](../../../.docs/README.md) and [repository guidance](../../../CLAUDE.md).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make transaction startup failure-safe, retryable, and unusable after disposal.

**Architecture:** `DapperTransactionManager` retains ownership only after both connection open and transaction begin succeed. Failed local acquisition is disposed immediately, and disposed instances reject public operations.

**Tech Stack:** .NET 10, C#, ADO.NET, xUnit, FluentAssertions, Moq.

**Spec:** `docs/superpowers/specs/2026-11-17-transaction-startup-lifecycle-design.md`

## Global Constraints

- Preserve successful begin, commit, rollback, retry-after-completion, and idempotent disposal behavior.
- Preserve the original startup exception if failure cleanup also fails.
- Do not modify unrelated existing working-tree changes.
- Leave changes uncommitted for the user.

## Review Focus

- A connection whose `OpenAsync` fails is disposed immediately and is not retained.
- A connection whose transaction begin fails after opening is disposed immediately and is not retained.
- A second begin attempt after startup failure uses a new connection and succeeds without leaking the first.
- Begin, commit, rollback, and transaction lookup after disposal throw `ObjectDisposedException` without allocating resources.
- Calling `DisposeAsync` repeatedly remains safe.

---

### Task 1: Make transaction startup failure-safe

**Files:**
- Modify: `src/GSS.Infrastructure/Helpers/DapperTransactionManager.cs`
- Modify: `tests/GSS.UnitTests/Common/Fakes/FakeDbConnection.cs`
- Modify: `tests/GSS.UnitTests/Infrastructure/Helpers/DapperTransactionManagerTests.cs`

**Interfaces:**
- Consumes: `IDapperConnectionFactory.CreateWriteConnection()` and ADO.NET async open/begin/dispose APIs.
- Produces: the existing `IDapperTransactionManager` contract with failure-safe ownership and disposal guards.

- [ ] **Step 1: Add failing regression tests and the minimum fake failure control**

Add focused tests for open failure cleanup, begin failure cleanup, successful retry with a fresh connection, and public operations after disposal. Add `BeginTransactionException` to `FakeDbConnection` so begin failure can be reproduced.

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj --no-restore --configuration Release --filter FullyQualifiedName~DapperTransactionManagerTests`

Expected: new cleanup, retry, and disposal-contract assertions fail against the current manager.

- [ ] **Step 3: Implement the minimum production fix**

Add `ThrowIfDisposed()`. In `BeginTransactionAsync`, keep connection and transaction local until both startup operations succeed; publish fields only on success. Dispose the local connection in `catch`, log cleanup failure without replacing the original exception, and rethrow. Guard `BeginTransactionAsync`, `GetCurrentTransaction`, `CommitAsync`, and `RollbackAsync`.

- [ ] **Step 4: Verify focused and nearest affected gates**

Run the focused command, then:

`dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj --no-restore --configuration Release`

Expected: all tests pass with no new warnings.
