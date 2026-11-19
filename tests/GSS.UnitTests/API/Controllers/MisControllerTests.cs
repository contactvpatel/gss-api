using FluentAssertions;
using GSS.Api.Authorization;
using GSS.Api.Controllers;
using GSS.Api.Models;
using GSS.Business.DTOs;
using GSS.Core.Interfaces.Base;
using GSS.UnitTests.Common;
using GSS.Util.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using IMisService = GSS.Business.Services.Interfaces.IMisService;

namespace GSS.UnitTests.API.Controllers;

/// <summary>
/// Unit tests for MisController.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class MisControllerTests
{
    private readonly Mock<IMisService> _mockService = new();
    private readonly Mock<ICallerValidator> _mockCallerValidator = new();
    private readonly Mock<IUserContext> _mockUserContext = new();
    private readonly Mock<ILogger<MisController>> _mockLogger = new();
    private readonly Mock<IOptionsMonitor<MisServiceOptions>> _mockOptions = new();
    private readonly MisServiceOptions _misOptions = new();
    private readonly MisController _controller;

    public MisControllerTests()
    {
        _mockOptions.Setup(o => o.CurrentValue).Returns(_misOptions);
        _controller = new MisController(_mockService.Object, _mockCallerValidator.Object, _mockUserContext.Object, _mockLogger.Object, _mockOptions.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        SetupCaller(1);
    }

    private void SetupCaller(int divisionId)
    {
        ValidatedCaller caller = new("user-1", 10, 20, divisionId, new[] { 30 });
        _mockCallerValidator
            .Setup(v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerValidationResult.Valid(caller));
    }

    private void SetupInvalidCaller(string reason)
    {
        _mockCallerValidator
            .Setup(v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerValidationResult.Invalid(reason));
    }

    private void SetupDepartment(int departmentId, int divisionId)
    {
        _mockService
            .Setup(s => s.GetDepartmentByIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentDto { DepartmentId = departmentId, DepartmentName = "Department", DepartmentCode = "D", DivisionId = divisionId });
    }

    private void SetupRole(int roleId, int divisionId)
    {
        _mockService
            .Setup(s => s.GetRoleByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleDto { Id = roleId, Name = "Role", DivisionId = divisionId });
    }

    /// <summary>The exact 404 a missing ID gets, so another division's ID is indistinguishable from one that does not exist.</summary>
    private static void AssertNotFoundLikeMissing<T>(ActionResult<Response<T>> result, string entity, int id)
    {
        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var body = notFound.Value.Should().BeOfType<Response<T>>().Subject;
        body.Succeeded.Should().BeFalse();
        body.Message.Should().Be($"{entity} not found");
        body.Errors!.Select(e => e.Message).Should().Equal($"{entity} with ID {id} not found.");
    }

    private static void AssertForbidden<T>(ActionResult<Response<T>> result)
    {
        var forbidden = result.Result.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = forbidden.Value.Should().BeOfType<Response<T>>().Subject;
        body.Succeeded.Should().BeFalse();
        body.Message.Should().Be("Access denied");
        body.Errors.Should().NotBeEmpty();
        body.Errors!.Select(e => e.Message).Should().OnlyContain(m => m == "Access denied", "the denial reason is logged, never returned");
    }

    #region GetDivisions Tests

    [Fact]
    public void GetDivisions_ReturnsConfiguredDivisionsOrderedById()
    {
        // Arrange
        _misOptions.Divisions = new Dictionary<int, string> { [3] = "Three", [1] = "One", [2] = "Two" };

        // Act
        var result = _controller.GetDivisions();

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<Response<IReadOnlyList<DivisionResponse>>>().Subject;
        body.Succeeded.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.Select(d => d.DivisionId).Should().Equal(1, 2, 3);
        body.Data!.Select(d => d.DivisionName).Should().Equal("One", "Two", "Three");
        _mockCallerValidator.Verify(v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void GetDivisions_NoneConfigured_ReturnsEmptyList()
    {
        // Act
        var result = _controller.GetDivisions();

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        var body = ok.Value.Should().BeOfType<Response<IReadOnlyList<DivisionResponse>>>().Subject;
        body.Succeeded.Should().BeTrue();
        body.Data.Should().BeEmpty();
        _mockCallerValidator.Verify(v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region GetAllDepartments Tests

    [Fact]
    public async Task GetAllDepartments_ValidDivisionId_ReturnsOk()
    {
        // Arrange
        const int divisionId = 1;
        List<DepartmentDto> departments = new List<DepartmentDto>
        {
            new() { DepartmentId = 1, DepartmentName = "Department 1", DepartmentCode = "D1", DivisionId = divisionId },
            new() { DepartmentId = 2, DepartmentName = "Department 2", DepartmentCode = "D2", DivisionId = divisionId }
        };

        _mockService
            .Setup(s => s.GetAllDepartmentsAsync(divisionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(departments);

        // Act
        var result = await _controller.GetAllDepartments(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<IReadOnlyList<DepartmentDto>>? response = okResult!.Value as Response<IReadOnlyList<DepartmentDto>>;
        response!.Data.Should().HaveCount(2);
        response.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllDepartments_NoDepartments_ReturnsOkWithEmptyList()
    {
        // Arrange
        const int divisionId = 1;
        _mockService
            .Setup(s => s.GetAllDepartmentsAsync(divisionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DepartmentDto>());

        // Act
        var result = await _controller.GetAllDepartments(CancellationToken.None);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        Response<IReadOnlyList<DepartmentDto>>? response = ok.Value as Response<IReadOnlyList<DepartmentDto>>;
        response!.Succeeded.Should().BeTrue();
        response.Data.Should().BeEmpty();
    }

    #endregion

    #region Caller Division Scoping Tests

    [Fact]
    public async Task GetAllRoles_UsesCallerDivision()
    {
        SetupCaller(7);
        _mockService
            .Setup(s => s.GetAllRolesAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RoleDto> { new() { Id = 1, Name = "Role", DivisionId = 7 } });

        var result = await _controller.GetAllRoles(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        _mockService.Verify(s => s.GetAllRolesAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        _mockService.Verify(s => s.GetAllRolesAsync(1, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAllRoles_CallerInvalid_Returns403AndSkipsMis()
    {
        SetupInvalidCaller("Division 9 not on position 10");

        var result = await _controller.GetAllRoles(CancellationToken.None);

        AssertForbidden(result);
        _mockService.Verify(s => s.GetAllRolesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAllDepartments_UsesCallerDivision()
    {
        SetupCaller(7);
        _mockService
            .Setup(s => s.GetAllDepartmentsAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DepartmentDto> { new() { DepartmentId = 1, DepartmentName = "D", DepartmentCode = "D1", DivisionId = 7 } });

        var result = await _controller.GetAllDepartments(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        _mockService.Verify(s => s.GetAllDepartmentsAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllDepartments_CallerInvalid_Returns403AndSkipsMis()
    {
        SetupInvalidCaller("Division 9 not on position 10");

        var result = await _controller.GetAllDepartments(CancellationToken.None);

        AssertForbidden(result);
        _mockService.Verify(s => s.GetAllDepartmentsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAllRoleTypes_UsesCallerDivision()
    {
        SetupCaller(7);
        _mockService
            .Setup(s => s.GetAllRoleTypesAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RoleTypeDto> { new() { Id = 1, Name = "RT", DivisionId = 7 } });

        var result = await _controller.GetAllRoleTypes(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        _mockService.Verify(s => s.GetAllRoleTypesAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllRoleTypes_CallerInvalid_Returns403AndSkipsMis()
    {
        SetupInvalidCaller("Division 9 not on position 10");

        var result = await _controller.GetAllRoleTypes(CancellationToken.None);

        AssertForbidden(result);
        _mockService.Verify(s => s.GetAllRoleTypesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPersonPositions_NoHeaders_DoesNotValidateCaller()
    {
        _mockUserContext.Setup(u => u.GetUserId()).Returns("user-123");
        _mockService
            .Setup(s => s.GetPersonPositionsAsync("user-123", It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PersonPositionDto> { new() { PersonId = 123, RoleId = 1, PositionId = 1, RoleName = "Role" } });

        await _controller.GetPersonPositions(CancellationToken.None);

        _mockCallerValidator.Verify(v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region GetDepartmentById Tests

    [Fact]
    public async Task GetDepartmentById_ValidId_ReturnsOk()
    {
        // Arrange
        const int departmentId = 1;
        SetupDepartment(departmentId, divisionId: 1);

        // Act
        var result = await _controller.GetDepartmentById(departmentId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<DepartmentDto>? response = okResult!.Value as Response<DepartmentDto>;
        response!.Data.Should().NotBeNull();
        response.Data!.DepartmentId.Should().Be(departmentId);
        response.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetDepartmentById_OtherDivision_Returns404LikeMissing()
    {
        SetupDepartment(1, divisionId: 2);

        var result = await _controller.GetDepartmentById(1, CancellationToken.None);

        AssertNotFoundLikeMissing(result, "Department", 1);
    }

    [Fact]
    public async Task GetDepartmentById_CallerInvalid_Returns403AndSkipsMis()
    {
        SetupInvalidCaller("Missing or invalid x-app-* headers");

        var result = await _controller.GetDepartmentById(1, CancellationToken.None);

        AssertForbidden(result);
        _mockService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetDepartmentById_InvalidId_ReturnsNotFound()
    {
        // Arrange
        const int departmentId = 999;
        _mockService
            .Setup(s => s.GetDepartmentByIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DepartmentDto?)null);

        // Act
        var result = await _controller.GetDepartmentById(departmentId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
        NotFoundObjectResult? notFoundResult = result.Result as NotFoundObjectResult;
        Response<DepartmentDto>? response = notFoundResult!.Value as Response<DepartmentDto>;
        response!.Succeeded.Should().BeFalse();
    }

    #endregion

    #region GetAllRoleTypes Tests

    [Fact]
    public async Task GetAllRoleTypes_ValidDivisionId_ReturnsOk()
    {
        // Arrange
        const int divisionId = 1;
        List<RoleTypeDto> roleTypes = new List<RoleTypeDto>
        {
            new() { Id = 1, Name = "RoleType 1", DivisionId = divisionId },
            new() { Id = 2, Name = "RoleType 2", DivisionId = divisionId }
        };

        _mockService
            .Setup(s => s.GetAllRoleTypesAsync(divisionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roleTypes);

        // Act
        var result = await _controller.GetAllRoleTypes(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<IReadOnlyList<RoleTypeDto>>? response = okResult!.Value as Response<IReadOnlyList<RoleTypeDto>>;
        response!.Data.Should().HaveCount(2);
        response.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllRoleTypes_NoRoleTypes_ReturnsOkWithEmptyList()
    {
        // Arrange
        const int divisionId = 1;
        _mockService
            .Setup(s => s.GetAllRoleTypesAsync(divisionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RoleTypeDto>());

        // Act
        var result = await _controller.GetAllRoleTypes(CancellationToken.None);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        Response<IReadOnlyList<RoleTypeDto>>? response = ok.Value as Response<IReadOnlyList<RoleTypeDto>>;
        response!.Succeeded.Should().BeTrue();
        response.Data.Should().BeEmpty();
    }

    #endregion

    #region GetAllRoles Tests

    [Fact]
    public async Task GetAllRoles_ValidDivisionId_ReturnsOk()
    {
        // Arrange
        const int divisionId = 1;
        List<RoleDto> roles = new List<RoleDto>
        {
            new() { Id = 1, Name = "Role 1", DivisionId = divisionId },
            new() { Id = 2, Name = "Role 2", DivisionId = divisionId }
        };

        _mockService
            .Setup(s => s.GetAllRolesAsync(divisionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);

        // Act
        var result = await _controller.GetAllRoles(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<IReadOnlyList<RoleDto>>? response = okResult!.Value as Response<IReadOnlyList<RoleDto>>;
        response!.Data.Should().HaveCount(2);
        response.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllRoles_NoRoles_ReturnsOkWithEmptyList()
    {
        // Arrange
        const int divisionId = 1;
        _mockService
            .Setup(s => s.GetAllRolesAsync(divisionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RoleDto>());

        // Act
        var result = await _controller.GetAllRoles(CancellationToken.None);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        Response<IReadOnlyList<RoleDto>>? response = ok.Value as Response<IReadOnlyList<RoleDto>>;
        response!.Succeeded.Should().BeTrue();
        response.Data.Should().BeEmpty();
    }

    #endregion

    #region GetRoleById Tests

    [Fact]
    public async Task GetRoleById_ValidId_ReturnsOk()
    {
        // Arrange
        const int roleId = 1;
        SetupRole(roleId, divisionId: 1);

        // Act
        var result = await _controller.GetRoleById(roleId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<RoleDto>? response = okResult!.Value as Response<RoleDto>;
        response!.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(roleId);
        response.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetRoleById_OtherDivision_Returns404LikeMissing()
    {
        SetupRole(1, divisionId: 2);

        var result = await _controller.GetRoleById(1, CancellationToken.None);

        AssertNotFoundLikeMissing(result, "Role", 1);
    }

    [Fact]
    public async Task GetRoleById_CallerInvalid_Returns403AndSkipsMis()
    {
        SetupInvalidCaller("Missing or invalid x-app-* headers");

        var result = await _controller.GetRoleById(1, CancellationToken.None);

        AssertForbidden(result);
        _mockService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetRoleById_InvalidId_ReturnsNotFound()
    {
        // Arrange
        const int roleId = 999;
        _mockService
            .Setup(s => s.GetRoleByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleDto?)null);

        // Act
        var result = await _controller.GetRoleById(roleId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
        NotFoundObjectResult? notFoundResult = result.Result as NotFoundObjectResult;
        Response<RoleDto>? response = notFoundResult!.Value as Response<RoleDto>;
        response!.Succeeded.Should().BeFalse();
    }

    #endregion

    #region GetRolesByDepartmentId Tests

    [Fact]
    public async Task GetRolesByDepartmentId_ValidDepartmentId_ReturnsOk()
    {
        // Arrange
        const int departmentId = 1;
        SetupDepartment(departmentId, divisionId: 1);
        List<RoleDto> roles = new List<RoleDto>
        {
            new() { Id = 1, Name = "Role 1", DepartmentId = departmentId },
            new() { Id = 2, Name = "Role 2", DepartmentId = departmentId }
        };

        _mockService
            .Setup(s => s.GetRolesByDepartmentIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);

        // Act
        var result = await _controller.GetRolesByDepartmentId(departmentId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<IReadOnlyList<RoleDto>>? response = okResult!.Value as Response<IReadOnlyList<RoleDto>>;
        response!.Data.Should().HaveCount(2);
        response.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetRolesByDepartmentId_NoRoles_ReturnsOkWithEmptyList()
    {
        // Arrange
        const int departmentId = 999;
        SetupDepartment(departmentId, divisionId: 1);
        _mockService
            .Setup(s => s.GetRolesByDepartmentIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RoleDto>());

        // Act
        var result = await _controller.GetRolesByDepartmentId(departmentId, CancellationToken.None);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        Response<IReadOnlyList<RoleDto>>? response = ok.Value as Response<IReadOnlyList<RoleDto>>;
        response!.Succeeded.Should().BeTrue();
        response.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRolesByDepartmentId_DepartmentMissing_Returns404AndSkipsRoleList()
    {
        _mockService
            .Setup(s => s.GetDepartmentByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DepartmentDto?)null);

        var result = await _controller.GetRolesByDepartmentId(5, CancellationToken.None);

        AssertNotFoundLikeMissing(result, "Department", 5);
        _mockService.Verify(s => s.GetRolesByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRolesByDepartmentId_OtherDivision_Returns404LikeMissingAndSkipsRoleList()
    {
        SetupDepartment(5, divisionId: 2);

        var result = await _controller.GetRolesByDepartmentId(5, CancellationToken.None);

        AssertNotFoundLikeMissing(result, "Department", 5);
        _mockService.Verify(s => s.GetRolesByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRolesByDepartmentId_CallerInvalid_Returns403AndSkipsMis()
    {
        SetupInvalidCaller("Missing or invalid x-app-* headers");

        var result = await _controller.GetRolesByDepartmentId(5, CancellationToken.None);

        AssertForbidden(result);
        _mockService.VerifyNoOtherCalls();
    }

    #endregion

    #region GetPositionsByRoleId Tests

    [Fact]
    public async Task GetPositionsByRoleId_ValidRoleId_ReturnsOk()
    {
        // Arrange
        const int roleId = 1;
        SetupRole(roleId, divisionId: 1);
        List<PositionDto> positions = new List<PositionDto>
        {
            new() { Id = 1, Name = "Position 1", RoleId = roleId },
            new() { Id = 2, Name = "Position 2", RoleId = roleId }
        };

        _mockService
            .Setup(s => s.GetPositionsByRoleIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        // Act
        var result = await _controller.GetPositionsByRoleId(roleId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<IReadOnlyList<PositionDto>>? response = okResult!.Value as Response<IReadOnlyList<PositionDto>>;
        response!.Data.Should().HaveCount(2);
        response.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetPositionsByRoleId_NoPositions_ReturnsOkWithEmptyList()
    {
        // Arrange
        const int roleId = 999;
        SetupRole(roleId, divisionId: 1);
        _mockService
            .Setup(s => s.GetPositionsByRoleIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PositionDto>());

        // Act
        var result = await _controller.GetPositionsByRoleId(roleId, CancellationToken.None);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        Response<IReadOnlyList<PositionDto>>? response = ok.Value as Response<IReadOnlyList<PositionDto>>;
        response!.Succeeded.Should().BeTrue();
        response.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPositionsByRoleId_RoleMissing_Returns404AndSkipsPositionList()
    {
        _mockService
            .Setup(s => s.GetRoleByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleDto?)null);

        var result = await _controller.GetPositionsByRoleId(5, CancellationToken.None);

        AssertNotFoundLikeMissing(result, "Role", 5);
        _mockService.Verify(s => s.GetPositionsByRoleIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPositionsByRoleId_OtherDivision_Returns404LikeMissingAndSkipsPositionList()
    {
        SetupRole(5, divisionId: 2);

        var result = await _controller.GetPositionsByRoleId(5, CancellationToken.None);

        AssertNotFoundLikeMissing(result, "Role", 5);
        _mockService.Verify(s => s.GetPositionsByRoleIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPositionsByRoleId_CallerInvalid_Returns403AndSkipsMis()
    {
        SetupInvalidCaller("Missing or invalid x-app-* headers");

        var result = await _controller.GetPositionsByRoleId(5, CancellationToken.None);

        AssertForbidden(result);
        _mockService.VerifyNoOtherCalls();
    }

    #endregion

    #region GetPersonPositions Tests

    [Fact]
    public async Task GetPersonPositions_ValidUserId_ReturnsOk()
    {
        // Arrange
        const string userId = "user-123";
        List<PersonPositionDto> positions = new List<PersonPositionDto>
        {
            new() { PersonId = 123, RoleId = 1, PositionId = 1, RoleName = "Role 1" },
            new() { PersonId = 123, RoleId = 2, PositionId = 2, RoleName = "Role 2" }
        };

        _mockUserContext.Setup(u => u.GetUserId()).Returns(userId);
        _mockService
            .Setup(s => s.GetPersonPositionsAsync(userId, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        // Act
        var result = await _controller.GetPersonPositions(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        OkObjectResult? okResult = result.Result as OkObjectResult;
        Response<IReadOnlyList<PersonPositionDto>>? response = okResult!.Value as Response<IReadOnlyList<PersonPositionDto>>;
        response!.Data.Should().HaveCount(2);
        response.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetPersonPositions_MissingUserId_ReturnsUnauthorized()
    {
        // Arrange
        _mockUserContext.Setup(u => u.GetUserId()).Returns((string?)null);

        // Act
        var result = await _controller.GetPersonPositions(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        UnauthorizedObjectResult? unauthorizedResult = result.Result as UnauthorizedObjectResult;
        Response<IReadOnlyList<PersonPositionDto>>? response = unauthorizedResult!.Value as Response<IReadOnlyList<PersonPositionDto>>;
        response!.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task GetPersonPositions_NoPositions_ReturnsOkWithEmptyList()
    {
        // Arrange
        const string userId = "user-123";
        _mockUserContext.Setup(u => u.GetUserId()).Returns(userId);
        _mockService
            .Setup(s => s.GetPersonPositionsAsync(userId, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PersonPositionDto>());

        // Act
        var result = await _controller.GetPersonPositions(CancellationToken.None);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        Response<IReadOnlyList<PersonPositionDto>>? response = ok.Value as Response<IReadOnlyList<PersonPositionDto>>;
        response!.Succeeded.Should().BeTrue();
        response.Data.Should().BeEmpty();
    }

    #endregion
}
