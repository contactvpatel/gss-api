# ASM Authorization

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Usage](#usage)
- [How a Request Is Checked](#how-a-request-is-checked)
- [MIS Endpoints](#mis-endpoints)
- [Caching](#caching)
- [Failures](#failures)
- [Testing](#testing)
- [References](#references)

---

## Overview

Module permissions come from ASM (Application Security Management) and are enforced with standard ASP.NET Core authorization: `[AsmAuthorize]` carries an `AsmPermissionRequirement`, and [AsmPermissionHandler.cs](../../src/GSS.Api/Authorization/AsmPermissionHandler.cs) checks it. The handler is a singleton and resolves scoped `ICallerValidator` and `IAsmService` from `HttpContext.RequestServices` only when checking an `[AsmAuthorize]` requirement; other authorized requests never construct the MIS/ASM clients. Permissions are evaluated for **one position**, selected by caller headers. There is no on/off switch and no super-admin bypass.

## Usage

No scaffold controller uses `[AsmAuthorize]` yet and no policy is registered; the snippets below are illustrative.

```csharp
[AsmAuthorize(ModuleCode.Application, AccessType.View)]                     // single permission
[AsmAuthorize(ModuleCode.Application, AccessType.Create, AccessType.Update)] // OR within one module
[AsmAuthorize(ModuleCode.Application, AccessType.View)]
[AsmAuthorize(ModuleCode.Administration, AccessType.View)]              // two attributes = AND
```

Cross-module OR uses a named policy (the name is illustrative):

```csharp
services.AddAuthorizationBuilder().AddPolicy("application-or-administration", p => p.AddRequirements(
    AsmPermissionRequirement.Any(new(ModuleCode.Application, AccessType.View), new(ModuleCode.Administration, AccessType.View))));
```

`ModuleCode` descriptions in [AsmPermission.cs](../../src/GSS.Api/Authorization/AsmPermission.cs) must match the module codes configured in ASM for this application. Matching is case-sensitive. They are read once into a frozen lookup (`ModuleCodeExtensions.ToAsmCode`); every module needs one, unique (`ModuleCodeExtensionsTests`). `AccessType.AllowAny` passes when the selected position has any module access.

## How a Request Is Checked

1. The caller is authenticated by the `SSOv1` scheme ([SSO Authentication](sso-authentication.md)).
2. Four headers are read, all required:

   | Header | Value |
   | --- | --- |
   | `x-app-position-id` | Positive integer |
   | `x-app-role-id` | Positive integer |
   | `x-app-division-id` | Positive integer (one division) |
   | `x-app-department-id` | Comma-separated positive integers (a position can span several departments) |

3. **Caller check (MIS).** `IMisService.GetPersonPositionsAsync` returns the caller's positions, with each department's division taken from the MIS department list. The header position must be one of them, the header role must be its role, the header division must be the division of one of its departments, and **every** header department must be one of its departments. The check lives in `CallerValidator` (`ICallerValidator`, scoped); it caches its result in `HttpContext.Items`, so every caller in the same request (the handler for each `[AsmAuthorize]` attribute, or a controller) validates once and calls MIS once.
4. **Permission check (ASM).** `IAsmService.GetApplicationSecurityAsync(token, tokenExpiresAtUtc, ct)` returns the caller's ASM entries; ASM identifies the user from the token, so there is no person ID parameter. The entry matching the position **and** role is selected.
5. The requirement passes if that entry grants any of the required module/access pairs. Other positions never count.

## MIS Endpoints

Every MIS endpoint that returns division data is scoped to the caller's division. No division is assumed. Paths below are relative to `api/v{version}`.

| Endpoint | Scope check |
| --- | --- |
| `GET /mis/departments`, `/mis/roletypes`, `/mis/roles` | Listed for the caller's division |
| `GET /mis/departments/{departmentId}` | The department must be in the caller's division (else 404) |
| `GET /mis/roles/{id}` | The role must be in the caller's division (else 404) |
| `GET /mis/roles/departments/{departmentId}` | The department (looked up with the cached `GetDepartmentByIdAsync`) must exist and be in the caller's division (else 404) |
| `GET /mis/positions/roles/{roleId}` | The role (looked up with the cached `GetRoleByIdAsync`) must exist and be in the caller's division (else 404) |

- They all require the four `x-app-*` headers. `MisController` calls `ICallerValidator` (the same request-cached check the `[AsmAuthorize]` handler uses), so MIS is called once per request for the caller check. Missing or invalid headers answer 403 before any other MIS call.
- The division is the validated `x-app-division-id`.
- The three lists take no division parameter: the division is always the caller's.
- Missing or invalid headers answer 403 `Access denied`.
- A department or role in another division answers the same 404 as a missing ID, so a caller cannot probe which IDs exist in other divisions.
- In both cases the reason is logged as a Warning, never returned.
- `GET /mis/person-positions` needs no `x-app-*` headers: it lists the authenticated user's positions, which is how a client discovers valid header values.
- `GET /mis/divisions` lists the configured divisions (`MisService:Divisions`, ordered by id) and needs no `x-app-*` headers.
- OpenAPI lists `x-app-*` headers exactly where `[RequiresCallerHeaders]` or `[AsmAuthorize]` checks them, together with the possible 403; other authenticated operations list only the bearer token.
- `MisService:Divisions` entries from `appsettings.json` merge with environment overrides: an environment variable can rename a division (`MisService__Divisions__<id>=<name>`) or add one, but cannot remove a key that `appsettings.json` defines.

## Caching

- **ASM:** `AsmService` caches the full response per token hash until the token's `exp`; without an expiry it is not cached. Switching positions needs no extra ASM call. Logout and successful renewal clear the entry.
- **MIS:** `MisService` caches person positions per person until the token's `exp` (logout clears the entry; no lifetime → not cached), and the full department list and role types for 24 hours, and roles and role lookups for 12 hours (a role missing in MIS for 5 minutes). Positions by role are not cached.

ASM entries and person positions use `FailSafe = false`, so stale authorization data is never served. MIS reference data (departments, role types, roles) keeps fail-safe for twice its lifetime (48 hours for the 24-hour entries, 24 hours for the 12-hour ones).

## Failures

| Case | Response |
| --- | --- |
| Not authenticated | 401 (challenge from `SSOv1`) |
| Missing or invalid x-app-* headers; position, role, division or department not the caller's; or missing permission | 403 `Access denied` (reason logged, not returned) |
| A department or role by ID outside the caller's division (MIS endpoints) | 404, identical to a missing ID (reason logged, not returned) |
| MIS or ASM unavailable | `ApiExceptionHandler`: 504 timeout, 503 unavailable, 502 other failure (fail closed) |
| Client disconnected | Cancellation propagates; `ApiExceptionHandler` answers 499, logged at Information, not as an error |

## Testing

- [CallerValidatorTests.cs](../../tests/GSS.UnitTests/API/Authorization/CallerValidatorTests.cs) covers the caller check: header parsing (missing, non-positive, malformed, comma-separated departments with whitespace), the MIS position/role/division/department checks, the per-request cache and re-validation when the request's user changes.
- [AsmPermissionHandlerTests.cs](../../tests/GSS.UnitTests/API/Authorization/AsmPermissionHandlerTests.cs) runs the handler through a real `IAuthorizationService`. It covers the ASM step, failed caller checks, and that a request without an `[AsmAuthorize]` requirement resolves neither scoped service.
- [MisControllerTests.cs](../../tests/GSS.UnitTests/API/Controllers/MisControllerTests.cs) covers the division scoping of every MIS endpoint and the generic 403 body; `InputBoundaryIntegrationTests.MissingCallerHeaders_Returns403` pins the 403 over HTTP.
- The MIS wire format is covered in `Infrastructure/Services/External/MisServiceTests.cs`.

## References

- [ASP.NET Core authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction)
- [SSO authentication](sso-authentication.md)
