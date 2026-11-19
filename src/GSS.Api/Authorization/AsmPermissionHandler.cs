using System.Security.Claims;
using GSS.Business.DTOs;
using GSS.Business.Services.Interfaces;
using GSS.Util.Security;
using Microsoft.AspNetCore.Authorization;

namespace GSS.Api.Authorization;

/// <summary>
/// Checks <see cref="AsmPermissionRequirement"/> against the one position the caller selects with x-app-* headers.
/// The headers are validated against MIS; permissions of other positions never count.
/// A singleton: the request's <see cref="ICallerValidator"/> and <see cref="IAsmService"/> are resolved only when an
/// [AsmAuthorize] requirement is checked, so other requests (fallback policy only) never build the MIS/ASM clients.
/// </summary>
public sealed class AsmPermissionHandler(ILogger<AsmPermissionHandler> logger) : AuthorizationHandler<AsmPermissionRequirement>
{
    /// <summary>Header selecting the caller's position.</summary>
    public const string PositionIdHeader = "x-app-position-id";

    /// <summary>Header selecting the caller's role on that position.</summary>
    public const string RoleIdHeader = "x-app-role-id";

    /// <summary>Header naming the caller's division (one id).</summary>
    public const string DivisionIdHeader = "x-app-division-id";

    /// <summary>Header naming the caller's departments (comma-separated ids).</summary>
    public const string DepartmentIdHeader = "x-app-department-id";

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AsmPermissionRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext)
        {
            Deny(context, requirement, "No HTTP context");
            return;
        }

        string? userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        string? token = httpContext.Items[HttpContextItemKeys.UserToken] as string;
        if (context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
        {
            Deny(context, requirement, "Caller is not authenticated");
            return;
        }

        var callerValidator = httpContext.RequestServices.GetRequiredService<ICallerValidator>();
        var validation = await callerValidator.ValidateAsync(httpContext, httpContext.RequestAborted);
        if (!validation.Succeeded)
        {
            Deny(context, requirement, validation.FailureReason!);
            return;
        }

        var caller = validation.Caller!;
        int positionId = caller.PositionId;
        int roleId = caller.RoleId;
        var tokenExpiresAtUtc = JwtTokenHelper.GetTokenExpiration(context.User);

        // Cached per token by AsmService; ASM returns only the caller's own positions, so a match is the caller's.
        var asmService = httpContext.RequestServices.GetRequiredService<IAsmService>();
        var positions = await asmService.GetApplicationSecurityAsync(token, tokenExpiresAtUtc, httpContext.RequestAborted);
        var selected = positions.FirstOrDefault(p => p.PositionId == positionId && p.RoleId == roleId);
        if (selected == null)
        {
            Deny(context, requirement, $"Position {positionId} / role {roleId} not assigned to caller");
            return;
        }

        if (requirement.AnyOf.Any(permission => HasAccess(selected.ApplicationAccess, permission)))
        {
            context.Succeed(requirement);
            return;
        }

        Deny(context, requirement, "Insufficient permissions");
    }

    private void Deny(AuthorizationHandlerContext context, AsmPermissionRequirement requirement, string reason)
    {
        logger.LogWarning("ASM authorization denied: {Reason}. Required: {Requirement}", reason, requirement);
        context.Fail(new AuthorizationFailureReason(this, reason));
    }

    private static bool HasAccess(IReadOnlyList<ApplicationAccessDto> accesses, AsmPermission permission)
    {
        if (permission.Access == AccessType.AllowAny)
        {
            return accesses.Count > 0;
        }

        string moduleCode = permission.Module.ToAsmCode();
        return accesses.Any(a => string.Equals(a.ModuleCode, moduleCode, StringComparison.Ordinal) && permission.Access switch
        {
            AccessType.View => a.HasViewAccess == true,
            AccessType.Create => a.HasCreateAccess == true,
            AccessType.Update => a.HasUpdateAccess == true,
            AccessType.Delete => a.HasDeleteAccess == true,
            AccessType.Access => a.HasAccess == true,
            _ => false
        });
    }
}
