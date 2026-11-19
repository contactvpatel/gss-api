# Idempotency and Concurrent Writes

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [HTTP Method Behavior](#http-method-behavior)
- [Idempotency-Key for Create POSTs](#idempotency-key-for-create-posts)
- [Transaction Model](#transaction-model)
- [Retention and Cleanup](#retention-and-cleanup)
- [Current Scaffold Status](#current-scaffold-status)
- [Concurrent Updates](#concurrent-updates)
- [Verification](#verification)
- [References](#references)

---

## Overview

GSS provides an opt-in `[Idempotent]` resource filter for authenticated create POST actions. It also defines the expected repeat behavior for ordinary update, patch, and delete operations. The safeguard prevents duplicate creates caused by client retries; it does not provide optimistic concurrency for competing updates.

## HTTP Method Behavior

- **GET** operations are read-only.
- **PUT/PATCH** map the supplied update DTO onto the current entity. Repeating the same request produces the same state; when no value changes, the Business base service skips the write.
- **DELETE** is a soft delete. Repeating a delete for an already-deleted ID succeeds; an ID that never existed is not found.
- **Batch update/delete** validate the full input and follow the same semantics at set level.
- **POST create** is not naturally idempotent and should use `[Idempotent]` when a feature adds a create route.

## Idempotency-Key for Create POSTs

With `[Idempotent]`, a request may send one `Idempotency-Key` header. A valid key contains 1–255 printable ASCII characters excluding spaces and control characters. Set `Required = true` on the attribute only when clients must always provide it.

The request hash covers method, path, query string, and body. Behavior is scoped to the authenticated numeric user ID and key:

| Situation | Result |
| --- | --- |
| No header and `Required` is false | The action runs without idempotency storage. |
| Missing required, empty, repeated, invalid, or oversized header | `400` in the normal error envelope. |
| First valid request | The action runs. A successful 2xx response is stored with its status, body, and optional `Location`. |
| Same key and same request within retention | The stored response is replayed with `Idempotent-Replayed: true`. |
| Same key and a different request | `422`; the original response remains authoritative. |
| First attempt returns a non-2xx response or throws | The transaction rolls back and nothing is stored, so a later corrected request may reuse the key. |

Only JSON action responses are appropriate because replay writes the stored JSON body with `application/json; charset=utf-8`.

## Transaction Model

`IdempotencyService.BeginAsync` opens the request's Unit of Work transaction. `IdempotencyRepository` then takes a PostgreSQL transaction-scoped advisory lock derived from user ID and key and looks for an unexpired row in `gss.idempotencykey`.

On a miss, the action runs while that transaction remains open. The resource filter buffers the response. For a 2xx outcome, the service saves the response and commits; feature writes and the idempotency record therefore become visible together. The buffered response is sent only after the commit succeeds.

On a replay or mismatch, the service rolls back to release the lock before responding. On an error or exception, `AbortAsync` rolls back without masking the original failure.

Do not put `[Idempotent]` on an action that calls SSO, MIS, or ASM. Outbound HTTP is prohibited while a Unit of Work transaction is active because it would hold the database connection and locks across a network call.

## Retention and Cleanup

Stored responses are eligible for replay for 24 hours. `IdempotencyCleanupHostedService` waits one hour before its first run, then deletes expired rows hourly in batches of 1,000. Every pod may run cleanup; concurrent deletion is harmless. Reads ignore expired rows even if cleanup has not removed them yet.

The retention and cleanup clocks use injected `TimeProvider` instances.

## Current Scaffold Status

The domain-neutral GSS scaffold ships no business create endpoint. It ships the generic filter, service, repository, migration, cleanup service, and OpenAPI transformer support. Integration tests exercise the end-to-end behavior through `tests/GSS.IntegrationTests/Fixtures/IdempotencyProbeController.cs`, which exists only in the test host.

When a feature adds a create endpoint, the developer must choose whether retries need this contract and apply `[Idempotent]` deliberately.

## Concurrent Updates

GSS currently uses last-write-wins for PUT, PATCH, and DELETE. It does not expose a row version, ETag, or `If-Match` contract. Existing protections prevent partial batch writes and map rows deleted during an update to not found, but they do not detect two valid clients overwriting one another.

If a feature requires lost-update detection, design it end to end: persistent version data, response ETag, required `If-Match`, atomic repository predicate, `412 Precondition Failed`, OpenAPI documentation, and integration tests. Adding only a header or only a database column is insufficient.

## Verification

- `IdempotentAttributeTests` covers header validation, hashing, buffering, replay, mismatch, and abort behavior.
- `IdempotencyServiceTests` covers transaction and retention decisions.
- `IdempotencyKeyIntegrationTests` covers PostgreSQL locking and replay through the test-only probe.
- `IdempotencyCleanupHostedServiceTests` covers deleting expired rows in batches.

## References

- [RFC 9110: Idempotent Methods](https://www.rfc-editor.org/rfc/rfc9110.html#name-idempotent-methods)
- [PostgreSQL advisory locks](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS)
- [API response formats](../standards/api-response-formats.md)
