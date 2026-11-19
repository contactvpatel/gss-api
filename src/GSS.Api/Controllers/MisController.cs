using Asp.Versioning;
using GSS.Api.Authorization;
using GSS.Api.Models;
using GSS.Business.DTOs;
using GSS.Business.Services.Interfaces;
using GSS.Core.Interfaces.Base;
using GSS.Util.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GSS.Api.Controllers;

/// <summary>
/// Management Information System (MIS) endpoints:
/// departments, roles, role types and positions.
/// Every endpoint that returns division data needs the x-app-* caller headers and is scoped to the caller's
/// validated division (another division's ID is a 404, like a missing one); only <c>divisions</c> and
/// <c>person-positions</c> are header-free.
/// </summary>
[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/mis")]
[Produces("application/json")]
public class MisController(IMisService misService, ICallerValidator callerValidator, IUserContext userContext, ILogger<MisController> logger, IOptionsMonitor<MisServiceOptions> misOptions) : ApiControllerBase
{
    /// <summary>
    /// Lists the configured divisions. Needs authentication only, no x-app-* headers, so a client can call it before selecting a position.
    /// </summary>
    /// <returns>Configured divisions ordered by ID; an empty list when none are configured.</returns>
    [HttpGet("divisions")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<DivisionResponse>>), StatusCodes.Status200OK)]
    public ActionResult<Response<IReadOnlyList<DivisionResponse>>> GetDivisions()
    {
        IReadOnlyList<DivisionResponse> divisions = (misOptions.CurrentValue.Divisions ?? [])
            .OrderBy(d => d.Key)
            .Select(d => new DivisionResponse { DivisionId = d.Key, DivisionName = d.Value })
            .ToList();

        return OkResponse(divisions, "Divisions retrieved successfully");
    }

    /// <summary>
    /// Gets all departments for the caller's division.
    /// </summary>
    /// <returns>Departments (an empty list when none), or 403 if the caller or division is not allowed.</returns>
    [RequiresCallerHeaders]
    [HttpGet("departments")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<DepartmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<IReadOnlyList<DepartmentDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Response<IReadOnlyList<DepartmentDto>>>> GetAllDepartments(
        CancellationToken cancellationToken)
    {
        var caller = await ValidateCallerAsync(cancellationToken);

        if (caller is null)
        {
            return AccessDenied<IReadOnlyList<DepartmentDto>>();
        }

        int divisionId = caller.DivisionId;

        var departments = await misService.GetAllDepartmentsAsync(divisionId, cancellationToken);

        string message = $"Found {departments.Count} departments for division ID: {divisionId}";
        return OkResponse(departments, message);
    }

    /// <summary>
    /// Gets a department in the caller's division by identifier.
    /// </summary>
    /// <param name="departmentId">Department identifier.</param>
    /// <returns>Department, 403 if the caller is not allowed, or 404 if it does not exist or is in another division.</returns>
    [RequiresCallerHeaders]
    [HttpGet("departments/{departmentId:int:min(1)}")]
    [ProducesResponseType(typeof(Response<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<DepartmentDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<DepartmentDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Response<DepartmentDto>>> GetDepartmentById(
        [FromRoute] int departmentId,
        CancellationToken cancellationToken)
    {
        var caller = await ValidateCallerAsync(cancellationToken);

        if (caller is null)
        {
            return AccessDenied<DepartmentDto>();
        }

        var department = await misService.GetDepartmentByIdAsync(departmentId, cancellationToken);

        if (department is null || !IsCallerDivision(caller, department.DivisionId, "Department", departmentId))
        {
            return HandleNotFound<DepartmentDto>("Department", "ID", departmentId);
        }

        return OkResponse(department, "Department retrieved successfully");
    }

    /// <summary>
    /// Gets all role types for the caller's division.
    /// </summary>
    /// <returns>Role types (an empty list when none), or 403 if the caller or division is not allowed.</returns>
    [RequiresCallerHeaders]
    [HttpGet("roletypes")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<RoleTypeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<IReadOnlyList<RoleTypeDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Response<IReadOnlyList<RoleTypeDto>>>> GetAllRoleTypes(
        CancellationToken cancellationToken)
    {
        var caller = await ValidateCallerAsync(cancellationToken);

        if (caller is null)
        {
            return AccessDenied<IReadOnlyList<RoleTypeDto>>();
        }

        int divisionId = caller.DivisionId;

        var roleTypes = await misService.GetAllRoleTypesAsync(divisionId, cancellationToken);

        string message = $"Found {roleTypes.Count} role types for division ID: {divisionId}";
        return OkResponse(roleTypes, message);
    }

    /// <summary>
    /// Gets all roles for the caller's division.
    /// </summary>
    /// <returns>Roles (an empty list when none), or 403 if the caller or division is not allowed.</returns>
    [RequiresCallerHeaders]
    [HttpGet("roles")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<RoleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<IReadOnlyList<RoleDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Response<IReadOnlyList<RoleDto>>>> GetAllRoles(
        CancellationToken cancellationToken)
    {
        var caller = await ValidateCallerAsync(cancellationToken);

        if (caller is null)
        {
            return AccessDenied<IReadOnlyList<RoleDto>>();
        }

        int divisionId = caller.DivisionId;

        var roles = await misService.GetAllRolesAsync(divisionId, cancellationToken);

        string message = $"Found {roles.Count} roles for division ID: {divisionId}";
        return OkResponse(roles, message);
    }

    /// <summary>
    /// Gets a role in the caller's division by identifier.
    /// </summary>
    /// <param name="id">Role ID.</param>
    /// <returns>Role, 403 if the caller is not allowed, or 404 if it does not exist or is in another division.</returns>
    [RequiresCallerHeaders]
    [HttpGet("roles/{id:int:min(1)}")]
    [ProducesResponseType(typeof(Response<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<RoleDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<RoleDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Response<RoleDto>>> GetRoleById(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var caller = await ValidateCallerAsync(cancellationToken);

        if (caller is null)
        {
            return AccessDenied<RoleDto>();
        }

        var role = await misService.GetRoleByIdAsync(id, cancellationToken);

        if (role is null || !IsCallerDivision(caller, role.DivisionId, "Role", id))
        {
            return HandleNotFound<RoleDto>("Role", "ID", id);
        }

        return OkResponse(role, "Role retrieved successfully");
    }

    /// <summary>
    /// Gets all roles for a department in the caller's division.
    /// </summary>
    /// <param name="departmentId">Department identifier.</param>
    /// <returns>Roles (an empty list when none), 403 if the caller is not allowed, or 404 if the department does not exist or is in another division.</returns>
    [RequiresCallerHeaders]
    [HttpGet("roles/departments/{departmentId:int:min(1)}")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<RoleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<IReadOnlyList<RoleDto>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<IReadOnlyList<RoleDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Response<IReadOnlyList<RoleDto>>>> GetRolesByDepartmentId(
        [FromRoute] int departmentId,
        CancellationToken cancellationToken)
    {
        var caller = await ValidateCallerAsync(cancellationToken);

        if (caller is null)
        {
            return AccessDenied<IReadOnlyList<RoleDto>>();
        }

        var department = await misService.GetDepartmentByIdAsync(departmentId, cancellationToken);

        if (department is null || !IsCallerDivision(caller, department.DivisionId, "Department", departmentId))
        {
            return HandleNotFound<IReadOnlyList<RoleDto>>("Department", "ID", departmentId);
        }

        var roles = await misService.GetRolesByDepartmentIdAsync(departmentId, cancellationToken);

        string message = $"Found {roles.Count} roles for department ID: {departmentId}";
        return OkResponse(roles, message);
    }

    /// <summary>
    /// Gets all positions for a role in the caller's division.
    /// </summary>
    /// <param name="roleId">Role identifier.</param>
    /// <returns>Positions (an empty list when none), 403 if the caller is not allowed, or 404 if the role does not exist or is in another division.</returns>
    [RequiresCallerHeaders]
    [HttpGet("positions/roles/{roleId:int:min(1)}")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<PositionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<IReadOnlyList<PositionDto>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<IReadOnlyList<PositionDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Response<IReadOnlyList<PositionDto>>>> GetPositionsByRoleId(
        [FromRoute] int roleId,
        CancellationToken cancellationToken)
    {
        var caller = await ValidateCallerAsync(cancellationToken);

        if (caller is null)
        {
            return AccessDenied<IReadOnlyList<PositionDto>>();
        }

        var role = await misService.GetRoleByIdAsync(roleId, cancellationToken);

        if (role is null || !IsCallerDivision(caller, role.DivisionId, "Role", roleId))
        {
            return HandleNotFound<IReadOnlyList<PositionDto>>("Role", "ID", roleId);
        }

        var positions = await misService.GetPositionsByRoleIdAsync(roleId, cancellationToken);

        string message = $"Found {positions.Count} positions for role ID: {roleId}";
        return OkResponse(positions, message);
    }

    /// <summary>
    /// Gets all positions for the current authenticated user.
    /// </summary>
    /// <returns>The caller's positions (an empty list when none).</returns>
    [HttpGet("person-positions")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<PersonPositionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Response<IReadOnlyList<PersonPositionDto>>>> GetPersonPositions(
        CancellationToken cancellationToken)
    {
        string? userId = userContext.GetUserId();

        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("User ID not available in context");
            return UnauthorizedResponse<IReadOnlyList<PersonPositionDto>>("User ID not available", "User ID not available");
        }

        var positions = await misService.GetPersonPositionsAsync(userId, userContext.GetTokenExpiresAtUtc(), cancellationToken);

        return OkResponse(positions, "Person positions retrieved successfully");
    }

    /// <summary>
    /// Validates the caller's x-app-* headers (request-cached by <see cref="ICallerValidator"/>).
    /// Returns null, after logging the reason, when the caller is not allowed.
    /// </summary>
    private async Task<ValidatedCaller?> ValidateCallerAsync(CancellationToken cancellationToken)
    {
        var validation = await callerValidator.ValidateAsync(HttpContext, cancellationToken);

        if (validation.Succeeded && validation.Caller is not null)
        {
            return validation.Caller;
        }

        LogAccessDenied(validation.FailureReason ?? "Caller validation failed");
        return null;
    }

    /// <summary>
    /// False for another division's entity, which callers answer with the same 404 as a missing ID, so IDs in other
    /// divisions cannot be probed. The reason is logged, never returned.
    /// </summary>
    private bool IsCallerDivision(ValidatedCaller caller, int divisionId, string entityName, int entityId)
    {
        if (divisionId == caller.DivisionId)
        {
            return true;
        }

        logger.LogWarning(
            "MIS {Entity} {EntityId} is in division {DivisionId}, not the caller's division {CallerDivisionId}; answered 404",
            entityName, entityId, divisionId, caller.DivisionId);
        return false;
    }

    private void LogAccessDenied(string reason)
    {
        logger.LogWarning("MIS division access denied: {Reason}", reason);
    }

    /// <summary>
    /// 403 with generic text only: the reason is logged, never returned.
    /// </summary>
    private ObjectResult AccessDenied<T>()
    {
        return ForbiddenResponse<T>("Access denied");
    }
}
