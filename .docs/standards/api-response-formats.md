# API Response Formats

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Envelope](#envelope)
- [Success Responses](#success-responses)
- [Error Responses](#error-responses)
- [Status Rules](#status-rules)
- [Headers and Health Probes](#headers-and-health-probes)
- [Testing](#testing)
- [References](#references)

---

## Overview

Every JSON API response MUST use the GSS `Response<T>` envelope. Expected Business outcomes use `Result<T>` and are mapped centrally by `ApiControllerBase`; unexpected failures are mapped by `ApiExceptionHandler`. Do not return ProblemDetails from application endpoints.

## Envelope

Success:

```json
{
  "succeeded": true,
  "data": {},
  "message": "",
  "errors": null
}
```

Failure:

```json
{
  "succeeded": false,
  "data": null,
  "message": "Validation failed",
  "errors": [
    { "errorId": "correlation-id", "statusCode": 400, "message": "Name: Name is required." }
  ]
}
```

Construct envelopes only through `Response<T>.Success` and `Response<T>.Failure` or the controller-base helpers. `ApiError.ErrorId` is the request correlation ID, which matches `X-Correlation-Id` and the structured `CorrelationId` log property.

## Success Responses

- `200 OK`: reads and batch update/delete operations, which return the affected items or ids.
- `204 No Content`: single PUT, PATCH and DELETE (a DELETE of an already-deleted row also succeeds).
- `201 Created`: creates. A single create SHOULD include a `Location` header through `CreatedAtActionResponse`; a batch create uses `CreatedResponse` without a location.
- Empty list reads return `200` with `[]`, not `404`.
- List endpoints may expose all items, `PagedResult<T>`, or `CursorPagedResult<T>` as documented by the controller. Counts are nullable unless requested.

Do not return a bare DTO or anonymous object from a JSON controller action.

## Error Responses

- Validation and binding errors use `Response<object?>.Failure("Validation failed", errors)` with `400`.
- A missing routed resource is a `Result.NotFound` mapped to `404`.
- Business validation uses `Result.Failure` (`400`) or `Result.Conflict` (`409`).
- Authentication and authorization use `401` and `403`; an SSO outage is not a token verdict and maps to `503`.
- Rate limiting and empty-bodied framework errors are converted to the same envelope by `ApiErrorProblemDetailsService` and status-code pages.
- Exception text MUST NOT be returned to the caller. The exception classifier chooses a fixed safe message and status.

## Status Rules

HTTP semantics in RFC 9110 takes precedence if this repository prose conflicts with the protocol. Within those semantics, the GSS mapping is mandatory:

| Outcome | Status |
| --- | --- |
| Success | `200`, `201` or `204` |
| Invalid request/business failure | `400` |
| Unauthenticated / forbidden | `401` / `403` |
| Missing resource | `404` |
| Conflict | `409` |
| Reused idempotency key for a different request | `422` |
| Rate limit | `429` |
| Dependency failed / unavailable / timed out | `502` / `503` / `504` |
| Unclassified server bug | `500` |

Never choose a status by parsing exception-message wording.

## Headers and Health Probes

Normal API responses carry pipeline headers such as correlation and trace identifiers. Retryable rate-limit/dependency responses may carry `Retry-After`. Idempotent replays carry `Idempotent-Replayed: true`.

Health probe endpoints are the exception to the JSON envelope: HealthChecks.NET owns their operational payload. Do not wrap probe results in `Response<T>`.

## Testing

Controller tests SHOULD assert status, envelope shape, and mapping. Integration coverage in `ErrorEnvelopeIntegrationTests`, `InputBoundaryIntegrationTests`, and `OpenApiIntegrationTests` protects framework errors and public metadata. New statuses MUST be documented with `ProducesResponseType` and tested.

## References

- [RFC 9110: HTTP Semantics](https://www.rfc-editor.org/rfc/rfc9110)
- [Error contract](../architecture/error-contract.md)
- [Exception handling](../architecture/exception-handling.md)
