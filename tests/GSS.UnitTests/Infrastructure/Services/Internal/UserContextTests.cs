using System.Security.Claims;
using FluentAssertions;
using GSS.Infrastructure.Services.Internal;
using GSS.UnitTests.Common;
using GSS.Util.Security;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Services.Internal;

/// <summary>
/// Unit tests for UserContext.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class UserContextTests
{
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
    private readonly UserContext _userContext;

    public UserContextTests()
    {
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        _userContext = new UserContext(_mockHttpContextAccessor.Object);
    }

    #region GetUserId Tests

    [Fact]
    public void GetUserId_NoUserIdInContext_ReturnsNull()
    {
        // Arrange
        DefaultHttpContext httpContext = new();
        _mockHttpContextAccessor.Setup(h => h.HttpContext).Returns(httpContext);

        // Act
        string? result = _userContext.GetUserId();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetUserId_NullHttpContext_ReturnsNull()
    {
        // Arrange
        _mockHttpContextAccessor.Setup(h => h.HttpContext).Returns((HttpContext?)null);

        // Act
        string? result = _userContext.GetUserId();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetUserId_AuthenticatedPrincipal_ReturnsNameIdentifierClaim()
    {
        // Arrange
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "from-claim")], "SSOv1"))
        };
        _mockHttpContextAccessor.Setup(h => h.HttpContext).Returns(httpContext);

        // Act
        string? result = _userContext.GetUserId();

        // Assert
        result.Should().Be("from-claim");
    }

    [Fact]
    public void GetUserId_UnauthenticatedPrincipal_ReturnsNull_EvenWithAUserIdItem()
    {
        // Arrange: no auth type = unauthenticated
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "spoofed")]))
        };
        httpContext.Items["UserId"] = "from-items";
        _mockHttpContextAccessor.Setup(h => h.HttpContext).Returns(httpContext);

        // Act
        string? result = _userContext.GetUserId();

        // Assert
        result.Should().BeNull(because: "the authenticated claim is the only source of the user id");
    }

    #endregion

    #region GetTokenExpiresAtUtc Tests

    [Fact]
    public void GetTokenExpiresAtUtc_AuthenticatedWithExp_ReturnsUtc()
    {
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("exp", "1735689600")], "SSOv1"))
        };
        _mockHttpContextAccessor.Setup(h => h.HttpContext).Returns(httpContext);

        _userContext.GetTokenExpiresAtUtc().Should().Be(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetTokenExpiresAtUtc_Unauthenticated_ReturnsNull()
    {
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("exp", "1735689600")]))
        };
        _mockHttpContextAccessor.Setup(h => h.HttpContext).Returns(httpContext);

        _userContext.GetTokenExpiresAtUtc().Should().BeNull();
    }

    #endregion

    #region GetToken Tests

    [Fact]
    public void GetToken_WithTokenInContext_ReturnsToken()
    {
        // Arrange
        DefaultHttpContext httpContext = new();
        httpContext.Items[HttpContextItemKeys.UserToken] = "token123";
        _mockHttpContextAccessor.Setup(h => h.HttpContext).Returns(httpContext);

        // Act
        string? result = _userContext.GetToken();

        // Assert
        result.Should().Be("token123");
    }

    [Fact]
    public void GetToken_NoTokenInContext_ReturnsNull()
    {
        // Arrange
        DefaultHttpContext httpContext = new();
        _mockHttpContextAccessor.Setup(h => h.HttpContext).Returns(httpContext);

        // Act
        string? result = _userContext.GetToken();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetToken_NullHttpContext_ReturnsNull()
    {
        // Arrange
        _mockHttpContextAccessor.Setup(h => h.HttpContext).Returns((HttpContext?)null);

        // Act
        string? result = _userContext.GetToken();

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
