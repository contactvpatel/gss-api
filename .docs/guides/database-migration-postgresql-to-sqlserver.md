# Porting GSS from PostgreSQL to SQL Server

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Support Status](#support-status)
- [Provider Replacement Map](#provider-replacement-map)
- [SQL and Behavioral Differences](#sql-and-behavioral-differences)
- [Migration Sequence](#migration-sequence)
- [Verification](#verification)
- [References](#references)

---

## Overview

This is a planning map for replacing PostgreSQL with SQL Server. It is not a switch that GSS currently supports: the shipped application, migrations, health checks, repository base, integration fixtures, and operational assumptions are PostgreSQL-specific. A real port is an architecture change and must replace the provider coherently.

## Support Status

GSS supports one database provider: PostgreSQL through Npgsql, Dapper, and `dbup-postgresql`. Do not add a SQL Server connection string and assume the existing code is provider-neutral. Until a completed port passes the full verification suite, SQL Server is unsupported.

The reference boilerplate is useful for identifying data-access concerns, but it is also PostgreSQL-first. There is no dormant SQL Server hosted service, repository, or query-builder implementation. The only remnant is a commented-out SQL Server connection-string builder in `src/GSS.Infrastructure/Helpers/DatabaseConnectionStrings.cs`; it is not wired in and is not a supported starting point.

## Provider Replacement Map

| PostgreSQL implementation point | Required SQL Server decision |
| --- | --- |
| `Directory.Packages.props` and `src/GSS.Infrastructure/GSS.Infrastructure.csproj` | Replace Npgsql and `dbup-postgresql` with approved SQL Server provider and DbUp packages. |
| `src/GSS.Infrastructure/Helpers/DapperConnectionFactory.cs` | Build validated SQL Server read/write connections and preserve shared-setting behavior where appropriate. |
| `src/GSS.Infrastructure/Helpers/DbConnectionModelValidator.cs` | Replace PostgreSQL connection-string and SSL validation with SQL Server equivalents. |
| `src/GSS.Infrastructure/DbUp/Core/DatabaseMigrationInitFilter.cs` | Replace PostgreSQL connection, advisory lock, journal/schema, lock-timeout, and type-reload behavior. |
| `src/GSS.Infrastructure/DbUp/Core/MigrationBatches.cs` | Redesign the `-NoTransaction.sql` convention; it currently exists for PostgreSQL operations such as `CREATE INDEX CONCURRENTLY`. |
| `src/GSS.Infrastructure/Repositories/Base/DapperRepositoryBase.cs` | Replace quoted identifiers, `RETURNING`, array parameters, `unnest`, PostgreSQL casts, SQLSTATE handling, and retry assumptions. |
| `src/GSS.Infrastructure/Repositories/IdempotencyRepository.cs` | Replace transaction-scoped advisory locking and PostgreSQL upsert/delete syntax without weakening per-user/key serialization. |
| `src/GSS.Api/HealthChecks/DatabaseConnectionHealthCheck.cs` | Verify SQL Server read/write probes and sanitization. |
| `src/GSS.Api/HostedServices/DatabaseConnectionValidationHostedService.cs` | Preserve fail-fast validation and capacity checks with SQL Server pool semantics. |
| `tests/GSS.IntegrationTests/Fixtures/TestDatabaseSettings.cs` and related fixtures | Replace test-database creation, reset, connection, and safety checks. |
| `tests/GSS.ArchitectureTests/MigrationScriptTests.cs` | Establish a new append-only manifest and SQL Server-specific prohibited-pattern checks. |
| `src/GSS.Infrastructure/DbUp/Migrations` | Translate every migration and plan production data movement. |

Also replace Compose, deployment secrets, monitoring dashboards, runbooks, and any PostgreSQL-specific configuration or documentation.

## SQL and Behavioral Differences

The port must explicitly redesign at least these behaviors:

- PostgreSQL lower-case schema/table identifiers and double-quoted column aliases;
- `numeric(19,4)` and any enum/range types;
- `RETURNING`, `ON CONFLICT`, `CREATE INDEX CONCURRENTLY`, and catalog queries;
- typed array parameters plus `unnest` for set-based range writes;
- advisory locks used by migrations and idempotency keys;
- PostgreSQL SQLSTATE classification (`23505`, `40001`, `40P01`, `3D000`);
- `Timezone=UTC` session behavior and Npgsql type mapping;
- connection-pool budgeting and telemetry instrumentation.

Choose SQL Server equivalents based on atomicity and observable behavior, not text substitution. For example, a table-valued parameter may replace an array/`unnest` batch, but tests must prove all-or-nothing update semantics and concurrent behavior.

## Migration Sequence

1. Approve the provider, driver, DbUp package, connection model, schema strategy, and rollback plan.
2. Add characterization tests for current PostgreSQL behavior that the new provider must preserve.
3. Introduce provider-specific connection/migration/repository implementations behind existing contracts where feasible.
4. Translate generic infrastructure tables and feature migrations; validate data types, indexes, constraints, and defaults.
5. Rebuild integration fixtures for an isolated SQL Server test database with the same destructive-reset safety rule.
6. Run both providers only during an explicitly designed transition. Do not ship an untested runtime toggle.
7. Rehearse production schema and data migration on a representative copy, including duration, lock impact, rollback, and verification queries.
8. Remove PostgreSQL packages and paths only after cutover evidence and rollback criteria are satisfied.

## Verification

At minimum, the port must pass unit, architecture, and SQL Server integration tests; migration-from-empty and migration-from-production-like-state tests; concurrent Unit of Work/range/idempotency tests; health and startup-failure tests; and performance measurements against an agreed workload. The current PostgreSQL suite is not evidence that a SQL Server implementation works.

## References

- [Microsoft.Data.SqlClient documentation](https://learn.microsoft.com/en-us/sql/connect/ado-net/introduction-microsoft-data-sqlclient-namespace)
- [SQL Server table-valued parameters](https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/sql/table-valued-parameters)
- [DbUp](https://dbup.readthedocs.io/)
- [Dapper repository architecture](../architecture/dapper-hybrid-approach.md)
