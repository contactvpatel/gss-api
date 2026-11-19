using GSS.Business.DTOs;
using GSS.Business.Services.Interfaces;
using GSS.Core.Exceptions;
using GSS.Util.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>Fake SSO: accepts only <see cref="ValidToken"/>, so the real auth filter runs without external calls.</summary>
public sealed class FakeSsoService : ISsoService
{
    /// <summary>pid claim of test tokens.</summary>
    public const string TestUserId = "900001";

    /// <summary>Token SSO accepts.</summary>
    public static readonly string ValidToken = CreateToken("valid");

    /// <summary>Well-formed token SSO rejects.</summary>
    public static readonly string RejectedToken = CreateToken("rejected");

    /// <summary>Token whose exp has passed; authentication rejects it before calling SSO.</summary>
    public static readonly string ExpiredToken = CreateToken("expired", DateTime.UtcNow.AddHours(-1));

    /// <summary>Token whose validation finds SSO unavailable (circuit open).</summary>
    public static readonly string OutageToken = CreateToken("outage");

    /// <summary>Refresh token SSO accepts on renew, with any well-formed access token (expired included).</summary>
    public const string ValidRefreshToken = "valid-refresh-token";

    /// <summary>Token whose validation hits an internal error (a bug, not a verdict on the token).</summary>
    public static readonly string ErrorToken = CreateToken("error");

    /// <inheritdoc />
    public Task<bool> ValidateTokenAsync(string token, DateTimeOffset? tokenExpiresAtUtc, CancellationToken cancellationToken = default)
    {
        if (string.Equals(token, OutageToken, StringComparison.Ordinal))
        {
            throw new ExternalServiceException("SsoService", ExternalServiceFailure.Unavailable, "SSO circuit open.");
        }

        if (string.Equals(token, ErrorToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Simulated validation bug.");
        }

        return Task.FromResult(string.Equals(token, ValidToken, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public Task<SsoAuthResponse?> RenewTokenAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        var renewed = string.Equals(refreshToken, ValidRefreshToken, StringComparison.Ordinal)
            ? new SsoAuthResponse { AccessToken = ValidToken, RefreshToken = ValidRefreshToken }
            : null;
        return Task.FromResult(renewed);
    }

    /// <inheritdoc />
    public Task<bool> LogoutAsync(string token, string? userId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    private static string CreateToken(string tokenId, DateTime? expiresUtc = null)
    {
        var expires = expiresUtc ?? DateTime.UtcNow.AddHours(1);
        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = "integration-tests",
            Audience = "gss-api",
            Claims = new Dictionary<string, object>
            {
                [JwtTokenHelper.ClaimTypeUserId] = TestUserId,
                [JwtRegisteredClaimNames.Jti] = tokenId
            },
            NotBefore = expires.AddHours(-2),
            Expires = expires
        };

        // Unsigned header.payload. plus a fake signature: JWS-shaped like SSO's tokens, which the API cannot verify.
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor) + "test-signature";
    }
}
