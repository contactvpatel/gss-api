# Transaction Startup Lifecycle Design

> **Archived design record (2026-11-17).** This document records the selected transaction-startup lifecycle design. It is not current guidance, and any unchecked work described here does not indicate current implementation status. See the [current documentation](../../../.docs/README.md) and [repository guidance](../../../CLAUDE.md).

## Intent

Prevent `DapperTransactionManager` from retaining or losing ownership of a connection when transaction startup fails, and prevent new work after the manager has been disposed.

## Selected approach

Acquire, open, and begin the transaction using local variables. Publish `_writeConnection` and `_currentTransaction` only after startup succeeds. On failure, dispose the locally owned connection immediately while preserving the original exception. Guard every public operation other than idempotent `DisposeAsync` with `ObjectDisposedException`.

Alternatives rejected:

- Publishing `_writeConnection` before startup and clearing it in `catch` leaves partially initialized shared state and makes retry correctness harder to reason about.
- Forbidding retry after startup failure is unnecessarily restrictive; a clean local acquisition model makes retry safe.

## Scope

- Modify only `DapperTransactionManager`, its focused unit tests, and the shared fake connection used by those tests.
- Preserve successful begin/commit/rollback behavior and idempotent disposal.
- Do not change dependency injection lifetimes, transaction APIs, or unrelated database code.

## Verification

Tests must cover open failure, begin failure, retry after failure, and every public operation after disposal. The focused transaction-manager tests and full unit suite must pass.
