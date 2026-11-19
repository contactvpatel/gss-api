# UTC Time Guidelines

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Clock Rules](#clock-rules)
- [Boundary Normalization](#boundary-normalization)
- [Date-Only Values](#date-only-values)
- [Testing](#testing)
- [References](#references)

---

## Overview

GSS represents instants in UTC and obtains the current time through dependency-injected `TimeProvider`. This makes expiry, auditing, validation, and hosted-service timing deterministic in tests.

## Clock Rules

- Source code under `src/` MUST NOT call `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, `DateTime.Now`, `DateTimeOffset.Now`, `DateTime.Today`, or `ToLocalTime()`.
- Inject `TimeProvider` and use `timeProvider.GetUtcNow()`.
- Convert to `.UtcDateTime` only where an existing `DateTime` persistence/API contract requires it.
- Read the clock at the operation point, not once in a singleton/validator constructor.
- Use `PeriodicTimer` with the injected provider for time-driven hosted services where supported.

.NET `TimeProvider` guidance takes precedence if it conflicts with this document; GSS intentionally applies the stricter no-static-clock rule throughout production source.

## Boundary Normalization

- `UtcDateTimeJsonConverter` treats offsetless JSON `DateTime` input as UTC and writes UTC.
- `UtcDateTimeModelBinder` applies the same rule to query/route values.
- PostgreSQL connection strings set `Timezone=UTC`.
- Prefer `DateTimeOffset` for external instants that arrive with an offset. Normalize before comparison/storage according to the contract.
- Never reinterpret a local wall-clock value as UTC without an explicit business timezone and conversion rule.

## Date-Only Values

Use `DateOnly` and a PostgreSQL `DATE` column for calendar dates with no time or timezone, such as an illustrative birth date. Do not model those values as midnight `DateTime`, which can change the calendar day during timezone conversion.

When UTC today is the intended business rule, derive it from the injected clock (`DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)`) and compare against that. A business-local “today” requires an explicitly approved timezone rule.

## Testing

Inject a fake `TimeProvider` and advance it; do not wait for real time. `ClockUsageTests` statically rejects `DateTime` and `DateTimeOffset` `.UtcNow`, `.Now` and `.Today` in `src/` code (comment lines are skipped); it does not check `ToLocalTime()`, so review for that. Tests may construct fixed UTC values directly because those are data, not reads of the current clock.

## References

- [TimeProvider](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview)
- [DateTime and DateTimeOffset guidance](https://learn.microsoft.com/en-us/dotnet/standard/datetime/choosing-between-datetime)
- [Npgsql date/time mapping](https://www.npgsql.org/doc/types/datetime.html)
