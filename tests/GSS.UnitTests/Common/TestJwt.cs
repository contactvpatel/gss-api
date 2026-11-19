using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GSS.UnitTests.Common;

/// <summary>
/// Test JWTs built with <see cref="JsonWebTokenHandler"/>: unsigned, and no exp/nbf/iat unless given.
/// </summary>
public static class TestJwt
{
    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    /// <summary>Unsigned token (<c>header.payload.</c>).</summary>
    public static string Create(
        IDictionary<string, object>? claims = null,
        DateTime? expires = null,
        DateTime? notBefore = null,
        string? issuer = null,
        string? audience = null)
    {
        return Handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            Expires = expires,
            NotBefore = notBefore,
            Issuer = issuer,
            Audience = audience
        });
    }

    /// <summary>JWS-shaped token with a fake signature, like SSO's v1 tokens: readable, not verifiable.</summary>
    public static string CreateWithFakeSignature(
        IDictionary<string, object>? claims = null,
        DateTime? expires = null,
        DateTime? notBefore = null,
        string? issuer = null,
        string? audience = null)
    {
        return Create(claims, expires, notBefore, issuer, audience) + "test-signature";
    }
}
