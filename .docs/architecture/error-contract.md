# Error Contract

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Decision: Keep the Response Envelope, Not ProblemDetails](#decision-keep-the-response-envelope-not-problemdetails)
- [Wire Shape](#wire-shape)
- [Status Codes](#status-codes)
- [How Status and Log Level Are Chosen](#how-status-and-log-level-are-chosen)
- [Adding an Error](#adding-an-error)
- [Testing](#testing)
- [References](#references)

---

## Overview

Every error the API returns, from a controller, a filter or the exception middleware, uses the same `Response<ApiError>` envelope. This page records why the project keeps that envelope instead of ProblemDetails (RFC 9457), and the rules that choose an error's HTTP status. For example payloads of every response, see [API Response Formats](../standards/api-response-formats.md).

## Decision: Keep the Response Envelope, Not ProblemDetails

**Decision (2026-11-07):** the API does **not** use `AddProblemDetails`, `Results.Problem` or `application/problem+json`. Errors stay in the `Response<T>` / `ApiError` envelope, by design. The one `IProblemDetailsService` registered is `ApiErrorProblemDetailsService`, which writes that envelope: ApiPipeline.NET writes its own errors (429) through whatever `IProblemDetailsService` is registered, and would otherwise fall back to problem+json.

**Why:**

- **One shape for every response.** Success and failure share `succeeded`, `data`, `message` and `errors`, so clients parse one structure and branch on `succeeded`.
- **Existing consumers depend on it.** Clients and the integration suite read `errors[].errorId` / `errors[].statusCode`. A second error format would break them or force them to handle two.
- **`errorId` already correlates.** It is the request's correlation id: the `X-Correlation-Id` response header and the `CorrelationId` log-scope property (ApiPipeline.NET). Support searches the logs for it and finds every line of the request, which covers what ProblemDetails' `traceId` extension would add.

**Consequences:**

- Gateways or generated clients that expect ProblemDetails need a mapping at the edge, not in this service.
- No machine-readable error code on the wire: it would mean a new `ApiError` field, which this decision freezes.
- Do not add `AddProblemDetails()` "for completeness". It switches framework-generated errors (status-code pages, `UseExceptionHandler`, `Results.Problem`) to `application/problem+json` and creates a second contract.

## Wire Shape

```json
{
  "succeeded": false,
  "data": null,
  "message": "The request conflicts with existing data. Please reload and try again.",
  "errors": [
    { "errorId": "1f0c…", "statusCode": 409, "message": "The request conflicts with existing data. Please reload and try again." }
  ]
}
```

Content type is `application/json`. `errors[].statusCode` always equals the HTTP status, and every `errors[].errorId` is the request's correlation id. `message` is never blank: for a `Failure`, `Conflict`, exception-handler response or status-code-page response it equals `errors[0].message`; otherwise it is a summary and the errors carry the detail.

## Status Codes

| Source | Status | Message returned |
| --- | --- | --- |
| `Result<T>.NotFound()` | 404 | `"{Entity} not found"` |
| `Result<T>.NotFound(message)` (e.g. a batch update naming its missing ids) | 404 | The service's message |
| `Result<T>.Failure(message)` (business rule) | 400 | The service's message |
| `Result<T>.Conflict(message)` (clashes with existing state, e.g. duplicate unique value) | 409 | The service's message |
| `default(Result<T>)` (service bug) | 500 | Generic, with `errorId` |
| FluentValidation failure (`ValidationFilter`) | 400 | One error per failed rule |
| Exceptions | See [Exception Handling](exception-handling.md#exception-mapping) | Fixed per type, or generic; never the exception's message |
| SSO, MIS or ASM failure (`ApiExceptionHandler`) | 502 / 503 / 504 | Fixed "A service this API depends on …" text: error, unavailable (circuit open) or timeout |
| Database exception without a SQLSTATE | 503 | Generic temporary-unavailability message |
| Database server error with a SQLSTATE | 500 | Generic, with `errorId` |
| Authentication / authorization | 401 / 403 (503 when SSO cannot answer, 500 for an internal token-validation error) | See [SSO Authentication](sso-authentication.md) and [ASM Authorization](asm-authorization.md) |
| Rate limit exceeded (ApiPipeline.NET) | 429 | The package's text, plus `Retry-After`; see [API Request Pipeline](api-pipeline.md#error-responses-from-the-pipeline) |
| Unmatched route, failed route constraint, unknown API version (authenticated caller; an anonymous one gets 401 first) | 404 | `"The requested resource was not found."` |
| Wrong HTTP method | 405 | `"The HTTP method is not allowed for this resource."` |
| Body not `application/json` | 415 | `"Unsupported media type: send application/json."` |

**Framework errors use the envelope too.** Routing (404, 405) and MVC (415) normally answer without a body; `UseStatusCodePages()` fills empty-bodied 4xx/5xx through `ApiErrorProblemDetailsService`, and `ApiBehaviorOptions.SuppressMapClientErrors` prevents MVC from writing ProblemDetails. A response that already has a body is never overwritten. `ErrorEnvelopeIntegrationTests` covers these cases.

## How Status and Log Level Are Chosen

- **Results, one mapping.** `ApiControllerBase.ErrorResponse` is the only place a non-success `Result<T>` becomes HTTP. Every Get / Create / Update helper goes through it, so a `Failure` is never reported as 404 and a `NotFound` from a create is never a 500.
- **`default(Result<T>)` is not an outcome.** `IsSuccess`, `IsNotFound` and `IsFailure` are all false, and `Value` throws. A service that forgets to return a result fails loudly instead of answering 404.
- **Exceptions, one classifier.** `ApiExceptionHandler.Classify` uses the exception type and its structured properties. A `DbException` without a SQLSTATE returns 503, logged at `Error` when a `TimeoutException` is in the chain and at `Critical` otherwise. A database server error with a SQLSTATE returns 500 and is logged at `Error`.

## Adding an Error

1. **Expected business outcome:** return `Result<T>.Failure(...)` (400) or `Result<T>.Conflict(...)` (409) from the service, with a message written for the caller. Add `[ProducesResponseType(typeof(Response<T>), StatusCodes.Status409Conflict)]` (or 400) to the action.
2. **Missing resource:** return `Result<T>.NotFound()`.
3. **Error raised deep in a call chain:** carry it back as a `Result<T>` outcome (`NotFound` for a missing resource). Never throw for an expected outcome: every exception the handler does not map to an upstream, database or request-read failure is a 500, `KeyNotFoundException` included. An exception's message never reaches the caller, and wording never selects a status: an `InvalidOperationException("… not found")` is a 500.
4. **New status for a `Result`:** add an outcome to `Result<T>` and one branch to `ApiControllerBase.ErrorResponse`, not a per-controller `if`.

## Testing

- [ApiControllerBaseResultMappingTests.cs](../../tests/GSS.UnitTests/API/Controllers/ApiControllerBaseResultMappingTests.cs): Failure → 400, Conflict → 409, NotFound → 404 (including from create), default result throws.
- [ErrorEnvelopeIntegrationTests.cs](../../tests/GSS.IntegrationTests/API/ErrorEnvelopeIntegrationTests.cs): unmatched route, unknown version, failed route constraint, 405 and 415 return the envelope with the correlation id.
- [ApiErrorProblemDetailsServiceTests.cs](../../tests/GSS.UnitTests/API/Middleware/ApiErrorProblemDetailsServiceTests.cs): detail, then title, then a message for the status.
- [ResultTests.cs](../../tests/GSS.UnitTests/Business/Models/ResultTests.cs): `default` is no outcome; Conflict and Failure flags.
- [ApiExceptionHandlerTests.cs](../../tests/GSS.UnitTests/API/Middleware/ApiExceptionHandlerTests.cs): "not found" wording is 500; unreachable database is `Critical`; a server-side `PostgresException` and connection-like wording are `Error`.

## References

- [RFC 9457: Problem Details for HTTP APIs](https://www.rfc-editor.org/rfc/rfc9457)
- [API response formats](../standards/api-response-formats.md)
