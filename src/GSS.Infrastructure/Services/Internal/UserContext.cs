using System.Security.Claims;
using GSS.Core.Interfaces.Base;
using GSS.Util.Security;
using Microsoft.AspNetCore.Http;

namespace GSS.Infrastructure.Services.Internal;

/// <summary>
/// <see cref="IUserContext"/> from the current HTTP context.
/// </summary>
public class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    /// <inheritdoc />
    /// <remarks>The authenticated principal's NameIdentifier claim (set from <c>pid</c> by the SSO handler).</remarks>
    public string? GetUserId()
    {
        var user = httpContextAccessor.HttpContext?.User;
        string? userId = user?.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
        return string.IsNullOrWhiteSpace(userId) ? null : userId;
    }

    /// <inheritdoc />
    public DateTimeOffset? GetTokenExpiresAtUtc()
    {
        var user = httpContextAccessor.HttpContext?.User;
        return user?.Identity?.IsAuthenticated == true ? JwtTokenHelper.GetTokenExpiration(user) : null;
    }

    /// <inheritdoc />
    public string? GetToken()
    {
        return httpContextAccessor.HttpContext?.Items[HttpContextItemKeys.UserToken] as string;
    }
}
