using System.Globalization;
using System.Security.Claims;
using GSS.Business.DTOs;
using GSS.Business.Services.Interfaces;
using GSS.Util.Security;

namespace GSS.Api.Authorization;

/// <inheritdoc />
public sealed class CallerValidator(IMisService misService) : ICallerValidator
{
    private const string ItemsKey = "GSS.CallerValidation";

    /// <inheritdoc />
    public async Task<CallerValidationResult> ValidateAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        string? userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (httpContext.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            return CallerValidationResult.Invalid("Caller is not authenticated");
        }

        // A result cached for another user (HttpContext.User replaced mid-request) is ignored and re-validated.
        if (httpContext.Items[ItemsKey] is CachedValidation cached && string.Equals(cached.UserId, userId, StringComparison.Ordinal))
        {
            return cached.Result;
        }

        var result = await ValidateCoreAsync(httpContext, userId, cancellationToken).ConfigureAwait(false);
        httpContext.Items[ItemsKey] = new CachedValidation(userId, result);
        return result;
    }

    private async Task<CallerValidationResult> ValidateCoreAsync(HttpContext httpContext, string userId, CancellationToken cancellationToken)
    {
        if (!TryGetIntHeader(httpContext, AsmPermissionHandler.PositionIdHeader, out int positionId)
            || !TryGetIntHeader(httpContext, AsmPermissionHandler.RoleIdHeader, out int roleId)
            || !TryGetIntHeader(httpContext, AsmPermissionHandler.DivisionIdHeader, out int divisionId)
            || !TryGetIntListHeader(httpContext, AsmPermissionHandler.DepartmentIdHeader, out var departmentIds))
        {
            return CallerValidationResult.Invalid("Missing or invalid x-app-* headers");
        }

        // exp from the principal's claims: the token is not parsed again.
        var tokenExpiresAtUtc = JwtTokenHelper.GetTokenExpiration(httpContext.User);
        var positions = await misService.GetPersonPositionsAsync(userId, tokenExpiresAtUtc, cancellationToken).ConfigureAwait(false);
        var position = positions.FirstOrDefault(p => p.PositionId == positionId);
        if (position == null)
        {
            return CallerValidationResult.Invalid($"Position {positionId} not held by caller");
        }

        if (position.RoleId != roleId)
        {
            return CallerValidationResult.Invalid($"Role {roleId} not held on position {positionId}");
        }

        if (position.Department.All(d => d.DivisionId != divisionId))
        {
            return CallerValidationResult.Invalid($"Division {divisionId} not on position {positionId}");
        }

        HashSet<int> owned = [.. position.Department.Select(d => d.DepartmentId)];
        return departmentIds.All(owned.Contains)
            ? CallerValidationResult.Valid(new ValidatedCaller(userId, positionId, roleId, divisionId, departmentIds))
            : CallerValidationResult.Invalid($"Department not on position {positionId}");
    }

    private static bool TryGetIntListHeader(HttpContext httpContext, string name, out IReadOnlyList<int> values)
    {
        string[] parts = httpContext.Request.Headers[name].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<int> parsed = new(parts.Length);
        foreach (string part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value <= 0)
            {
                values = [];
                return false;
            }

            parsed.Add(value);
        }

        values = parsed;
        return parsed.Count > 0;
    }

    private static bool TryGetIntHeader(HttpContext httpContext, string name, out int value)
    {
        return int.TryParse(httpContext.Request.Headers[name].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value > 0;
    }

    /// <summary>
    /// A validation result and the user it was computed for (failures carry no <see cref="ValidatedCaller"/>).
    /// </summary>
    private sealed record CachedValidation(string UserId, CallerValidationResult Result);
}
