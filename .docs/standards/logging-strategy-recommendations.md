# Logging Standard

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Ownership](#ownership)
- [Levels](#levels)
- [Structured Events](#structured-events)
- [Event ID Ranges](#event-id-ranges)
- [Sensitive Data](#sensitive-data)
- [Testing](#testing)
- [References](#references)

---

## Overview

GSS uses structured `Microsoft.Extensions.Logging` events, with Serilog and OpenTelemetry handling configured outputs. Log an event once at the boundary that handles it; do not log and rethrow at every layer.

Microsoft's .NET logging guidance takes precedence where it conflicts with this standard. GSS additionally reserves event ranges and defines boundary ownership below.

## Ownership

- `ApiExceptionHandler` logs unhandled application, database, and downstream dependency failures once.
- `SsoAuthenticationHandler` logs the authentication verdict/outage at the authentication boundary.
- `ValidationFilter` logs client validation failures at Information and unsafe binding exception detail at Debug.
- `ApiControllerBase.HandleNotFound` logs controller 404 outcomes once at Debug.
- `EntityServiceBase` logs successful single/batch writes once at Information.
- Repository connection, read and write events are Debug, except the safe batch retry (1009) and a transaction rolled back at disposal (1207), which are Warning, and failed transaction cleanup (1201, 1208), which is Error.
- `HttpServiceBase` and `HttpErrorHandler` MUST NOT log a failure that is propagated to a boundary logger.

Expected Business outcomes represented by `Result<T>` are not exceptions and SHOULD NOT produce Error logs.

## Levels

- **Trace/Debug:** diagnostic flow and expected not-found details useful only during investigation.
- **Information:** normal lifecycle events, successful writes, client validation failures, local malformed/expired token rejection.
- **Warning:** an unusual recoverable condition requiring attention, such as SSO explicitly rejecting a token or a safe database retry.
- **Error:** a failed request/background operation whose cause needs action.
- **Critical:** service/database connectivity failure classified as a severe infrastructure condition.

Do not declare a universal slow-query threshold in documentation. Establish one from service objectives and telemetry, then implement/test it deliberately.

## Structured Events

Use source-generated `[LoggerMessage]` methods for stable repeated events. Message templates use named properties, not string interpolation:

```csharp
[LoggerMessage(EventId = 3200, Level = LogLevel.Information,
    Message = "Imported {Count} {Entity}")]
private static partial void Imported(ILogger logger, int count, string entity);
```

The example is illustrative. Before assigning an ID, inspect existing helpers and reserve a non-conflicting range. Keep property names stable because dashboards and alerts may depend on them.

## Event ID Ranges

Current repository ranges are:

| Layer/owner | IDs |
| --- | --- |
| Infrastructure data, transactions, HTTP, DbUp | `1000–1499` |
| Api controller/request/authentication | `2000+` (`2000`, `2010–2011`, `2100–2101`) |
| Api idempotency cleanup | `2200–2201` |
| Business entity writes | `3000–3013` |
| Business idempotency | `3100–3102` |

New events MUST avoid existing IDs and SHOULD remain in their layer's range.

## Sensitive Data

Never log bearer/refresh tokens, passwords, application secrets, full connection strings, cookies, or raw request/response bodies by default. Log stable identifiers only when operationally necessary and authorized.

`SensitiveDataSanitizer` masks supported patterns in upstream failure detail before it reaches the boundary. It is defense in depth, not permission to log arbitrary content. Exception messages returned to clients remain sanitized fixed text.

## Testing

For high-value events, tests SHOULD assert level, EventId, and structured values, and SHOULD prove duplicate logs are absent across a propagated failure. Do not assert console rendering; providers may format the same structured event differently.

## References

- [.NET logging guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging)
- [High-performance logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging)
- [Exception handling](../architecture/exception-handling.md)
