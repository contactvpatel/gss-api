# Cancellation Token Guidelines

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Required Signatures](#required-signatures)
- [Propagation](#propagation)
- [Transactions and Cleanup](#transactions-and-cleanup)
- [Exceptions](#exceptions)
- [Testing](#testing)
- [References](#references)

---

## Overview

Cancellation is part of every GSS asynchronous request path. Public application methods MUST accept and propagate a `CancellationToken` unless an implemented framework interface fixes the signature.

## Required Signatures

- Put `CancellationToken cancellationToken = default` last on public async methods owned by GSS.
- Controller actions MUST accept a token and pass it to Business services.
- Business, repository, cache, Dapper, and external HTTP calls MUST receive the same token unless a documented cleanup rule requires otherwise.
- Do not add a token parameter to an override or interface implementation whose contract has no token. Use the token exposed by that framework context, such as `HttpContext.RequestAborted`, when appropriate.

Microsoft's task-cancellation guidance takes precedence where it conflicts with this document; GSS adds the repository-specific propagation rules above.

## Propagation

Pass the token to every cancellable operation:

```csharp
public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
{
    return repository.GetAllAsync(cancellationToken);
}
```

Do not replace a caller token with `CancellationToken.None`, create an unrelated token source, or catch `OperationCanceledException` merely to continue ordinary work. In Business and Infrastructure, use `ConfigureAwait(false)` as the repository convention.

## Transactions and Cleanup

Cancellation may occur after a remote or database side effect has started. Preserve atomic boundaries:

- a Unit of Work rolls back when the operation does not commit;
- idempotent response storage commits with the create;
- database calls inside one transaction remain sequential;
- do not retry or start replacement work after caller cancellation.

Cleanup that must release security state or a held transaction may intentionally ignore the disconnected request token. Current examples include idempotency rollback and cache eviction after an SSO call may already have changed remote state. Such use of `CancellationToken.None` MUST be narrow, commented, and must not mask the original failure.

## Exceptions

Background services receive the host stopping token and should end normally on shutdown cancellation. Framework-owned callbacks may expose cancellation through a context rather than a parameter.

For non-cancellable in-memory work, keep the token in the API so downstream I/O can use it; do not add arbitrary `ThrowIfCancellationRequested` calls to tiny synchronous transformations unless they bound a meaningful loop.

## Testing

Tests SHOULD prove propagation where a boundary matters by using a canceled token or a mock that captures the token. Also test rollback/cleanup when cancellation occurs after a transaction or external call begins. Do not use timing sleeps as cancellation proof when a controllable task completion source can make the ordering deterministic.

## References

- [Task cancellation](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-cancellation)
- [Cancellation in managed threads](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)
- [Dapper repository architecture](../architecture/dapper-hybrid-approach.md)
