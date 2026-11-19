# Set-Based Dapper Batch Writes

> **Archived design record (2026-11-17).** This document records the design selected for set-based Dapper batch writes. It is not current guidance, and any unchecked work described here does not indicate current implementation status. See the [current documentation](../../../.docs/README.md) and [repository guidance](../../../CLAUDE.md).

## Intent

Replace the current semicolon-joined, one-statement-per-row Dapper batches with stable, PostgreSQL set-based commands. Preserve the public repository API, audit stamping, transaction ownership, all-or-nothing behavior, affected-row validation, cancellation, and input-order ID assignment.

## Goals

- Execute each insert, update, or soft-delete batch as one set-based PostgreSQL statement using typed arrays and `unnest()`.
- Keep SQL text independent of batch size so Dapper and Npgsql can reuse cached/prepared command shapes.
- Enforce the 1,000-row limit inside `DapperRepositoryBase`, including callers that bypass the business helper.
- Preserve `AddRangeAsync`'s mapping of generated IDs back to the corresponding input entity.
- Keep reads and single-row writes unchanged.

## Non-goals

- Binary `COPY`, temporary tables, chunking, provider-neutral batch SQL, or SQL Server support.
- Changing `IRepository<T>`, service/controller behavior, transaction semantics, or the public batch-size limit.
- Adding concrete production repositories or migrations; this scaffold currently has only test repositories.

## Constraints

- The generic base does not know entity-specific columns or PostgreSQL array types.
- Concrete repositories must therefore own insert/update batch SQL and parameter projection.
- A set-based insert must return a one-based `Ordinal` plus generated `Id`; the base validates that every input ordinal occurs exactly once before assigning IDs.
- Set-based update/delete must report exactly one affected row per input. Missing, inactive, or duplicate IDs cause the existing exception and transaction rollback.
- Existing ambient `IDapperTransactionManager` transactions remain caller-owned; otherwise the base creates and commits its own transaction.

## Selected approach

Add protected batch-command hooks to `DapperRepositoryBase<T>`:

- Insert hook: returns stable SQL plus typed-array parameters. Its query result is `(Ordinal, Id)` for each input row.
- Update hook: returns stable SQL plus typed-array parameters and is executed once.
- Soft delete remains base-owned because all entities share `Id`, `UpdatedAt`, and `UpdatedBy`; it uses `unnest(@Ids, @UpdatedAts, @UpdatedBys)`.

The probe/test repositories will implement insert and update with `unnest()` CTEs. Insert SQL will allocate IDs inside a materialized input CTE and return explicit ordinal-to-ID mappings, avoiding reliance on undocumented `RETURNING` row order. The obsolete `SqlStatementBatch` helper and its tests will be removed after no production references remain.

## Alternatives considered

- `NpgsqlBatch`: smaller change, but PostgreSQL still executes one command per row and therefore only partially addresses the issue.
- Binary `COPY` plus staging: highest throughput for very large imports, but excessive for the 1,000-row contract and substantially complicates generated IDs, updates, and deletes.
- Parsing existing single-row SQL in the base: rejected because SQL rewriting is fragile and cannot reliably infer column types or expressions.

## Verification

- Test-first unit coverage for repository-level size rejection and ordinal/ID validation.
- Integration coverage proving insert/update/delete behavior, atomic rollback, ID order, and ambient transaction behavior against PostgreSQL.
- Assert generated batch SQL has one set-based statement and stable text for different batch sizes.
- Run focused repository tests, then the full solution suite.

PostgreSQL-backed verification requires valid local integration-test credentials. If those credentials are unavailable, unit and architecture verification will still run, and the integration-test limitation will be reported separately rather than attributed to the batch implementation.
