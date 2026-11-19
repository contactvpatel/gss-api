# Service Registration Layering

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Layer Responsibilities](#layer-responsibilities)
- [Registration Locations](#registration-locations)
- [Dependency Flow](#dependency-flow)
- [Why This Design?](#why-this-design)
- [For New Developers](#for-new-developers)
- [Related Documentation](#related-documentation)

---

## Overview

This document explains the intentional dual registration of SSO, MIS, and ASM services across Infrastructure and Business layers. This is **by design** and does not represent a conflict.

Both `HttpClientRegistrationExtensions` (Infrastructure) and `Business/DependencyInjection` (Business) register services with similar names: `ISsoService`, `IMisService`, `IAsmService`. These are **different interfaces** in different namespaces.

## Layer Responsibilities

| Layer | Interface | Implementation | Purpose |
|-------|-----------|----------------|---------|
| **Core** | `GSS.Core.Interfaces.Services.ISsoService` | `SsoService` (Infrastructure) | Raw HTTP calls to external SSO API |
| **Core** | `GSS.Core.Interfaces.Services.IMisService` | `MisService` (Infrastructure) | Raw HTTP calls to external MIS API |
| **Core** | `GSS.Core.Interfaces.Services.IAsmService` | `AsmService` (Infrastructure) | Raw HTTP calls to external ASM API |
| **Business** | `GSS.Business.Services.Interfaces.ISsoService` | `SsoService` (Business) | Wraps Core: token validation caching, cache cleanup on renew/logout, DTO mapping |
| **Business** | `GSS.Business.Services.Interfaces.IMisService` | `MisService` (Business) | Wraps Core with caching, DTO mapping |
| **Business** | `GSS.Business.Services.Interfaces.IAsmService` | `AsmService` (Business) | Wraps Core with caching (per token, until `exp`), DTO mapping |

## Registration Locations

- **Infrastructure** (`HttpClientRegistrationExtensions.AddInfrastructureHttpClients`): Registers Core interfaces with Infrastructure implementations as typed HTTP clients (`AddHttpClient<ICore, Impl>("Name")` plus `AddHttpResilienceHandler()`). Typed clients are transient: never inject one into a singleton.
- **Business** (`DependencyInjection.AddBusinessServices`): Registers Business interfaces with Business implementations (scoped; they depend on Core interfaces).

## Dependency Flow

```text
API controllers, SsoAuthenticationHandler (CallerValidator uses IMisService and AsmPermissionHandler uses IAsmService the same way)
    → Business.Services.Interfaces.ISsoService (injected)
        → Business.Services.SsoService
            → Core.Interfaces.Services.ISsoService (injected)
                → Infrastructure.Services.External.SsoService (HTTP client)
```

## Why This Design?

1. **Separation of concerns**: Infrastructure handles HTTP; Business handles caching and domain logic.
2. **Testability**: Controllers are tested with mocks of the Business interfaces, and Business services with mocks of the Core interfaces.
3. **No circular dependency**: Business depends on Core; Infrastructure implements Core. Business wraps Core services.

## For New Developers

When you see both `HttpClientRegistrationExtensions` and `Business/DependencyInjection` registering "the same" services, remember: they register **different interfaces**. There is no conflict. The API layer consumes only the Business interfaces; `ApiLayerDependencyTests` fails if a controller, filter, handler or middleware uses a Core `I{Sso,Mis,Asm}Service`.

## Related Documentation

- [HttpClient factory guide](../guides/httpclient-factory.md)
- [Project structure](project-structure.md)
