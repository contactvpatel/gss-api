using System.Security.Claims;
using FluentAssertions;
using GSS.Api.Authentication;
using GSS.Api.Authorization;
using GSS.Business.DTOs;
using GSS.Business.Services.Interfaces;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace GSS.UnitTests.API.Authorization;

[Trait("Category", TestCategories.Unit)]
public class CallerValidatorTests
{
    private const string UserId = "42";

    private readonly Mock<IMisService> _mockMisService = new();

    public CallerValidatorTests()
    {
        // Caller holds position 10 / role 3 in departments 11 and 12 of division 7
        _mockMisService
            .Setup(s => s.GetPersonPositionsAsync(UserId, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MisPosition(10, 3, (11, 7), (12, 7))]);
    }

    [Theory]
    [InlineData("11,12")]
    [InlineData("11, 12")]
    public async Task Validate_ValidHeaders_ReturnsCallerWithHeaderDivision(string departmentHeader)
    {
        var httpContext = CreateContext();
        httpContext.Request.Headers[AsmPermissionHandler.DepartmentIdHeader] = departmentHeader;

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Caller!.UserId.Should().Be(UserId);
        result.Caller.PositionId.Should().Be(10);
        result.Caller.RoleId.Should().Be(3);
        result.Caller.DivisionId.Should().Be(7);
        result.Caller.DepartmentIds.Should().Equal(11, 12);
    }

    [Theory]
    [InlineData(AsmPermissionHandler.PositionIdHeader, null)]
    [InlineData(AsmPermissionHandler.PositionIdHeader, "0")]
    [InlineData(AsmPermissionHandler.PositionIdHeader, "-1")]
    [InlineData(AsmPermissionHandler.PositionIdHeader, "abc")]
    [InlineData(AsmPermissionHandler.RoleIdHeader, null)]
    [InlineData(AsmPermissionHandler.RoleIdHeader, "0")]
    [InlineData(AsmPermissionHandler.RoleIdHeader, "-1")]
    [InlineData(AsmPermissionHandler.RoleIdHeader, "abc")]
    [InlineData(AsmPermissionHandler.DivisionIdHeader, null)]
    [InlineData(AsmPermissionHandler.DivisionIdHeader, "0")]
    [InlineData(AsmPermissionHandler.DivisionIdHeader, "-1")]
    [InlineData(AsmPermissionHandler.DivisionIdHeader, "abc")]
    [InlineData(AsmPermissionHandler.DepartmentIdHeader, null)]
    [InlineData(AsmPermissionHandler.DepartmentIdHeader, "0")]
    [InlineData(AsmPermissionHandler.DepartmentIdHeader, "-1")]
    [InlineData(AsmPermissionHandler.DepartmentIdHeader, "abc")]
    [InlineData(AsmPermissionHandler.DepartmentIdHeader, "11,x")]
    [InlineData(AsmPermissionHandler.DepartmentIdHeader, ",")]
    public async Task Validate_MissingHeader_FailsWithoutCallingMis(string header, string? value)
    {
        var httpContext = CreateContext();
        if (value == null)
        {
            httpContext.Request.Headers.Remove(header);
        }
        else
        {
            httpContext.Request.Headers[header] = value;
        }

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
        VerifyMisCalls(Times.Never());
    }

    [Fact]
    public async Task Validate_PositionNotHeld_Fails()
    {
        var httpContext = CreateContext();
        httpContext.Request.Headers[AsmPermissionHandler.PositionIdHeader] = "10";
        _mockMisService
            .Setup(s => s.GetPersonPositionsAsync(UserId, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MisPosition(77, 3, (11, 7))]);

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("Position 10");
    }

    [Fact]
    public async Task Validate_RoleMismatch_Fails()
    {
        var httpContext = CreateContext();
        httpContext.Request.Headers[AsmPermissionHandler.RoleIdHeader] = "4";

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("Role 4");
    }

    [Fact]
    public async Task Validate_DivisionNotOnPosition_Fails()
    {
        var httpContext = CreateContext();
        httpContext.Request.Headers[AsmPermissionHandler.DivisionIdHeader] = "9";

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("Division 9");
    }

    [Fact]
    public async Task Validate_DepartmentNotOnPosition_Fails()
    {
        var httpContext = CreateContext();
        httpContext.Request.Headers[AsmPermissionHandler.DepartmentIdHeader] = "11,99";

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("Department");
    }

    [Fact]
    public async Task Validate_UnauthenticatedCaller_FailsWithoutCallingMis()
    {
        var httpContext = CreateContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        VerifyMisCalls(Times.Never());
    }

    [Fact]
    public async Task Validate_TwiceInOneRequest_CallsMisOnce()
    {
        var httpContext = CreateContext();
        var validator = CreateValidator();

        var first = await validator.ValidateAsync(httpContext, CancellationToken.None);
        var second = await validator.ValidateAsync(httpContext, CancellationToken.None);

        VerifyMisCalls(Times.Once());
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task Validate_UserChangedWithinRequest_RevalidatesForNewUser()
    {
        const string otherUserId = "43";
        _mockMisService
            .Setup(s => s.GetPersonPositionsAsync(otherUserId, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MisPosition(10, 3, (11, 7), (12, 7))]);
        var httpContext = CreateContext();
        var validator = CreateValidator();

        var first = await validator.ValidateAsync(httpContext, CancellationToken.None);
        httpContext.User = CreatePrincipal(otherUserId);
        var second = await validator.ValidateAsync(httpContext, CancellationToken.None);

        VerifyMisCalls(Times.Exactly(2));
        first.Caller!.UserId.Should().Be(UserId);
        second.Caller!.UserId.Should().Be(otherUserId);
    }

    private CallerValidator CreateValidator()
    {
        return new CallerValidator(_mockMisService.Object);
    }

    private void VerifyMisCalls(Times times)
    {
        _mockMisService.Verify(
            s => s.GetPersonPositionsAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), times);
    }

    private static DefaultHttpContext CreateContext()
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers[AsmPermissionHandler.PositionIdHeader] = "10";
        httpContext.Request.Headers[AsmPermissionHandler.RoleIdHeader] = "3";
        httpContext.Request.Headers[AsmPermissionHandler.DivisionIdHeader] = "7";
        httpContext.Request.Headers[AsmPermissionHandler.DepartmentIdHeader] = "11,12";
        httpContext.User = CreatePrincipal(UserId);
        return httpContext;
    }

    private static ClaimsPrincipal CreatePrincipal(string userId)
    {
        long exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId), new Claim("exp", exp.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            SsoAuthenticationDefaults.AuthenticationScheme));
    }

    private static PersonPositionDto MisPosition(int positionId, int roleId, params (int DepartmentId, int DivisionId)[] departments)
    {
        return new PersonPositionDto
        {
            PersonId = 42,
            PositionId = positionId,
            RoleId = roleId,
            RoleName = "Role",
            Department = departments
                .Select(d => new DepartmentDto { DepartmentId = d.DepartmentId, DivisionId = d.DivisionId, DepartmentName = "Dept", DepartmentCode = "D" })
                .ToList()
        };
    }
}
