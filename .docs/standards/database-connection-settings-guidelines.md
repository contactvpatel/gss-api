# Database Connection Settings

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Configuration Contract](#configuration-contract)
- [Defaults](#defaults)
- [Pool Capacity](#pool-capacity)
- [Security](#security)
- [Troubleshooting and Monitoring](#troubleshooting-and-monitoring)
- [References](#references)

---

## Overview

All application database settings live under `DbConnectionSettings` with `Read` and `Write` children. Infrastructure binds them to `DbConnectionModel`, validates them at startup, and builds Npgsql connection strings. Application code MUST NOT assemble connection strings from raw configuration.

## Configuration Contract

Each side supports `Host`, `Port`, `DatabaseName`, `UserId`, `Password`, `SslMode`, pool limits, timeouts, keepalive/lifetime, and auto-prepare settings. `Host`, `DatabaseName`, and `UserId` are required. Port must be 1–65535. `MinPoolSize` must not exceed the effective `MaxPoolSize`.

Allowed SSL values are `Disable`, `Allow`, `Prefer`, `Require`, `VerifyCA`, and `VerifyFull` (case-insensitive). Blank means `Prefer`; an unknown value fails startup rather than weakening TLS accidentally.

At the section level, `MaxReplicas` is the maximum simultaneous pods including rollout surge. `BehindConnectionPooler` skips the direct PostgreSQL capacity comparison when a pooler owns server connections.

Npgsql's connection-string documentation takes precedence if this standard conflicts with provider behavior. GSS additionally requires UTC sessions, validated options, and startup failure for invalid values.

## Defaults

| Setting | GSS default |
| --- | --- |
| Port | `5432` |
| `MaxPoolSize` | `20` per distinct pool per pod |
| `MinPoolSize` | `5` |
| `ConnectionIdleLifetime` | `300` seconds |
| `CommandTimeout` | `30` seconds |
| Connect `Timeout` | `15` seconds |
| `KeepAlive` | `30` seconds |
| `ConnectionLifetime` | `0` (unlimited) |
| `MaxAutoPrepare` | `100` |
| `AutoPrepareMinUsages` | `2` |
| `MaxReplicas` | `1` |

The builder sets `Timezone=UTC` and `ApplicationName` for every application connection. Identical read/write settings share one `NpgsqlDataSource` and one pool; differing connection strings produce separate pools.

## Pool Capacity

Configure capacity from the worst simultaneous deployment, not a single local pod. Startup compares required pool capacity with the server's non-superuser connection allowance and logs a warning when the budget is exceeded. It does not silently shrink pools.

Increase `MaxPoolSize` only when measurement shows connection acquisition is the bottleneck and all pods × distinct pools still fit under PostgreSQL `max_connections`. Otherwise reduce concurrency/pool sizes, optimize query duration, or use an approved pooler such as PgBouncer. When transaction-mode PgBouncer cannot support prepared statements, set `MaxAutoPrepare` appropriately for the pooler version/configuration.

## Security

- Store real passwords in environment variables or a secret manager, never tracked JSON.
- Use `VerifyFull` with a trusted certificate in production unless infrastructure has an approved alternative.
- Give the application only the database/schema permissions it needs. The migration identity may require additional DDL rights.
- Keep parameter logging disabled; connection strings and SQL parameters may contain sensitive data.
- Do not log passwords or full connection strings on validation failures.

## Troubleshooting and Monitoring

Check application sessions with the actual application-name prefix:

```sql
SELECT application_name, state, count(*)
FROM pg_stat_activity
WHERE application_name LIKE 'gss-api%'
GROUP BY application_name, state
ORDER BY application_name, state;
```

For pool exhaustion, first inspect long/blocked queries, leaked readers/transactions, replica count, and whether read/write settings create two pools. Increase `MaxPoolSize` only if pods × pools still fit under `max_connections` (the startup budget warning); otherwise add or tune a connection pooler.

The API validates read and write connectivity before migrations. Readiness includes both connection checks; liveness deliberately does not depend on PostgreSQL.

## References

- [Npgsql connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html)
- [Npgsql data source](https://www.npgsql.org/doc/basic-usage.html#data-source)
- [Health checks](../architecture/health-checks.md)
