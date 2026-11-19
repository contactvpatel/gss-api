using FluentAssertions;
using GSS.Api.Controllers;
using GSS.Api.Models;
using GSS.Business.DTOs;
using GSS.Core.Interfaces.Base;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using ISsoService = GSS.Business.Services.Interfaces.ISsoService;

namespace GSS.UnitTests.API.Controllers;

/// <summary>
/// Unit tests for SsoController.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class SsoControllerTests
{
    private readonly Mock<ISsoService> _mockSsoService = new();
    private readonly Mock<IUserContext> _mockUserContext = new();
    private readonly SsoController _controller;

    public SsoControllerTests()
    {
        _controller = new SsoController(_mockSsoService.Object, _mockUserContext.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    /// <summary>Renew reads the access token from the Authorization header, not from authentication.</summary>
    private void SetBearer(string? token)
    {
        _controller.HttpContext.Request.Headers.Authorization = token is null ? default : $"Bearer {token}";
    }

    private static string AccessToken(DateTime? expires = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        return TestJwt.CreateWithFakeSignature(new Dictionary<string, object> { ["pid"] = "42" }, expires: expiry, notBefore: expiry.AddHours(-2));
    }

    #region RenewToken Tests

    [Fact]
    public async Task RenewToken_ValidTokens_ReturnsOk()
    {
        // Arrange
        string accessToken = AccessToken();
        const string refreshToken = "valid-refresh-token";
        SsoRenewTokenRequest request = new SsoRenewTokenRequest { RefreshToken = refreshToken };
        SsoAuthResponse authResponse = new SsoAuthResponse
        {
            AccessToken = "new-access-token",
            RefreshToken = "new-refresh-token"
        };

        SetBearer(accessToken);
        _mockSsoService
            .Setup(s => s.RenewTokenAsync(accessToken, refreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResponse);

        // Act
        var result = await _controller.RenewToken(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<SsoAuthResponse>? response = okResult!.Value as Response<SsoAuthResponse>;
        response!.Data.Should().NotBeNull();
        response.Data!.AccessToken.Should().Be("new-access-token");
        response.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RenewToken_MissingAccessToken_ReturnsUnauthorized()
    {
        // Arrange
        const string refreshToken = "valid-refresh-token";
        SsoRenewTokenRequest request = new SsoRenewTokenRequest { RefreshToken = refreshToken };

        SetBearer(null);

        // Act
        var result = await _controller.RenewToken(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        UnauthorizedObjectResult? unauthorizedResult = result.Result as UnauthorizedObjectResult;
        Response<SsoAuthResponse>? response = unauthorizedResult!.Value as Response<SsoAuthResponse>;
        response!.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RenewToken_MissingRefreshToken_ReturnsUnauthorized()
    {
        // Arrange
        SsoRenewTokenRequest request = new SsoRenewTokenRequest { RefreshToken = string.Empty };

        SetBearer(AccessToken());

        // Act
        var result = await _controller.RenewToken(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        UnauthorizedObjectResult? unauthorizedResult = result.Result as UnauthorizedObjectResult;
        Response<SsoAuthResponse>? response = unauthorizedResult!.Value as Response<SsoAuthResponse>;
        response!.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RenewToken_InvalidTokens_ReturnsUnauthorized()
    {
        // Arrange
        string accessToken = AccessToken();
        const string refreshToken = "invalid-refresh-token";
        SsoRenewTokenRequest request = new SsoRenewTokenRequest { RefreshToken = refreshToken };

        SetBearer(accessToken);
        _mockSsoService
            .Setup(s => s.RenewTokenAsync(accessToken, refreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SsoAuthResponse?)null);

        // Act
        var result = await _controller.RenewToken(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        UnauthorizedObjectResult? unauthorizedResult = result.Result as UnauthorizedObjectResult;
        Response<SsoAuthResponse>? response = unauthorizedResult!.Value as Response<SsoAuthResponse>;
        response!.Succeeded.Should().BeFalse();
    }

    /// <summary>The usual case: SSO renews with an expired access token, which authentication would reject.</summary>
    [Fact]
    public async Task RenewToken_ExpiredAccessToken_IsSentToSso()
    {
        string expired = AccessToken(DateTime.UtcNow.AddHours(-1));
        SetBearer(expired);
        _mockSsoService
            .Setup(s => s.RenewTokenAsync(expired, "refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SsoAuthResponse { AccessToken = "new", RefreshToken = "new-refresh" });

        var result = await _controller.RenewToken(new SsoRenewTokenRequest { RefreshToken = "refresh" }, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        _mockUserContext.Verify(u => u.GetToken(), Times.Never, "the validated token is null for an expired one");
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    public async Task RenewToken_MalformedAccessToken_Returns401WithoutCallingSso(string token)
    {
        SetBearer(token);

        var result = await _controller.RenewToken(new SsoRenewTokenRequest { RefreshToken = "refresh" }, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        _mockSsoService.Verify(s => s.RenewTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Logout Tests

    [Fact]
    public async Task Logout_ValidToken_ReturnsOk()
    {
        // Arrange
        const string token = "valid-token";
        const string userId = "user-123";

        _mockUserContext.Setup(u => u.GetToken()).Returns(token);
        _mockUserContext.Setup(u => u.GetUserId()).Returns(userId);
        _mockSsoService
            .Setup(s => s.LogoutAsync(token, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Logout(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<bool>? response = okResult!.Value as Response<bool>;
        response!.Data.Should().BeTrue();
        response.Succeeded.Should().BeTrue();
        // The authenticated caller's ID goes to the service, which clears that user's cached positions.
        _mockSsoService.Verify(s => s.LogoutAsync(token, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Logout_MissingToken_ReturnsUnauthorized()
    {
        // Arrange
        _mockUserContext.Setup(u => u.GetToken()).Returns((string?)null);

        // Act
        var result = await _controller.Logout(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        UnauthorizedObjectResult? unauthorizedResult = result.Result as UnauthorizedObjectResult;
        Response<bool>? response = unauthorizedResult!.Value as Response<bool>;
        response!.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Logout_LogoutFails_ReturnsUnauthorized()
    {
        // Arrange
        const string token = "valid-token";
        const string userId = "user-123";

        _mockUserContext.Setup(u => u.GetToken()).Returns(token);
        _mockUserContext.Setup(u => u.GetUserId()).Returns(userId);
        _mockSsoService
            .Setup(s => s.LogoutAsync(token, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.Logout(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        UnauthorizedObjectResult? unauthorizedResult = result.Result as UnauthorizedObjectResult;
        Response<bool>? response = unauthorizedResult!.Value as Response<bool>;
        response!.Succeeded.Should().BeFalse();
    }

    #endregion
}
