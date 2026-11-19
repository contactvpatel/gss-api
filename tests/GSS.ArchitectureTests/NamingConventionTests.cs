using System.Reflection;
using FluentAssertions;
using GSS.Api.Controllers;
using GSS.Business;
using GSS.Core.Entities;
using GSS.Infrastructure.Extensions;
using NetArchTest.Rules;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// Naming conventions across all layers.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class NamingConventionTests
{
    private static readonly Assembly CoreAssembly = typeof(EntityBase).Assembly;
    private static readonly Assembly BusinessAssembly = typeof(DependencyInjection).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(DataAccessExtensions).Assembly;
    private static readonly Assembly ApiAssembly = typeof(ApiControllerBase).Assembly;

    #region Interface prefix convention

    [Fact]
    public void CoreInterfaces_ShouldStartWith_I()
    {
        var result = Types.InAssembly(CoreAssembly)
            .That().AreInterfaces()
            .Should().HaveNameStartingWith("I")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all interfaces in GSS.Core must follow the 'I' prefix convention");
    }

    [Fact]
    public void BusinessInterfaces_ShouldStartWith_I()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .That().AreInterfaces()
            .Should().HaveNameStartingWith("I")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all interfaces in GSS.Business must follow the 'I' prefix convention");
    }

    [Fact]
    public void InfrastructureInterfaces_ShouldStartWith_I()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().AreInterfaces()
            .Should().HaveNameStartingWith("I")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all interfaces in GSS.Infrastructure must follow the 'I' prefix convention");
    }

    #endregion

    #region Service naming

    [Fact]
    public void BusinessServices_ShouldEndWith_Service()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .That().ResideInNamespace("GSS.Business.Services")
            .And().AreClasses()
            .And().AreNotAbstract()
            .Should().HaveNameEndingWith("Service")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all concrete service classes in GSS.Business.Services must end with 'Service'");
    }

    [Fact]
    public void BusinessServiceInterfaces_ShouldResideIn_InterfacesNamespace()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .That().AreInterfaces()
            .And().HaveNameEndingWith("Service")
            .Should().ResideInNamespace("GSS.Business.Services.Interfaces")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all service interfaces in GSS.Business must reside in the GSS.Business.Services.Interfaces namespace");
    }

    #endregion

    #region Repository naming

    [Fact]
    public void InfrastructureRepositories_ShouldEndWith_Repository()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().ResideInNamespace("GSS.Infrastructure.Repositories")
            .And().AreClasses()
            .And().ArePublic()
            .And().AreNotAbstract()
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all public repository classes in GSS.Infrastructure.Repositories must end with 'Repository'");
    }

    #endregion

    #region Validator naming

    [Fact]
    public void BusinessValidators_ShouldEndWith_Validator()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .That().ResideInNamespace("GSS.Business.Validators")
            .And().AreClasses()
            .Should().HaveNameEndingWith("Validator")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all validator classes in GSS.Business.Validators must end with 'Validator'");
    }

    #endregion

    #region Controller naming

    [Fact]
    public void ApiControllers_ShouldEndWith_Controller()
    {
        var result = Types.InAssembly(ApiAssembly)
            .That().ResideInNamespace("GSS.Api.Controllers")
            .And().AreClasses()
            .And().AreNotAbstract()
            .Should().HaveNameEndingWith("Controller")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all concrete controller classes in GSS.Api.Controllers must end with 'Controller'");
    }

    #endregion

    #region External client naming

    [Fact]
    public void InfrastructureExternalServices_ShouldEndWith_Service()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().ResideInNamespace("GSS.Infrastructure.Services.External")
            .And().AreClasses()
            .Should().HaveNameEndingWith("Service")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all external service classes in GSS.Infrastructure.Services.External must end with 'Service'");
    }

    #endregion

    #region DTO namespace residency

    [Fact]
    public void BusinessDTOs_ShouldResideIn_DTOsNamespace()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .That().HaveNameEndingWith("Dto")
            .Should().ResideInNamespace("GSS.Business.DTOs")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all DTO classes in GSS.Business must reside in the GSS.Business.DTOs namespace");
    }

    #endregion
}
