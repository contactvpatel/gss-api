# HttpClient Factory and External Services

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [GSS Client Model](#gss-client-model)
- [Registration and Configuration](#registration-and-configuration)
- [Implementing a Client](#implementing-a-client)
- [Headers and Serialization](#headers-and-serialization)
- [Failures and Transactions](#failures-and-transactions)
- [Testing](#testing)
- [References](#references)

---

## Overview

GSS uses typed clients from `IHttpClientFactory` for SSO, MIS, and ASM. `HttpClientRegistrationExtensions` owns registration, validated base addresses, permanent application headers, response-size limits, and the HttpResilience.NET handler. `HttpServiceBase` owns the shared send/deserialize/error path.

## GSS Client Model

There are two service layers with intentionally different interfaces:

- Core `I{Sso,Mis,Asm}Service` contracts are implemented by Infrastructure typed HTTP clients.
- Business `I{Sso,Mis,Asm}Service` contracts wrap those clients with DTO mapping and caching.

API controllers, authentication, and authorization depend on Business interfaces. Typed Infrastructure clients are transient; never inject one into a singleton and never add a second scoped registration for the same Core interface.

`HttpServiceBase` exposes shared GET/POST send paths and MIS/ASM envelope helpers. It receives `IDapperTransactionManager` so it can reject a network call while the request's database transaction is open.

## Registration and Configuration

Registration occurs in `src/GSS.Infrastructure/Extensions/HttpClientRegistrationExtensions.cs`:

```csharp
services.AddHttpResilience(configuration);

services.AddHttpClient<IMyService, MyService>("MyService", (serviceProvider, client) =>
    {
        // Resolve validated options and set the base address/default headers here.
    })
    .AddHttpResilienceHandler();
```

The example is illustrative; a real GSS client goes through the private `RegisterHttpClient` helper in that file so behavior stays consistent.

External URLs must be HTTPS, must not target loopback/private addresses, and are normalized with a trailing slash. Endpoint option values are relative paths without a leading slash. MIS/ASM application IDs and secrets are required and are placed in `DefaultRequestHeaders` once at client creation.

Each client inherits the root `HttpResilience` configuration and may override it under `HttpResilience:Clients:{clientName}`. A client-specific section that no registered client reads fails host startup (HttpResilience.NET validates it). The client name is the explicit registration name such as `MisService`.

## Implementing a Client

The constructor includes the transaction manager, as the base class requires:

```csharp
public sealed class MyService(
    HttpClient httpClient,
    IDapperTransactionManager transactionManager,
    IOptions<MyServiceOptions> options,
    ILogger<MyService> logger)
    : HttpServiceBase(httpClient, transactionManager, logger, nameof(MyService)), IMyService
{
    private readonly MyServiceOptions _options = options.Value;
}
```

This is illustrative feature/integration code; `MyService` is not shipped by GSS.

Use endpoints relative to `HttpClient.BaseAddress`. Escape user-controlled query values and pass the request cancellation token. Do not create a new `HttpClient`, set `client.Timeout`, install a second Polly pipeline, or construct a custom `SocketsHttpHandler` in a service.

## Headers and Serialization

- Put stable application credentials on `DefaultRequestHeaders` during registration.
- Put bearer tokens and other per-call values on the `HttpRequestMessage` through the shared configure callback. Never mutate default headers per request.
- Use the source-generated `JsonContext` overloads in `HttpServiceBase`; register every external response type and every `ExternalApiResponse<T>` wrapper used by MIS/ASM.
- `GetListAsync<T>` treats a successful envelope with `data: null` as an empty list, but a missing body or `succeeded: false` as an upstream failure.
- `GetSingleOrNullAsync<T>` returns null for upstream 404 or when no returned item matches the requested ID. It does not trust the upstream filter blindly.

No serialization speed percentage is claimed. Source generation is used to make metadata explicit and trimming-friendly; measure a representative workload before asserting performance.

## Failures and Transactions

HttpResilience.NET provides concurrency limiting, total/attempt timeouts, retries for safe HTTP methods, a circuit breaker, and connection settings. SSO calls are POST and are not retried by the method policy. See the resilience architecture document for the configured order and defaults.

A non-2xx response is thrown as an `HttpRequestException` (status plus sanitized body); `HttpServiceBase` then uses `HttpErrorHandler.ToExternalServiceException` to turn it, and transport/resilience failures, into Core `ExternalServiceException` values with a failure classification (timeout 504, unavailable 503, failed 502). Upstream response detail is sanitized before it can reach the boundary log. The shared HTTP layer does not log and rethrow; `ApiExceptionHandler` or the authentication boundary logs once.

Never call an external service while a scoped Unit of Work transaction is active. `HttpServiceBase` detects that state and throws because waiting on the network would retain a database connection and locks. Fetch external data first, then open the transaction for the writes.

## Testing

- `HttpClientRegistrationTests` covers base-address trailing-slash handling, per-request app credential headers, and blank-credential failure.
- `HttpServiceBaseTests` covers send paths, envelope behavior, malformed-JSON failures, non-2xx errors, blank endpoints, and transaction rejection.
- SSO/MIS/ASM service tests cover each concrete endpoint contract.
- Unknown `HttpResilience:Clients` sections are rejected by HttpResilience.NET at host startup, not by a repository test.

## References

- [IHttpClientFactory guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory)
- [Resilience architecture](../architecture/resilience.md)
- [Service registration layering](../architecture/service-registration-layering.md)
