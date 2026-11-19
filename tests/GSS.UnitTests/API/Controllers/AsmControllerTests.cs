using FluentAssertions;
using GSS.Api.Controllers;
using GSS.Api.Models;
using GSS.Business.DTOs;
using GSS.Core.Interfaces.Base;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using IAsmService = GSS.Business.Services.Interfaces.IAsmService;

namespace GSS.UnitTests.API.Controllers;

/// <summary>
/// Unit tests for AsmController.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class AsmControllerTests
{
    private readonly Mock<IAsmService> _mockService = new();
    private readonly Mock<IUserContext> _mockUserContext = new();
    private readonly Mock<ILogger<AsmController>> _mockLogger = new();
    private readonly AsmController _controller;

    public AsmControllerTests()
    {
        _controller = new AsmController(_mockService.Object, _mockUserContext.Object, _mockLogger.Object);
    }

    #region Get Tests

    [Fact]
    public async Task Get_ValidPersonIdAndToken_ReturnsOk()
    {
        // Arrange
        const string personId = "person-123";
        const string token = "valid-token";
        List<AsmResponseDto> securityInfo = new List<AsmResponseDto>
        {
            new()
            {
                RoleId = 111,
                PositionId = 111,
                ApplicationAccess = new List<ApplicationAccessDto>
                {
                    new() { ModuleCode = "SHR", ModuleName = "Share Report", ModuleType = "Module", HasViewAccess = true }
                }
            }
        };

        _mockUserContext.Setup(u => u.GetUserId()).Returns(personId);
        _mockUserContext.Setup(u => u.GetToken()).Returns(token);
        _mockService
            .Setup(s => s.GetApplicationSecurityAsync(token, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(securityInfo);

        // Act
        var result = await _controller.Get(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<IReadOnlyList<AsmResponseDto>>? response = okResult!.Value as Response<IReadOnlyList<AsmResponseDto>>;
        response!.Data.Should().HaveCount(1);
        response.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Get_MissingPersonId_ReturnsUnauthorized()
    {
        // Arrange
        const string token = "valid-token";
        _mockUserContext.Setup(u => u.GetUserId()).Returns((string?)null);
        _mockUserContext.Setup(u => u.GetToken()).Returns(token);

        // Act
        var result = await _controller.Get(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        UnauthorizedObjectResult? unauthorizedResult = result.Result as UnauthorizedObjectResult;
        Response<IReadOnlyList<AsmResponseDto>>? response = unauthorizedResult!.Value as Response<IReadOnlyList<AsmResponseDto>>;
        response!.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Get_MissingToken_ReturnsUnauthorized()
    {
        // Arrange
        const string personId = "person-123";
        _mockUserContext.Setup(u => u.GetUserId()).Returns(personId);
        _mockUserContext.Setup(u => u.GetToken()).Returns((string?)null);

        // Act
        var result = await _controller.Get(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        UnauthorizedObjectResult? unauthorizedResult = result.Result as UnauthorizedObjectResult;
        Response<IReadOnlyList<AsmResponseDto>>? response = unauthorizedResult!.Value as Response<IReadOnlyList<AsmResponseDto>>;
        response!.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Get_NoSecurityInfo_ReturnsOkWithEmptyList()
    {
        // Arrange
        const string personId = "person-123";
        const string token = "valid-token";
        _mockUserContext.Setup(u => u.GetUserId()).Returns(personId);
        _mockUserContext.Setup(u => u.GetToken()).Returns(token);
        _mockService
            .Setup(s => s.GetApplicationSecurityAsync(token, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AsmResponseDto>());

        // Act
        var result = await _controller.Get(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<IReadOnlyList<AsmResponseDto>>? response = okResult!.Value as Response<IReadOnlyList<AsmResponseDto>>;
        response!.Data.Should().BeEmpty();
        response.Succeeded.Should().BeTrue();
    }

    #endregion
}
