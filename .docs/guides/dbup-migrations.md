# DbUp Migrations

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Startup Behavior](#startup-behavior)
- [Configuration](#configuration)
- [Adding a Migration](#adding-a-migration)
- [Transaction Modes](#transaction-modes)
- [Seed Scripts](#seed-scripts)
- [Deployment Rules](#deployment-rules)
- [Troubleshooting](#troubleshooting)
- [References](#references)

---

## Overview

GSS embeds PostgreSQL migration and seed scripts in `GSS.Infrastructure` and runs them with DbUp during startup when `DatabaseMigration:Enabled` is true. The database must already exist; the application never creates a deployment database.

The scaffold currently ships one generic migration for idempotency-key storage. Feature migrations are appended after it.

## Startup Behavior

`DatabaseMigrationInitFilter` runs before the middleware pipeline is built:

1. Opens an unpooled migration connection using the validated write settings.
2. Acquires a PostgreSQL advisory lock derived from the migration journal identity unless an override is configured.
3. Prepares the `gss.schemaversions` journal and copies a legacy `public.schemaversions` journal once when present.
4. Executes pending migrations in name order.
5. Reloads Npgsql type catalogs for the application's read and write data sources.
6. Executes pending seed scripts for `AppSettings:Environment`.
7. Releases the advisory lock.

Migration or seed failure throws and prevents the host from starting. A pod must not serve against an unverified schema.

## Configuration

The `DatabaseMigration` section supports:

| Key | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `false` in the options type; `true` in tracked Development settings | Run migrations and environment seeds at startup. |
| `PostgresqlAdvisoryLockKey` | derived | Optional explicit advisory-lock key. Normally leave unset. |
| `AdvisoryLockTimeoutSeconds` | `60` | Maximum wait for another migrator. |
| `LockTimeoutSeconds` | `5` | PostgreSQL `lock_timeout` applied to migration statements; `0` waits indefinitely. |
| `LockTimeoutAttempts` | `3` | Attempts for a script that fails specifically because of lock timeout. |

Use environment variables for deployment overrides, for example `DatabaseMigration__Enabled=true`.

## Adding a Migration

1. Create a file under `src/GSS.Infrastructure/DbUp/Migrations` named:

   ```text
   yyyyMMdd-HHmmss-TICKET-NNN-Description.sql
   ```

2. Make the script safe for rolling deployment. Prefer expand/contract changes so old and new pods can run concurrently.
3. Make reruns harmless where PostgreSQL permits it (`IF EXISTS`, `IF NOT EXISTS`, guarded catalog checks).
4. Run the architecture tests. `EmbeddedScripts_MatchAppendOnlyList` fails because the embedded script is missing from `KnownScripts`.
5. Add the script to `KnownScripts` in `tests/GSS.ArchitectureTests/MigrationScriptTests.cs` as `("Migrations.<file>.sql", "<sha256>")` (seeds: `Seeds.{Environment}.<file>.sql`). Use a placeholder hash first; `EmbeddedScripts_MatchRecordedHashes` then prints the actual SHA-256 (line endings and BOM ignored) in its failure message.
6. Run the architecture and PostgreSQL integration tests.

Never rename, delete, or edit a recorded script. DbUp journals by name and will not re-run changed content in an environment where the name is already recorded. Correct a mistake with a later migration.

## Transaction Modes

Ordinary scripts run one per transaction. This allows a committed schema/type change from one script to be visible to the next.

A script whose name ends in `-NoTransaction.sql` runs outside a transaction. Use that suffix only for a PostgreSQL operation that cannot run transactionally, such as `CREATE INDEX CONCURRENTLY`. `MigrationBatches` preserves global script order while grouping consecutive scripts by transaction mode.

The filter retries a lock-timeout failure up to `LockTimeoutAttempts`. It does not generally retry arbitrary SQL or data errors.

## Seed Scripts

Environment seeds are embedded from `src/GSS.Infrastructure/DbUp/Seeds/{Environment}/`, where `{Environment}` is the `AppSettings:Environment` value (`Development`, `Testing`, `QA`, `UAT` or `Production`).

The scaffold ships no seed scripts and no `Seeds` folder; create the folder with the first seed. Use seeds only for environment-specific reference/setup data, not for schema changes. Seeds are journaled and follow the same append-only rule.

## Deployment Rules

- Apply migrations with an identity allowed to change the existing `gss` schema but not required to create databases.
- Keep migrations backward compatible until all old pods are gone.
- Avoid long table rewrites and unbounded data updates in startup migrations; plan large changes as explicit operational work.
- Review locks and statement duration on representative data.
- Back up and rehearse rollback before destructive changes.
- Do not disable migrations merely to hide a failing script; diagnose and correct it with a new script.

## Troubleshooting

- **Database does not exist:** create it outside the API and verify `DbConnectionSettings:Write`.
- **Advisory-lock timeout:** another instance may still be migrating. Inspect its logs/session before increasing the timeout.
- **Statement lock timeout:** find the session holding the conflicting lock. The filter retries only the configured number of times.
- **Script hash failure** (`EmbeddedScripts_MatchRecordedHashes`): restore the recorded script content and create a new migration for the correction.
- **Enum/type reads fail after migration:** confirm startup logged `Reloaded database types after migration.` and that the application uses the registered connection factory.
- **Seed did not run:** confirm `AppSettings:Environment`, embedded resource naming, and that the seed is new to that environment's journal.

## References

- [DbUp documentation](https://dbup.readthedocs.io/)
- [PostgreSQL explicit locking](https://www.postgresql.org/docs/current/explicit-locking.html)
- [Database connection settings](../standards/database-connection-settings-guidelines.md)
