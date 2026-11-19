using System.Security.Claims;
using FluentAssertions;
using GSS.Api.Authentication;
using GSS.Api.Authorization;
using GSS.Business.DTOs;
using GSS.Business.Services.Interfaces;
using GSS.UnitTests.Common;
using GSS.Util.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace GSS.UnitTests.API.Authorization;

[Trait("Category", TestCategories.Unit)]
public class AsmPermissionHandlerTests
{
    private const string UserId = "42";
    private const string Token = "token";
    private const int PositionId = 10;
    private const int RoleId = 3;

    private const int DivisionId = 5;

    private readonly Mock<IAsmService> _mockAsmService = new();
    private readonly Mock<IMisService> _mockMisService = new();

    public AsmPermissionHandlerTests()
    {
        // Caller holds position 10 / role 3 in departments 20 and 21 of division 5
        SetupMisPositions(MisPosition(PositionId, RoleId, (20, DivisionId), (21, DivisionId)));
    }

    /// <summary>The fallback policy runs every handler on each request; without [AsmAuthorize] none of its services are built.</summary>
    [Fact]
    public async Task RequestWithoutAsmRequirement_DoesNotResolveCallerValidatorOrAsmService()
    {
        int resolved = 0;
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddSingleton<IAuthorizationHandler, AsmPermissionHandler>();
        services.AddScoped<ICallerValidator>(_ => { resolved++; return Mock.Of<ICallerValidator>(); });
        services.AddScoped<IAsmService>(_ => { resolved++; return _mockAsmService.Object; });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        ClaimsPrincipal user = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId)], SsoAuthenticationDefaults.AuthenticationScheme));
        DefaultHttpContext httpContext = new() { RequestServices = scope.ServiceProvider, User = user };

        var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(user, httpContext, new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        result.Succeeded.Should().BeTrue();
        resolved.Should().Be(0);
    }

    [Fact]
    public async Task SelectedPositionGrantsPermission_Succeeds()
    {
        SetupPositions(Position(PositionId, RoleId, Access("APP", create: true)));

        var result = await AuthorizeAsync([Require(ModuleCode.Application, AccessType.Create)]);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task PermissionOnlyOnAnotherPosition_IsDenied()
    {
        SetupPositions(
            Position(PositionId, RoleId, Access("APP", view: true)),
            Position(99, 1, Access("APP", delete: true)));

        var result = await AuthorizeAsync([Require(ModuleCode.Application, AccessType.Delete)]);

        result.Succeeded.Should().BeFalse(because: "permissions of other positions must never count");
    }

    [Fact]
    public async Task FailedCallerValidation_IsDeniedWithTheValidatorReasonWithoutCallingAsm()
    {
        Mock<ICallerValidator> mockValidator = new();
        mockValidator
            .Setup(v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerValidationResult.Invalid("x"));

        var result = await AuthorizeAsync([Require(ModuleCode.Application, AccessType.View)], callerValidator: mockValidator.Object);

        result.Succeeded.Should().BeFalse();
        result.Failure!.FailureReasons.Select(r => r.Message).Should().Equal("x");
        _mockAsmService.Verify(
            s => s.GetApplicationSecurityAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PositionNotHeldInMis_IsDeniedWithoutCallingAsm()
    {
        SetupPositions(Position(77, RoleId, Access("APP", view: true)));

        var result = await AuthorizeAsync([Require(ModuleCode.Application, AccessType.View)], positionId: "77");

        result.Succeeded.Should().BeFalse();
        _mockAsmService.Verify(
            s => s.GetApplicationSecurityAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnauthenticatedCaller_IsDeniedWithoutCallingAsm()
    {
        var result = await AuthorizeAsync([Require(ModuleCode.Application, AccessType.View)], user: new ClaimsPrincipal(new ClaimsIdentity()));

        result.Succeeded.Should().BeFalse();
        _mockAsmService.Verify(
            s => s.GetApplicationSecurityAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WrongAccessFlag_IsDenied()
    {
        SetupPositions(Position(PositionId, RoleId, Access("APP", view: true)));

        var result = await AuthorizeAsync([Require(ModuleCode.Application, AccessType.Update)]);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task OrWithinAttribute_SucceedsWhenAnyGranted()
    {
        SetupPositions(Position(PositionId, RoleId, Access("APP", update: true)));

        var result = await AuthorizeAsync([Require(ModuleCode.Application, AccessType.Create, AccessType.Update)]);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task SeparateAttributes_AreAnd()
    {
        SetupPositions(Position(PositionId, RoleId, Access("APP", view: true)));

        var result = await AuthorizeAsync(
        [
            Require(ModuleCode.Application, AccessType.View),
            Require(ModuleCode.Administration, AccessType.View)
        ]);

        result.Succeeded.Should().BeFalse(because: "every attribute must be satisfied");
    }

    [Fact]
    public async Task CrossModuleOrRequirement_SucceedsWhenAnyGranted()
    {
        SetupPositions(Position(PositionId, RoleId, Access("ADMIN", view: true)));

        var result = await AuthorizeAsync(
        [
            AsmPermissionRequirement.Any(
                new AsmPermission(ModuleCode.Application, AccessType.View),
                new AsmPermission(ModuleCode.Administration, AccessType.View))
        ]);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AllowAny_RequiresSomeAccessOnSelectedPosition()
    {
        SetupPositions(Position(PositionId, RoleId), Position(99, 1, Access("APP", view: true)));

        var result = await AuthorizeAsync([Require(ModuleCode.Application, AccessType.AllowAny)]);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ModuleCodeMatch_IsCaseSensitive()
    {
        SetupPositions(Position(PositionId, RoleId, Access("app", view: true)));

        var result = await AuthorizeAsync([Require(ModuleCode.Application, AccessType.View)]);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Attribute_BuildsOneRequirementWithAllAccessTypes()
    {
        AsmAuthorizeAttribute attribute = new(ModuleCode.Administration, AccessType.Create, AccessType.Update);

        var requirement = attribute.GetRequirements().Should().ContainSingle().Subject;

        requirement.Should().BeOfType<AsmPermissionRequirement>()
            .Which.AnyOf.Should().Equal(
                new AsmPermission(ModuleCode.Administration, AccessType.Create),
                new AsmPermission(ModuleCode.Administration, AccessType.Update));
    }

    private static IAuthorizationRequirement Require(ModuleCode module, AccessType access, params AccessType[] orAccess)
    {
        return new AsmAuthorizeAttribute(module, access, orAccess).GetRequirements().Single();
    }

    private void SetupMisPositions(params PersonPositionDto[] positions)
    {
        _mockMisService
            .Setup(s => s.GetPersonPositionsAsync(UserId, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);
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

    private void SetupPositions(params AsmResponseDto[] positions)
    {
        _mockAsmService
            .Setup(s => s.GetApplicationSecurityAsync(Token, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);
    }

    private static AsmResponseDto Position(int positionId, int roleId, params ApplicationAccessDto[] accesses)
    {
        return new AsmResponseDto { PositionId = positionId, RoleId = roleId, ApplicationAccess = accesses };
    }

    private static ApplicationAccessDto Access(
        string moduleCode, bool view = false, bool create = false, bool update = false, bool delete = false)
    {
        return new ApplicationAccessDto
        {
            ModuleCode = moduleCode,
            ModuleName = moduleCode,
            ModuleType = "Module",
            HasViewAccess = view,
            HasCreateAccess = create,
            HasUpdateAccess = update,
            HasDeleteAccess = delete
        };
    }

    private async Task<AuthorizationResult> AuthorizeAsync(
        IAuthorizationRequirement[] requirements,
        string? positionId = "10",
        string? roleId = "3",
        ClaimsPrincipal? user = null,
        string? divisionId = "5",
        string? departmentIds = "20",
        ICallerValidator? callerValidator = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthorizationCore();
        if (callerValidator != null)
        {
            services.AddSingleton(callerValidator);
        }
        else
        {
            services.AddScoped<ICallerValidator, CallerValidator>();
        }

        services.AddSingleton<IAuthorizationHandler, AsmPermissionHandler>();
        services.AddSingleton(_mockAsmService.Object);
        services.AddSingleton(_mockMisService.Object);
        await using var provider = services.BuildServiceProvider();

        DefaultHttpContext httpContext = new();
        httpContext.Items[HttpContextItemKeys.UserToken] = Token;
        if (positionId != null)
        {
            httpContext.Request.Headers[AsmPermissionHandler.PositionIdHeader] = positionId;
        }

        if (roleId != null)
        {
            httpContext.Request.Headers[AsmPermissionHandler.RoleIdHeader] = roleId;
        }

        if (divisionId != null)
        {
            httpContext.Request.Headers[AsmPermissionHandler.DivisionIdHeader] = divisionId;
        }

        if (departmentIds != null)
        {
            httpContext.Request.Headers[AsmPermissionHandler.DepartmentIdHeader] = departmentIds;
        }

        var principal = user ?? new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, UserId)], SsoAuthenticationDefaults.AuthenticationScheme));

        httpContext.User = principal;

        using var scope = provider.CreateScope();
        httpContext.RequestServices = scope.ServiceProvider;
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        return await authorization.AuthorizeAsync(principal, httpContext, requirements);
    }
}
