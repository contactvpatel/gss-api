# Dapper Repository Architecture

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Contracts and Responsibilities](#contracts-and-responsibilities)
- [Read Path](#read-path)
- [Write Path](#write-path)
- [Transactions and Retries](#transactions-and-retries)
- [Adding a Feature Repository](#adding-a-feature-repository)
- [Verification](#verification)
- [References](#references)

---

## Overview

GSS uses hand-written Dapper SQL behind shared repository contracts. Concrete feature repositories supply table-specific SQL and columns; `DapperRepositoryBase<T>` owns the common active-row reads, audit stamping, single-row writes, set-based range writes, connection selection, and transaction participation. GSS does not contain a query-builder helper or a shipped feature repository.

## Contracts and Responsibilities

| Location | Responsibility |
| --- | --- |
| `src/GSS.Core/Interfaces/Base/IRepository.cs` | Persistence contract used by Business services. |
| `src/GSS.Infrastructure/Repositories/Base/DapperRepositoryBase.cs` | Shared PostgreSQL implementation for `EntityBase` types. |
| `src/GSS.Infrastructure/Interfaces/IDapperConnectionFactory.cs` | Creates read and write connections. |
| `src/GSS.Infrastructure/Interfaces/IDapperTransactionManager.cs` | Owns the current Unit of Work transaction and enforces one command at a time. |
| `src/GSS.Core/Interfaces/Base/IUnitOfWork.cs` | Transaction boundary exposed to Business services. |

Each concrete repository defines stable metadata and SQL:

- `TableName`, `Schema`, and `SelectSql`;
- `BatchColumns` for set-based inserts and updates;
- `BuildInsertSql()` and `BuildUpdateSql()` for single-row writes;
- any feature-specific read SQL.

The base caches generated SQL once per concrete repository type. Those members must not depend on instance or mutable state.

## Read Path

The base implements these common reads from `SelectSql`:

- `GetByIdAsync` and `GetAllAsync` for active rows;
- `FindByIdsAsync` and existence checks;
- `GetPagedAsync` for 1-based page-number pagination;
- `GetPagedAfterAsync` for ID-cursor pagination.

Both paging methods return `PageSlice<T>`, request one extra row to determine whether another page exists, and calculate a total only when `includeTotal` is true. Use cursor paging for an indexed, monotonic ID traversal; use page-number paging where callers require an offset-based grid.

Feature-specific reads compose a constant select list with a parameterized predicate and call the protected query helpers. List reads return `IReadOnlyList<T>`. Never interpolate request values into SQL.

Outside a transaction, a read uses and disposes a read connection. Inside an active Unit of Work, it uses the transaction's write connection and transaction, so it can observe earlier uncommitted writes in that transaction.

## Write Path

Single-row writes run immediately. `AddAsync`, `UpdateAsync`, and `DeleteAsync` do not require a later `SaveChangesAsync` call.

The base receives `TimeProvider` and `IUserContext`. It uses `TimeProvider.GetUtcNow()` for audit timestamps and requires a positive numeric authenticated user ID for `CreatedBy` and `UpdatedBy`. A background writer must register an explicit `IUserContext`; it must not silently record user `0`.

Range methods are set based:

- `AddRangeAsync` sends one typed array per `BatchColumns` entry and expands rows with PostgreSQL `unnest`.
- `UpdateRangeAsync` locks matching active rows in ID order and updates the whole validated batch.
- `DeleteRangeAsync` soft-deletes the supplied rows.

The SQL text is independent of batch size. The shared `BatchLimits.MaxBatchSize` cap is enforced at the API filter, Business service, and repository boundaries. An ordinary range call owns its set-based statement and transaction when no Unit of Work is already active; do not wrap a single range call in an extra Unit of Work.

## Transactions and Retries

When a Unit of Work is active, every repository command participates in it. A transaction has one connection, so commands must be awaited sequentially. `IDapperTransactionManager.EnterCommand()` fails immediately if another command is already running; do not use `Task.WhenAll` for database calls in one Unit of Work.

Use a Unit of Work for a composed operation whose multiple writes must commit or roll back together. Call external SSO, MIS, or ASM services before opening it; `HttpServiceBase` rejects outbound HTTP while the scoped database transaction is active.

For a range write that owns its transaction, the base retries the whole operation once after PostgreSQL serialization failure (`40001`) or deadlock (`40P01`). It does not retry inside a caller-owned Unit of Work because retrying only one statement would not replay the transaction safely.

## Adding a Feature Repository

The following is an illustrative checklist; GSS ships no sample feature entity.

1. Add an entity derived from `EntityBase` and a feature repository interface in Core.
2. Add a sealed Infrastructure repository derived from `DapperRepositoryBase<TEntity>`.
3. Define one quoted select list whose aliases match the entity properties.
4. Define constant schema/table metadata, `BatchColumns`, and parameterized single-write SQL.
5. Register the interface and implementation in `src/GSS.Infrastructure/Extensions/DataAccessExtensions.cs`.
6. Add a DbUp migration and update the append-only migration manifest test.
7. Add unit or integration coverage for feature-specific SQL and rules.

For enum or range columns, give `Column(...)` the PostgreSQL cast type and keep it aligned with the single-row SQL. Repository SQL identifiers are validated; do not derive schema, table, column, or cast names from user input.

## Verification

Relevant checks include:

- `DapperRepositoryBaseTests` for connection, transaction, range, audit, and cancellation behavior;
- `RepositorySqlTests` for stateless SQL metadata;
- `TransactionApiTests` (async-only transaction API) and `ParallelCallTests` (no parallel calls in `src/` outside reviewed files);
- `ClockUsageTests` for prohibited direct wall-clock reads;
- persistence integration tests (`TransactionScopeIntegrationTests`, `DapperConnectionFactoryIntegrationTests`) for real PostgreSQL behavior.

No performance multiplier is claimed here. Measure a concrete workload with representative data and the same database settings before making a performance assertion.

## References

- [Dapper repository](https://github.com/DapperLib/Dapper)
- [PostgreSQL array functions](https://www.postgresql.org/docs/current/functions-array.html)
- [Testing guide](../testing/testing-comprehensive-guide.md)
