# Exception Handling Implementation Guide

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [How It Fits Together](#how-it-fits-together)
- [Exception Mapping](#exception-mapping)
- [Error Response](#error-response)
- [Error ID](#error-id)
- [Logging](#logging)
- [What the Framework Middleware Does](#what-the-framework-middleware-does)
- [Adding or Changing a Mapping](#adding-or-changing-a-mapping)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [References](#references)

---

## Overview

Unhandled exceptions go through ASP.NET Core's exception handler middleware (`UseExceptionHandler`) and the project's `IExceptionHandler`, `ApiExceptionHandler`. It logs each exception once and writes the `Response<ApiError>` envelope, not ProblemDetails; that is a deliberate decision (see [Error Contract](error-contract.md)).

- Status code, client message and log level come from the exception **type** only, in one place: `ApiExceptionHandler.Classify`. Message text never selects any of them.
- Exception messages are logged, never returned: each mapped type gets a fixed client message and everything else a generic one, so internal details never leak. Errors the caller should act on are `Result<T>` outcomes, not exceptions.
- The body is serialized with the app's HTTP JSON options (`ConfigureHttpJsonOptions`: web defaults plus `UtcDateTimeJsonConverter`), the same options as every other response.

## How It Fits Together

| Piece | Where | Role |
| --- | --- | --- |
| `UseExceptionHandler()` | `MiddlewareExtensions.ConfigureMiddleware`, a `WithMiddleware` step right after `WithCorrelationId()` and `WithSecurityHeaders()` | Framework middleware: catches, clears the response, calls the handler |
| `ApiExceptionHandler` | `Api/Middleware/ApiExceptionHandler.cs`, registered with `services.AddExceptionHandler<ApiExceptionHandler>()` | Maps the exception type to status, client message and log level (`Classify`), logs and writes the envelope; always returns `true` |
| `ApiErrorProblemDetailsService` | `Api/Middleware/` (the registered `IProblemDetailsService`) | Writes framework errors in the same envelope (ApiPipeline.NET's 429, the HTTPS-only 400, every empty-bodied 4xx/5xx through `UseStatusCodePages()`); also satisfies `UseExceptionHandler()`'s requirement for a handler when no path is configured |

Running inside the correlation phase means every exception log carries `CorrelationId`, and error responses still get the correlation and security headers. See [API Request Pipeline](api-pipeline.md#pipeline-order).

## Exception Mapping

`ApiExceptionHandler.Classify` checks in this order (first match wins):

| Exception | Status | Client message | Log level |
| --- | --- | --- | --- |
| `OperationCanceledException` while `RequestAborted` is signalled (normally answered by the framework first; see below) | 499 | "Request was canceled by the client." | Information |
| `BadHttpRequestException` in the chain with status 413 (body over `ApiPipeline:RequestLimits:MaxRequestBodySize`) | 413 | "Request body size exceeds the maximum allowed limit. …" | Warning |
| `BadHttpRequestException` in the chain with status 408 (body arrived too slowly, Kestrel's `MinRequestBodyDataRate`) | 408 | "The request body was not received in time. …" | Information |
| `BadHttpRequestException` in the chain with any other 4xx (truncated or malformed body, headers too large); a non-4xx code becomes 400 | its own 4xx | "The request could not be read. …" | Information |
| Database exception without a SQLSTATE anywhere in the chain (`DbException` with `SqlState` null, e.g. a client-side `NpgsqlException`: the server never answered; a server error such as `PostgresException` has one and is a 500 unless a row below matches) | 503 (transient: clients and gateways retry) | `The service is temporarily unavailable. Please try again later.` | **Error** when a `TimeoutException` is in the chain (command timeout, exhausted pool, connect timeout: Npgsql raises all three alike), else **Critical** (refused or broken connection). The readiness probe's database check reports an outage |
| `DbException` in the chain with SQLSTATE `23505` (unique violation: a unique index caught what a service check missed because two requests raced) | 409 | "The request conflicts with existing data. Please reload and try again." | Information |
| `DbException` in the chain with SQLSTATE `40P01` (deadlock) or `40001` (serialization failure): the transaction was rolled back in full, so a retry is safe. A batch write in its own transaction is retried once first (`DapperRepositoryBase.WriteBatchAsync`, EventId 1009); one inside a Unit of Work is not | 409 | "The request conflicted with a concurrent change. Please try again." | Warning (frequent ones mean writes lock rows in different orders) |
| `ExternalServiceException` in the chain, `Failure = Timeout` (SSO, MIS or ASM did not answer in time) | 504 | "A service this API depends on did not respond in time. …" | Error |
| `ExternalServiceException` in the chain, `Failure = Unavailable` (circuit open, outbound limiter); sets `Retry-After` when the rate limiter gave a wait | 503 | "A service this API depends on is temporarily unavailable. …" | Warning (the resilience pipeline already logged it) |
| `ExternalServiceException` in the chain, `Failure = Failed` (non-success answer, connection error) | 502 | "A service this API depends on returned an error. …" | Error |
| Anything else, including every `InvalidOperationException`, `ArgumentException` (and its subclasses), `UnauthorizedAccessException`, `KeyNotFoundException` (a dictionary-indexer bug; nothing throws it on purpose) and an `OperationCanceledException` the client did not cause | 500 | Generic, with the ErrorId | Error |

Infrastructure (`HttpServiceBase` → `HttpErrorHandler.ToExternalServiceException`) is the only layer that reads Polly's exceptions, through HttpResilience.NET's `HttpResilienceFailure.Classify`; the Api maps the Core `ExternalServiceException` only. The rule searches the inner-exception chain, so a caller may wrap it. A bare `HttpRequestException` is a 500. It comes after the database rules, so a database timeout is the 503 above, never an upstream 504.

`UnauthorizedAccessException` is an OS/file permission error, not caller authorization, so it is a 500; 401/403 come only from `SsoAuthenticationHandler` and `AsmPermissionHandler`. An `OperationCanceledException` the client did not cause (an internal timeout, shutdown) is also a 500, never "canceled by the client".

`ArgumentException` is deliberately not a 400: it comes from a guard clause, so it means a bug, and mapping it to 400 would report bugs as "invalid data". Bad client input is rejected before any guard runs: FluentValidation validators (`ValidationFilter`), route constraints (`{id:int:min(1)}`, `{name:regex(\\S)}` for string route values, which answer 404) and `Result<T>.Failure` for business rules.

The generic message is `Error occurred in the API. Please use the ErrorId [<id>] and contact support team if the problem persists.` The response never says which backend failed: a database error reads like any other 500 (`PostgresException`) or 503 (no server answer); the full exception is in the log line, found by the `errorId`.

Statuses from `Result<T>` (no exception) are mapped by `ApiControllerBase`: NotFound 404, Failure 400, Conflict 409. See [Error Contract](error-contract.md#status-codes).

## Error Response

```json
{
  "succeeded": false,
  "data": null,
  "message": "Error occurred in the API. Please use the ErrorId [4bf92f3577b34da6a3ce929d0e0e4736] and contact support team if the problem persists.",
  "errors": [
    {
      "errorId": "4bf92f3577b34da6a3ce929d0e0e4736",
      "statusCode": 500,
      "message": "Error occurred in the API. Please use the ErrorId [4bf92f3577b34da6a3ce929d0e0e4736] and contact support team if the problem persists."
    }
  ]
}
```

The top-level `message` is always the error's message (never blank), as in every other envelope. Null properties are written (`"data": null`), as in controller responses; there is no separate serializer configuration for errors.

## Error ID

The `errorId` is always the request's correlation id, from `ApiErrorId.For(HttpContext)`: the caller's `X-Correlation-Id` when well-formed, else the one ApiPipeline.NET generated. It is the `X-Correlation-Id` response header and the `CorrelationId` property on every log line of the request. It is not forwarded to SSO, MIS or ASM (see [API Request Pipeline](api-pipeline.md#not-enabled-yet)). The trace id (for the distributed trace) is in the `X-Trace-Id` response header.

Nothing overrides it (an `exception.Data["ErrorId"]` is ignored). Every error envelope (validation 400, authentication 401, controller 404/409, 429, unhandled exceptions) uses the same `ApiErrorId`, so support can go from an error response straight to the request's logs.

## Logging

`ApiExceptionHandler` logs each handled exception once, at the strategy's level, with the exception attached:

```
Area: {Area}, RequestPath: {RequestPath}, RequestMethod: {RequestMethod}, ErrorId: {ErrorId}, Message: {Message}, InnerException: {InnerException}
```

`Area` is `ApiExceptionHandler`; `InnerException` is the innermost exception's message. The framework middleware does not log an exception that an `IExceptionHandler` handled (ASP.NET Core 10), so there is no second "unhandled exception" entry.

Code below the API layer should not catch, log and rethrow: the exception reaches this handler and is logged here (the repository base class follows this; see [Dapper Hybrid Approach](dapper-hybrid-approach.md#transactions-and-retries)).

## What the Framework Middleware Does

These behaviours come from `UseExceptionHandler`, not from project code:

- **Client aborted the request** (`OperationCanceledException` or `IOException` while `RequestAborted` is signalled): status 499, no body, the handler is not called, logged at Debug.
- **Response already started** (for example a fault mid-body): the error cannot be written, so the middleware logs it and rethrows; the server aborts the connection.
- **Before calling the handler** it clears the response (status, headers, body), resets the endpoint and route values, and sets `Cache-Control: no-cache,no-store`, `Pragma: no-cache` and `Expires: -1`. ApiPipeline.NET's correlation and security headers are still written.

## Adding or Changing a Mapping

- **An error the caller should see** (business rule, conflict): return `Result<T>.Failure(...)` (400) or `Result<T>.Conflict(...)` (409) from the service; see [Error Contract](error-contract.md#adding-an-error). Exceptions are for unexpected failures.
- **A missing resource**: return `Result<T>.NotFound()` from the service; the controller maps it to 404 (`ApiControllerBase.ErrorResponse`). Never throw for it: `KeyNotFoundException` is a 500, because a dictionary-indexer bug throws the same type.
- **A new exception type with its own status or log level**: add an arm to the `switch` in `ApiExceptionHandler.Classify`, matched on type, never on message text, and a test in `ApiExceptionHandlerTests`.
- Don't add `AddProblemDetails`, a second `IExceptionHandler` for the same exceptions, or a custom exception-catching middleware.

## Testing

- `ApiExceptionHandlerTests` (unit): each mapping, no message echo, an ErrorId carried by the exception is ignored, a database error does not name the database, use of the app's JSON options.
- `MiddlewarePipelineOrderTests` (unit, real pipeline): envelope with correlation and security headers, ErrorId equal to `X-Correlation-Id`, `KeyNotFoundException` is a 500, a single error log entry.
- `ErrorIdIntegrationTests` (integration): ErrorId equals the correlation id for a controller 401, a validation 400 and an authentication challenge 401.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| problem+json instead of the envelope | `AddProblemDetails` was added, or `ApiErrorProblemDetailsService` is no longer the `IProblemDetailsService` | Remove `AddProblemDetails`; keep the registration in `ServiceExtensions` |
| Startup fails while configuring the exception handler middleware | No `IProblemDetailsService` registered and no handler path | Keep `ApiErrorProblemDetailsService` registered |
| Wrong status for an exception | Its type isn't in `Classify`, or it derives from the wrong base type | Return a `Result<T>` outcome instead, or add an arm to `Classify` |
| Error logged twice | Code below the API catches, logs and rethrows | Let it propagate |
| No body, status 499 | The client disconnected | Expected |

## References

- [Error Contract](error-contract.md)
- [API Request Pipeline](api-pipeline.md)
- [API Response Formats](../standards/api-response-formats.md)
- [Microsoft: Handle errors in ASP.NET Core APIs](https://learn.microsoft.com/aspnet/core/web-api/handle-errors)
- [Microsoft: IExceptionHandler](https://learn.microsoft.com/aspnet/core/fundamentals/error-handling#iexceptionhandler)
