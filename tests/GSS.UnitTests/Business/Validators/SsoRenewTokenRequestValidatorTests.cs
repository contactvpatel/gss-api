using FluentValidation.TestHelper;
using GSS.Business.DTOs;
using GSS.Business.Validators;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.Business.Validators;

/// <summary>
/// Unit tests for SsoRenewTokenRequestValidator.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class SsoRenewTokenRequestValidatorTests
{
    private readonly SsoRenewTokenRequestValidator _validator = new();

    [Fact]
    public void ValidRefreshToken_ShouldNotHaveValidationError()
    {
        SsoRenewTokenRequest request = new() { RefreshToken = "refresh-token" };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MissingRefreshToken_ShouldHaveExactlyOneError(string? refreshToken)
    {
        SsoRenewTokenRequest request = new() { RefreshToken = refreshToken! };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(r => r.RefreshToken)
            .WithErrorMessage(SsoRenewTokenRequestValidator.RequiredMessage)
            .Only();
    }

}
