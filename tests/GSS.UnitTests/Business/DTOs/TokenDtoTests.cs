using FluentAssertions;
using GSS.Business.DTOs;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.Business.DTOs;

/// <summary>Token-carrying DTOs never print their tokens (a record's ToString lists every property).</summary>
[Trait("Category", TestCategories.Unit)]
public class TokenDtoTests
{
    [Fact]
    public void SsoAuthResponse_ToString_HidesTokens()
    {
        SsoAuthResponse response = new() { AccessToken = "secret-access", RefreshToken = "secret-refresh", AuthenticationInfo = "public-info" };

        string? text = response.ToString();

        text.Should().NotContain("secret-access").And.NotContain("secret-refresh");
        text.Should().Contain("public-info", because: "non-secret members are still printed");
    }

    [Fact]
    public void SsoRenewTokenRequest_ToString_HidesTheRefreshToken()
    {
        SsoRenewTokenRequest request = new() { RefreshToken = "secret-refresh" };

        request.ToString().Should().NotContain("secret-refresh");
    }
}
