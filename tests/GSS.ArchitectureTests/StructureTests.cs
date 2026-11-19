using System.Reflection;
using FluentAssertions;
using GSS.Api.Controllers;
using GSS.Business;
using GSS.Core.Entities;
using GSS.Infrastructure.Extensions;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// Structural rules: inheritance, interface implementations and encapsulation.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class StructureTests
{
    private static readonly Assembly CoreAssembly = typeof(EntityBase).Assembly;
    private static readonly Assembly BusinessAssembly = typeof(DependencyInjection).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(DataAccessExtensions).Assembly;
    private static readonly Assembly ApiAssembly = typeof(ApiControllerBase).Assembly;

    #region HTTP caching

    /// <summary>
    /// [ResponseCache] applies to every status (404s were cached) and max-age serves stale data after a write:
    /// cacheable GETs use [ClientCache] (private, no-cache + ETag, 200 only); [ResponseCache] is allowed only as NoStore.
    /// </summary>
    [Fact]
    public void ResponseCache_IsOnlyNoStore()
    {
        string[] offenders = ApiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => (Name: $"{t.Name}.{m.Name}", Attribute: m.GetCustomAttribute<ResponseCacheAttribute>()))
                .Append((Name: t.Name, Attribute: t.GetCustomAttribute<ResponseCacheAttribute>())))
            .Where(x => x.Attribute is { NoStore: false })
            .Select(x => x.Name)
            .ToArray();

        offenders.Should().BeEmpty(
            because: "use [ClientCache] to make a GET cacheable; [ResponseCache] caches every status and serves stale data");
    }

    #endregion

    #region Interface visibility

    [Fact]
    public void CoreInterfaces_ShouldBePublic()
    {
        var result = Types.InAssembly(CoreAssembly)
            .That().AreInterfaces()
            .Should().BePublic()
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "core interfaces define contracts consumed by other layers and must be publicly accessible");
    }

    [Fact]
    public void BusinessInterfaces_ShouldBePublic()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .That().AreInterfaces()
            .Should().BePublic()
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "business service interfaces are injected by the API layer and must be publicly accessible");
    }

    #endregion

    #region Repository structure

    [Fact]
    public void InfrastructureRepositories_ShouldDependOn_CoreInterfaces()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().ResideInNamespace("GSS.Infrastructure.Repositories")
            .And().AreClasses()
            .And().ArePublic()
            .And().AreNotAbstract()
            .Should().HaveDependencyOn("GSS.Core")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "repositories in GSS.Infrastructure must depend on GSS.Core contracts they implement");
    }

    [Fact]
    public void InfrastructureRepositories_ShouldBeSealed()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().ResideInNamespace("GSS.Infrastructure.Repositories")
            .And().AreClasses()
            .And().AreNotAbstract()
            .Should().BeSealed()
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "repository implementations are concrete leaf classes that should not be subclassed");
    }

    #endregion

    #region Entity inheritance

    [Fact]
    public void CoreEntities_ShouldInheritFrom_EntityBase()
    {
        IEnumerable<Type> allEntityTypes = Types.InAssembly(CoreAssembly)
            .That().ResideInNamespace("GSS.Core.Entities")
            .And().AreClasses()
            .And().AreNotAbstract()
            .GetTypes();

        foreach (Type entityType in allEntityTypes)
        {
            entityType.Should().BeDerivedFrom<EntityBase>(
                because: $"{entityType.Name} must inherit from EntityBase");
        }
    }

    #endregion

    #region Validator inheritance

    [Fact]
    public void BusinessValidators_ShouldInheritFrom_AbstractValidator()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .That().ResideInNamespace("GSS.Business.Validators")
            .And().AreClasses()
            .Should().Inherit(typeof(FluentValidation.AbstractValidator<>))
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all validators in GSS.Business.Validators must extend FluentValidation.AbstractValidator<T>");
    }

    [Fact]
    public void RequestModels_ShouldNotUse_DataAnnotationsValidation()
    {
        // FluentValidation is the only request validator; attributes would duplicate its errors.
        IEnumerable<Type> requestModelTypes = BusinessAssembly.GetTypes()
            .Where(t => t.Namespace == "GSS.Business.DTOs")
            .Concat(ApiAssembly.GetTypes().Where(t => t.Namespace == "GSS.Api.Models"));

        List<string> offenders = requestModelTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.IsDefined(typeof(System.ComponentModel.DataAnnotations.ValidationAttribute), inherit: true))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        offenders.Should().BeEmpty(
            because: "request DTOs are validated by FluentValidation only; move DataAnnotations rules into a validator");
    }

    #endregion

    #region Controller inheritance

    [Fact]
    public void ApiControllers_ShouldInheritFrom_ControllerBase()
    {
        var result = Types.InAssembly(ApiAssembly)
            .That().ResideInNamespace("GSS.Api.Controllers")
            .And().AreClasses()
            .And().AreNotAbstract()
            .Should().Inherit(typeof(ControllerBase))
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all concrete controllers must inherit from ControllerBase (via ApiControllerBase)");
    }

    #endregion

    #region External service structure

    [Fact]
    public void InfrastructureExternalServices_ShouldDependOn_CoreInterfaces()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().ResideInNamespace("GSS.Infrastructure.Services.External")
            .And().AreClasses()
            .Should().HaveDependencyOn("GSS.Core")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "external service implementations in GSS.Infrastructure must implement interfaces defined in GSS.Core");
    }

    #endregion

    #region Domain layer purity

    [Fact]
    public void CoreConcreteTypes_ShouldResideIn_CoreNamespace()
    {
        var result = Types.InAssembly(CoreAssembly)
            .That().AreClasses()
            .And().AreNotAbstract()
            .Should().ResideInNamespace("GSS.Core")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "all concrete types in GSS.Core must live within the GSS.Core namespace tree");
    }

    #endregion
}
