using System.Security.Claims;
using FluentAssertions;
using GSS.UnitTests.Common;
using GSS.Util.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Security;

/// <summary>
/// Unit tests for JwtTokenHelper - additional branch coverage.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class JwtTokenHelperTests
{
    #region GetClaimValue Tests

    [Fact]
    public void GetClaimValue_EmptyClaimType_ReturnsNull()
    {
        // Arrange
        var token = CreateJwtTokenWithClaims(("test", "value"));

        // Act
        string? result = JwtTokenHelper.GetClaimValue(token, "");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetClaimValue_WhitespaceClaimType_ReturnsNull()
    {
        // Arrange
        var token = CreateJwtTokenWithClaims(("test", "value"));

        // Act
        string? result = JwtTokenHelper.GetClaimValue(token, "   ");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetTokenExpiration Tests

    [Fact]
    public void GetTokenExpiration_ReturnsUtc()
    {
        var token = CreateTokenExpiringAt(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = JwtTokenHelper.GetTokenExpiration(token);

        result.Should().Be(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        result!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetTokenExpiration_FromPrincipal_ReadsExpClaim()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim(JwtTokenHelper.ClaimTypeExpiration, "1735689600")], "test"));

        JwtTokenHelper.GetTokenExpiration(principal).Should().Be(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetRemainingLifetime_ShortOrMissing_ReturnsNull()
    {
        DateTimeOffset now = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        JwtTokenHelper.GetRemainingLifetime(now.AddMinutes(5), now).Should().Be(TimeSpan.FromMinutes(5));
        JwtTokenHelper.GetRemainingLifetime(now.AddMilliseconds(500), now).Should().BeNull();
        JwtTokenHelper.GetRemainingLifetime(null, now).Should().BeNull();
    }

    #endregion

    #region ExtractBearerToken Tests

    [Fact]
    public void ExtractBearerToken_BearerWithWhitespaceOnly_ReturnsNull()
    {
        // Arrange
        const string authorizationHeader = "Bearer   ";

        // Act
        string? result = JwtTokenHelper.ExtractBearerToken(authorizationHeader);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractBearerToken_EmptyString_ReturnsNull()
    {
        // Act
        string? result = JwtTokenHelper.ExtractBearerToken(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractBearerToken_WhitespaceOnly_ReturnsNull()
    {
        // Act
        string? result = JwtTokenHelper.ExtractBearerToken("   ");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Helper Methods

    private static JsonWebToken CreateTokenExpiringAt(DateTime expiresUtc)
    {
        return new JsonWebToken(TestJwt.Create(expires: expiresUtc, notBefore: expiresUtc.AddHours(-1), issuer: "test", audience: "test"));
    }

    private static JsonWebToken CreateJwtTokenWithClaims(params (string Type, object Value)[] claims)
    {
        var all = claims.ToDictionary(c => c.Type, c => c.Value);
        all["sub"] = "user123";

        return new JsonWebToken(TestJwt.Create(all, issuer: "test", audience: "test"));
    }

    #endregion
}
