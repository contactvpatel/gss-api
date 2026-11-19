# MIS Multi-Division Support Implementation Plan

> **Archived implementation record (2026-11-17).** This plan records the MIS multi-division change and its review corrections. It is not current guidance, and unchecked boxes do not indicate current implementation status. See the [current documentation](../../../.docs/README.md) and [repository guidance](../../../CLAUDE.md).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the hardcoded MIS division `1` from GSS and scope division-filtered MIS reads to the caller's validated division, as `asm-api` does.

**Architecture:** The division comes from the caller's `x-app-division-id` header after it has been checked against the caller's MIS position. That check moves out of `AsmPermissionHandler` into a reusable, per-request-cached `ICallerValidator`. `MisController` resolves the division through it, and an explicit `?divisionId=` must match the caller's division. Role-by-id calls MIS directly instead of filtering the division-1 role cache. The MIS query strings are corrected to the contract `asm-api` uses in production, once Task 1 confirms it.

**Tech Stack:** .NET 10, ASP.NET Core authorization, Caching.NET, Mapster, FluentValidation, xUnit/Moq/FluentAssertions.

**Spec:** No separate spec. The reference implementation is `asm-api`:
- `asm-api/src/ASM.Api/Controllers/MisController.cs` (`ResolveDivisionId`, `GetDivisions`, `GetRoleById`)
- `asm-api/src/ASM.Api/Authorization/CallerValidator.cs` (header check against the MIS position, cached in `HttpContext.Items`)
- `asm-api/src/ASM.Infrastructure/Services/MisService.cs` (MIS query strings)
- `asm-api/src/ASM.Business/Services/MisService.cs` (`GetRoleById` calls MIS directly)

## What asm-api does (and GSS does not)

| Concern | asm-api | GSS today |
| --- | --- | --- |
| Division for list endpoints | `ResolveDivisionId(int? divisionId)`: omitted gives the caller's validated header division; a different one is allowed only for a Department Access admin, otherwise 403 | `DivisionQuery.DefaultDivisionId = 1`; any authenticated caller can read any division |
| Caller validation | `CallerValidator`, shared by filters and MIS endpoints, cached per request | Private `ValidateCallerAsync` inside `AsmPermissionHandler`; MIS endpoints never validate |
| Role by id | `misService.GetRoleById(id)` (MIS `Role?roleId=`) | Filters cached `GetAllRolesAsync(1)`; roles in other divisions return 404 |
| Division list | `GET /mis/divisions` from `AppSettings:Divisions` | none |
| MIS query strings | `Role?divId=`, `Division/RoleType?divId=`, `Role?roleId=`, `Role?deptId=`, `Position?roleId=` | `?divisionId=`, `Role/{id}`, `Role/departments/{id}`, `Position/roles/{id}` |

The last row matters most: if MIS ignores an unknown `divisionId` parameter, GSS's "per-division" role and role-type lists are not filtered at all. Task 1 settles this before any code changes.

## Decisions (change before execution if wrong)

1. **Cross-division reads are forbidden (403).** GSS has no Department Access admin and no super-admin bypass (`.docs/architecture/asm-authorization.md`). A project that needs cross-division reads adds its own `[AsmAuthorize]`-gated exception.
2. **Division resolution happens in the action, not in an authorization requirement.** That keeps model validation first, so `?divisionId=-1` still returns 400 (`InputBoundaryIntegrationTests`), and it matches asm-api's `ResolveDivisionId`.
3. **Every caller-check failure is 403**, the same as `AsmPermissionHandler` today. asm-api's 401 for a missing header is not copied.
4. **Every division-bearing MIS endpoint needs headers and is scoped to the caller's division:** the three division-filtered lists (`GET mis/departments`, `mis/roletypes`, `mis/roles`) and the by-id/by-parent endpoints `mis/departments/{departmentId}`, `mis/roles/{id}`, `mis/roles/departments/{departmentId}` and `mis/positions/roles/{roleId}` (the department or role, looked up through the cached `IMisService` call, must be in the caller's division, otherwise 403). Only `person-positions` and `divisions` stay header-free: the UI calls them to choose a position before it can send headers. (Corrected in the final review: the earlier text said the by-id and by-parent endpoints carry no division, a misreading of asm-api, which requires the caller headers on `roles/departments/{departmentId}` and `positions/roles/{roleId}` and scopes them to the caller's division.)
5. **The division list endpoint (Task 6) is optional.** Drop it if no GSS client needs a division picker.
6. **Scope is GSS only.** `boilerplate-net` had the same defect in its equivalent MIS service and division-query model. Port it there afterwards so the scaffold stays in sync.

## Global Constraints

- Explicit types, no `var`; `ConfigureAwait(false)` in Business/Infrastructure; `CancellationToken` on every async method.
- Cache keys only via `CacheKeys`; no try/catch around cache calls; `FailSafeMaxDuration` longer than the expiration.
- Every endpoint returns `Response<T>`; errors carry `ApiError` built by `ApiControllerBase`.
- Injected dependencies are required (no `= null` constructor parameters).
- Tests carry `[Trait("Category", TestCategories.Unit)]` or `Integration`.
- Preserve each file's existing line endings (CRLF for template files).
- Breaking API changes, to be called out in the PR: `divisionId=0` becomes 400 (0 no longer means "default"); the three list endpoints return 403 without valid `x-app-*` headers; `divisionId` different from the header returns 403.

## Review Focus

1. **Header division valid, `?divisionId` omitted:** the response uses the header division and never division 1. Pinned in Task 5 (`GetAllRoles_NoQuery_UsesCallerDivision` with division 7).
2. **Caller sends `x-app-division-id` for a division their position does not cover:** 403 before any MIS list call. Pinned in Task 4 (`Validate_DivisionNotOnPosition_Fails`) and Task 5 (`GetAllRoles_CallerInvalid_Returns403AndSkipsMis`).
3. **`[AsmAuthorize]` endpoint plus a MIS lookup in one request:** MIS person positions are fetched once, because the validated caller is reused from `HttpContext.Items`. Pinned in Task 4 (`Validate_TwiceInOneRequest_CallsMisOnce`).
4. **Role id that MIS does not know:** 404, and the miss is not cached for 12 h. Pinned in Task 3 (`GetRoleByIdAsync_Missing_ReturnsNullWithShortLifetime`).
5. **`person-positions` called with no `x-app-*` headers:** still 200. Pinned in Task 5 (`GetPersonPositions_NoHeaders_DoesNotValidateCaller`).

---

### Task 1: Confirm the MIS query contract (gate, no code)

**Files:** none. Record the results in the PR description.

- [ ] **Step 1: Call MIS UAT with both URL forms**

Needs the MIS app credentials in your shell. Never commit them.

```bash
H=(-H "x-baps-auth-app-id: $MIS_AUTH_APP_ID" -H "x-baps-auth-app-secret: $MIS_AUTH_APP_SECRET")
B=https://api.uat.bapsapps.org/myseva/api/v1
for u in "Role?divId=2" "Role?divisionId=2" "Division/RoleType?divId=2" "Division/RoleType?divisionId=2" \
         "Role?roleId=<known id>" "Role/<known id>" "Role?deptId=<known dept>" "Role/departments/<known dept>" \
         "Position?roleId=<known id>" "Position/roles/<known id>"; do
  printf '%-40s ' "$u"; curl -s "${H[@]}" "$B/$u" | jq -c '[.succeeded, (.data|length), ([.data[]?.divisionId]|unique)]'
done
```

- [ ] **Step 2: Decide**

Expected: the `divId` / `roleId=` / `deptId=` forms succeed, and `Role?divId=2` returns only `divisionId` 2. If `?divisionId=2` returns several divisions, or the path forms fail, Task 2 is required. If the GSS forms are also correct, skip Task 2 and note that in the PR.

---

### Task 2: Use asm-api's MIS query strings

**Files:**
- Modify: `src/GSS.Infrastructure/Services/External/MisService.cs:90,113,136,166,189`
- Test: `tests/GSS.UnitTests/Infrastructure/Services/External/MisServiceTests.cs`

**Interfaces:** No signature changes. Core `IMisService` stays as is.

- [ ] **Step 1: Write the failing tests**

Add `VerifyRequest` assertions to the existing tests (the helper matches `PathAndQuery.Contains`):

```csharp
// GetAllRoleTypesAsync_ValidDivisionId_ReturnsRoleTypes (division 7)
VerifyRequest(mockHandler, HttpMethod.Get, "RoleType?divId=7");
// GetAllRolesAsync_ValidDivisionId_ReturnsRoles (division 7)
VerifyRequest(mockHandler, HttpMethod.Get, "Role?divId=7");
// GetRoleByIdAsync_ValidId_ReturnsRole (role 1)
VerifyRequest(mockHandler, HttpMethod.Get, "Role?roleId=1");
// GetRolesByDepartmentIdAsync_ValidDepartmentId_ReturnsRoles
VerifyRequest(mockHandler, HttpMethod.Get, $"Role?deptId={departmentId}");
// GetPositionsByRoleIdAsync_ValidRoleId_ReturnsPositions
VerifyRequest(mockHandler, HttpMethod.Get, $"Position?roleId={roleId}");
```

Also add `GetRoleByIdAsync_EmptyData_ReturnsNull`: a 200 response with `data: []` returns `null`, because MIS reports a missing role as an empty list on this endpoint.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/GSS.UnitTests --no-restore --filter "FullyQualifiedName~Infrastructure.Services.External.MisServiceTests"`
Expected: five FAILs from `Moq.MockException` (no matching request).

- [ ] **Step 3: Change the five endpoint strings**

Use `?divId=`, `?divId=`, `?roleId=`, `?deptId=` and `?roleId=` on `RoleType`, `Role`, `Role`, `Role` and `Position`. Keep the existing 404 → `null` catch in `GetRoleByIdAsync`. Update the log messages that describe the URL.

- [ ] **Step 4: Run them and see them pass**

Same command. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/GSS.Infrastructure/Services/External/MisService.cs tests/GSS.UnitTests/Infrastructure/Services/External/MisServiceTests.cs
git commit -m "fix: call MIS with the divId/roleId/deptId query contract used by asm-api"
```

---

### Task 3: Role by id from MIS, cached per role

**Files:**
- Modify: `src/GSS.Core/Helpers/CacheKeys.cs` (after `RolesByDepartment`)
- Modify: `src/GSS.Business/Services/MisService.cs:106-112`
- Test: `tests/GSS.UnitTests/Business/Services/MisServiceTests.cs`, `tests/GSS.UnitTests/Business/Helpers/CacheKeysTests.cs`

**Interfaces:**
- Produces: `public static string CacheKeys.RoleById(int roleId)` returning `$"role-{roleId}"`, which throws `ArgumentOutOfRangeException` for `roleId < 1` (same guard as `Roles`).
- `IMisService.GetRoleByIdAsync(int, CancellationToken)` keeps its signature.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task GetRoleByIdAsync_RoleInDivision3_ReturnsIt()
// core GetRoleByIdAsync(42) returns RoleModel { Id = 42, DivisionId = 3 } → result.Id == 42, result.DivisionId == 3;
// coreMis.Verify(m => m.GetAllRolesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never)

[Fact] public async Task GetRoleByIdAsync_CalledTwice_CallsMisOnce()
// real TestCache; coreMis.Verify(m => m.GetRoleByIdAsync(42, It.IsAny<CancellationToken>()), Times.Once)

[Fact] public async Task GetRoleByIdAsync_Missing_ReturnsNullWithShortLifetime()
// core returns null → result null; advance the FakeTimeProvider/TestCache clock 6 minutes, call again → core called twice

// CacheKeysTests
[Fact] public void RoleById_FormatsKey() => CacheKeys.RoleById(42).Should().Be("role-42");
[Fact] public void RoleById_RejectsZero() => FluentActions.Invoking(() => CacheKeys.RoleById(0)).Should().Throw<ArgumentOutOfRangeException>();
```

If `TestCache` cannot advance time, assert the miss's overrides by capturing them through a `Mock<ICacheService>` instead (failure injection is the only allowed use of a mock; this is an observation, so prefer extending `TestCache` with the clock if needed).

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/GSS.UnitTests --no-restore --filter "FullyQualifiedName~MisServiceTests|FullyQualifiedName~CacheKeysTests"`
Expected: FAIL (`RoleById` does not exist; the division-3 role is not found).

- [ ] **Step 3: Implement**

Add `CacheKeys.RoleById`. Rewrite `GetRoleByIdAsync` to call the context-taking `cacheService.GetOrSetAsync<RoleDto?>(CacheKeys.RoleById(roleId), (ctx, cancel) => ...)`. It calls `coreMisService.GetRoleByIdAsync`, maps with `Adapt<RoleDto>()`, and uses the same 12 h / 12 h / 24 h fail-safe overrides as `GetAllRolesAsync`. When the role is null, set `ctx.Overrides` to 5 minutes local and distributed, with `FailSafeMaxDuration` at 10 minutes. Delete the "division-1" comment.

- [ ] **Step 4: Run them and see them pass**

Same command. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/GSS.Core/Helpers/CacheKeys.cs src/GSS.Business/Services/MisService.cs tests/GSS.UnitTests/Business
git commit -m "fix: look up MIS roles by id in every division instead of division 1"
```

---

### Task 4: Extract the caller check into `ICallerValidator`

**Files:**
- Create: `src/GSS.Api/Authorization/ICallerValidator.cs`, `CallerValidator.cs`, `ValidatedCaller.cs`, `CallerValidationResult.cs`
- Modify: `src/GSS.Api/Authorization/AsmPermissionHandler.cs` (header parsing at 48-55 and `ValidateCallerAsync` at 91-146 move out)
- Modify: `src/GSS.Api/Extensions/Features/AuthenticationExtensions.cs:24` (register `AddScoped<ICallerValidator, CallerValidator>()`)
- Modify: `.docs/architecture/asm-authorization.md` ("How a Request Is Checked", step 3: name `CallerValidator` and the per-request reuse)
- Test: create `tests/GSS.UnitTests/API/Authorization/CallerValidatorTests.cs`; keep `AsmPermissionHandlerTests.cs` green, changing only its constructor setup

**Interfaces:**
- Produces:
  ```csharp
  public sealed record ValidatedCaller(string UserId, int PositionId, int RoleId, int DivisionId, IReadOnlyList<int> DepartmentIds);
  public sealed record CallerValidationResult(ValidatedCaller? Caller, string? FailureReason)
  {
      public bool Succeeded => Caller != null;
      public static CallerValidationResult Valid(ValidatedCaller caller);
      public static CallerValidationResult Invalid(string reason);
  }
  public interface ICallerValidator
  {
      /// Validates the x-app-* headers against the caller's MIS position; cached in HttpContext.Items for the request.
      Task<CallerValidationResult> ValidateAsync(HttpContext httpContext, CancellationToken cancellationToken);
  }
  ```
- The header-name constants stay on `AsmPermissionHandler`, which existing tests and docs reference. `CallerValidator` uses them.
- `AsmPermissionHandler` constructor becomes `(ICallerValidator callerValidator, IAsmService asmService, ILogger<AsmPermissionHandler> logger)`. It keeps the authentication/token check and the ASM step, and calls `Deny(...)` with `result.FailureReason` when validation fails.

- [ ] **Step 1: Write the failing tests** (`CallerValidatorTests`, `DefaultHttpContext` with an authenticated `ClaimsPrincipal` carrying `NameIdentifier` "42" and an `exp` claim)

```csharp
Validate_ValidHeaders_ReturnsCallerWithHeaderDivision      // headers 10/3/7/"11,12"; MIS position 10 role 3, depts 11,12 in division 7 → Caller.DivisionId == 7, DepartmentIds == [11,12]
Validate_MissingHeader_FailsWithoutCallingMis               // [Theory] each of the four headers absent, "0", "-1", "abc" → !Succeeded; misService.Verify(GetPersonPositionsAsync, Times.Never)
Validate_PositionNotHeld_Fails                              // FailureReason contains "Position 10"
Validate_RoleMismatch_Fails
Validate_DivisionNotOnPosition_Fails                        // header division 9, position departments all division 7 → FailureReason contains "Division 9"
Validate_DepartmentNotOnPosition_Fails
Validate_TwiceInOneRequest_CallsMisOnce                     // same HttpContext → GetPersonPositionsAsync Times.Once; second result is the same instance
```

Move the matching caller-check cases out of `AsmPermissionHandlerTests` instead of duplicating them. Keep one handler test that proves a failed validation denies with that reason.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/GSS.UnitTests --no-restore --filter "FullyQualifiedName~CallerValidatorTests|FullyQualifiedName~AsmPermissionHandlerTests"`
Expected: build failure (`CallerValidator` does not exist).

- [ ] **Step 3: Implement**

Move `TryGetIntHeader`, `TryGetIntListHeader` and `ValidateCallerAsync` into `CallerValidator`, with the same messages. It reads the user id from the `NameIdentifier` claim and the expiry via `JwtTokenHelper.GetTokenExpiration(httpContext.User)`. Cache the `CallerValidationResult` (success or failure) under a private `HttpContext.Items` key, so a second call in the same request returns it without touching MIS.

- [ ] **Step 4: Run them and see them pass**

Same command, then all unit plus architecture tests: `dotnet test tests/GSS.UnitTests --no-restore && dotnet test tests/GSS.ArchitectureTests --no-restore`. Expected: all PASS, including `ConstructorInjectionTests`.

- [ ] **Step 5: Commit**

```bash
git add src/GSS.Api/Authorization src/GSS.Api/Extensions/Features/AuthenticationExtensions.cs .docs/architecture/asm-authorization.md tests/GSS.UnitTests/API/Authorization
git commit -m "refactor: extract the x-app caller check into a request-cached ICallerValidator"
```

---

### Task 5: Scope MIS list endpoints to the caller's division

**Files:**
- Modify: `src/GSS.Api/Models/DivisionQuery.cs` (`int? DivisionId`; remove `DefaultDivisionId` and `ResolvedDivisionId`)
- Modify: `src/GSS.Api/Validators/DivisionQueryValidator.cs` (null or `> 0`; message `"Division ID must be greater than 0."`)
- Modify: `src/GSS.Api/Controllers/ApiControllerBase.cs` (add `ForbiddenResponse<T>` next to `UnauthorizedResponse<T>` at line 94)
- Modify: `src/GSS.Api/Controllers/MisController.cs` (constructor, `GetAllDepartments`, `GetAllRoleTypes`, `GetAllRoles`, XML docs, `[ProducesResponseType(..., Status403Forbidden)]`)
- Modify: `.docs/architecture/asm-authorization.md` (new "MIS Endpoints" subsection) and any `.docs` page describing `divisionId` defaults
- Test: `tests/GSS.UnitTests/API/Controllers/MisControllerTests.cs`, the `DivisionQueryValidator` tests, `tests/GSS.IntegrationTests/API/InputBoundaryIntegrationTests.cs`

**Interfaces:**
- Consumes: `ICallerValidator.ValidateAsync`, `CallerValidationResult`, `ValidatedCaller.DivisionId` (Task 4).
- Produces: `protected ObjectResult ForbiddenResponse<T>(string message, params string[] errorDetails)` returning status 403 with `Response<T>.Failure(message, BuildErrors(HttpStatusCode.Forbidden, ...))`.
- `MisController(IMisService misService, ICallerValidator callerValidator, IUserContext userContext, ILogger<MisController> logger)`.
- Private `Task<(int? DivisionId, string? Error)> ResolveDivisionAsync(int? requested, CancellationToken)`. It returns the header division when `requested` is null or equal to it. When validation fails it returns the failure reason, and for a different division it returns `"Division {requested} is not the caller's division"`.

- [ ] **Step 1: Write the failing tests**

`MisControllerTests` set `controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() }` and mock `ICallerValidator`:

```csharp
GetAllRoles_NoQuery_UsesCallerDivision                 // validator → caller division 7; misService.Verify(GetAllRolesAsync(7, ...)); never 1
GetAllRoles_QueryEqualsCallerDivision_ReturnsOk         // ?divisionId=7, caller 7 → 200
GetAllRoles_QueryOtherDivision_Returns403AndSkipsMis    // ?divisionId=2, caller 7 → ObjectResult 403; GetAllRolesAsync never called
GetAllRoles_CallerInvalid_Returns403AndSkipsMis         // validator Invalid("Division 9 not on position 10") → 403; ErrorId path unchanged
// same three behaviours for GetAllDepartments and GetAllRoleTypes ([Theory] over the action if it stays readable)
GetPersonPositions_NoHeaders_DoesNotValidateCaller      // callerValidator.Verify(ValidateAsync, Times.Never)
```

Validator tests: `DivisionId = null` is valid; `0` and `-1` are invalid with the new message.

Integration (`InputBoundaryIntegrationTests`):

```csharp
[Theory]
[InlineData("/api/v1/mis/departments")]
[InlineData("/api/v1/mis/roletypes")]
[InlineData("/api/v1/mis/roles")]
public async Task MissingCallerHeaders_Returns403(string url) // FakeSsoService token, no x-app-* → 403; body is a Response<ApiError> envelope
```

Keep `NegativeDivisionId_Returns400WithReason`: validation runs before the action, so it still returns 400.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/GSS.UnitTests --no-restore --filter "FullyQualifiedName~MisControllerTests|FullyQualifiedName~DivisionQueryValidator"`
Expected: build failure (constructor arity, `DivisionId` type).

- [ ] **Step 3: Implement**

Make the `DivisionQuery` / validator / `ForbiddenResponse` changes. In the three actions, replace `division.ResolvedDivisionId` with `ResolveDivisionAsync(division.DivisionId, cancellationToken)`. On an error, log a Warning with the reason and return `ForbiddenResponse<IReadOnlyList<…>>("Access denied", error)`. Delete every `DefaultDivisionId` reference (`grep -rn DefaultDivisionId src tests` returns nothing).

- [ ] **Step 4: Run them and see them pass**

Same command. Expected: all PASS. Then run the integration tests if PostgreSQL is up: `dotnet test tests/GSS.IntegrationTests --no-restore --filter "FullyQualifiedName~InputBoundaryIntegrationTests"`.

- [ ] **Step 5: Run the required checks**

```bash
dotnet format GSS.slnx --no-restore --verify-no-changes
dotnet build GSS.slnx --no-restore
dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj --no-restore
dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj --no-restore
pwsh scripts/verify-scaffold.ps1
grep -rnE "GetAllRolesAsync\(1|DefaultDivisionId|division-1" src   # expect no output
```

Expected: every command exits 0, and the grep prints nothing.

- [ ] **Step 6: Commit**

```bash
git add src/GSS.Api tests/GSS.UnitTests/API tests/GSS.IntegrationTests/API .docs
git commit -m "feat!: scope MIS division endpoints to the caller's validated x-app-division-id"
```

---

### Task 6 (optional): `GET api/v1/mis/divisions`

**Files:**
- Modify: `src/GSS.Util/Models/MisServiceOptions.cs` (add `public Dictionary<int, string> Divisions { get; set; } = [];`), `src/GSS.Api/appsettings.json` (`MisService:Divisions` with asm-api's six ids/names)
- Create: `src/GSS.Api/Models/DivisionResponse.cs` (`sealed record` with `required int DivisionId`, `required string DivisionName`)
- Modify: `src/GSS.Api/Controllers/MisController.cs` (add `IOptionsMonitor<MisServiceOptions>`)
- Test: `MisControllerTests.GetDivisions_ReturnsConfiguredDivisionsOrderedById`; `DtoShapeTests` covers the record if it scans Api models

**Interfaces:** `[HttpGet("divisions")] ActionResult<Response<IReadOnlyList<DivisionResponse>>> GetDivisions()` returns the configured divisions ordered by id. It needs authentication but not the `x-app-*` headers, because a UI calls it before choosing a position. With no divisions configured it returns 200 with an empty list.

Steps follow Task 5's cycle: write the failing test, run it, implement, pass, commit `feat: list configured MIS divisions`.
