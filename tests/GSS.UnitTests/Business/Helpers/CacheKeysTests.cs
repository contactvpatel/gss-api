using FluentAssertions;
using GSS.Core.Helpers;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.Business.Helpers;

/// <summary>
/// Unit tests for the token-keyed CacheKeys builders.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class CacheKeysTests
{
    private const string Token = "header.payload.signature";

    [Fact]
    public void TokenKeys_NeverContainTheRawToken()
    {
        CacheKeys.TokenValidation(Token).Should().NotContain(Token);
        CacheKeys.AsmSecurity(Token).Should().NotContain(Token);
    }

    [Fact]
    public void TokenKeys_AreStablePerTokenAndDistinctAcrossTokensAndKinds()
    {
        CacheKeys.TokenValidation(Token).Should().Be(CacheKeys.TokenValidation(Token));
        CacheKeys.TokenValidation(Token).Should().NotBe(CacheKeys.TokenValidation(Token + "x"));
        CacheKeys.TokenValidation(Token).Should().NotBe(CacheKeys.AsmSecurity(Token));
    }

    [Fact]
    public void TokenKeys_UsePrefixAndLowerCaseSha256Hex()
    {
        CacheKeys.TokenValidation(Token).Should().MatchRegex("^token-validation-[0-9a-f]{64}$");
        CacheKeys.AsmSecurity(Token).Should().MatchRegex("^asm-security-[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void TokenKeys_BlankToken_Throws(string token)
    {
        FluentActions.Invoking(() => CacheKeys.TokenValidation(token)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => CacheKeys.AsmSecurity(token)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RoleById_FormatsKey() => CacheKeys.RoleById(42).Should().Be("role-42");

    [Fact]
    public void RoleById_RejectsZero() =>
        FluentActions.Invoking(() => CacheKeys.RoleById(0)).Should().Throw<ArgumentOutOfRangeException>();
}
