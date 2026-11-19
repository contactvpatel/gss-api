using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GSS.Util.Security;

/// <summary>
/// JWT parsing and claim reading with <see cref="JsonWebTokenHandler"/>. Does not validate signatures.
/// Token cache keys are in <c>CacheKeys</c>.
/// </summary>
public static class JwtTokenHelper
{
    /// <summary>Reads tokens only; thread-safe.</summary>
    private static readonly JsonWebTokenHandler TokenHandler = new();

    /// <summary>User ID (person ID) claim.</summary>
    public const string ClaimTypeUserId = "pid";

    /// <summary>Expiry claim (Unix seconds).</summary>
    public const string ClaimTypeExpiration = "exp";

    /// <summary>Minimum seconds left for a token to be worth caching.</summary>
    public const double MinimumCacheTimeSeconds = 1.0;

    /// <summary>Time left until <paramref name="expiresAtUtc"/>, or null when unknown or at most <see cref="MinimumCacheTimeSeconds"/>.</summary>
    public static TimeSpan? GetRemainingLifetime(DateTimeOffset? expiresAtUtc, DateTimeOffset utcNow)
    {
        if (!expiresAtUtc.HasValue)
        {
            return null;
        }

        var remaining = expiresAtUtc.Value - utcNow;
        return remaining.TotalSeconds > MinimumCacheTimeSeconds ? remaining : null;
    }

    /// <summary>Parses the token, or returns null if it is not a JWT.</summary>
    public static JsonWebToken? ParseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            return TokenHandler.CanReadToken(token) ? TokenHandler.ReadJsonWebToken(token) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>First claim of the type (case-insensitive), or null.</summary>
    public static string? GetClaimValue(JsonWebToken? token, string claimType)
    {
        if (token == null || string.IsNullOrWhiteSpace(claimType))
        {
            return null;
        }

        return token.Claims.FirstOrDefault(c => c.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    /// <summary>The <c>pid</c> claim, or null.</summary>
    public static string? GetUserId(JsonWebToken? token)
    {
        return GetClaimValue(token, ClaimTypeUserId);
    }

    /// <summary>The <c>exp</c> claim in UTC, or null.</summary>
    public static DateTimeOffset? GetTokenExpiration(JsonWebToken? token)
    {
        return ParseExpiration(GetClaimValue(token, ClaimTypeExpiration));
    }

    /// <summary>The <c>exp</c> claim of an authenticated principal, in UTC.</summary>
    public static DateTimeOffset? GetTokenExpiration(ClaimsPrincipal? principal)
    {
        return ParseExpiration(principal?.FindFirst(ClaimTypeExpiration)?.Value);
    }

    private static DateTimeOffset? ParseExpiration(string? expClaim)
    {
        if (string.IsNullOrWhiteSpace(expClaim) || !long.TryParse(expClaim, out long expSeconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(expSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Token from a <c>Bearer {token}</c> header value, or null.</summary>
    public static string? ExtractBearerToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return null;
        }

        const string bearerPrefix = "Bearer ";
        if (!authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string token = authorizationHeader.Substring(bearerPrefix.Length).Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
