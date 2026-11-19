using System.Reflection;
using FluentAssertions;
using GSS.Api.Controllers;
using GSS.ArchitectureTests.Fixtures.ApiLayer;
using GSS.Core.Entities;
using GSS.Core.Interfaces.Base;
using NetArchTest.Rules;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// API request handling reaches data and external services through Business services. The composition root may wire
/// Infrastructure, but controllers, filters and handlers may not depend on it directly.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ApiLayerDependencyTests
{
    private static readonly Assembly ApiAssembly = typeof(ApiControllerBase).Assembly;

    private static readonly string[] CompositionRoot =
    [
        "GSS.Api.Extensions",
        "GSS.Api.HealthChecks",
        "GSS.Api.HostedServices",
    ];

    [Fact]
    public void Rules_ScanControllersFiltersAndHandlers_NotTheCompositionRoot()
    {
        IReadOnlyList<string> scanned = [.. Select(ApiAssembly, "GSS.Api", CompositionRoot).GetTypes().Select(type => type.FullName!)];

        scanned.Should().Contain([
            typeof(AsmController).FullName!,
            typeof(GSS.Api.Filters.ValidationFilter).FullName!,
            typeof(GSS.Api.Authorization.AsmPermissionHandler).FullName!,
            typeof(GSS.Api.Authentication.SsoAuthenticationHandler).FullName!,
        ]);
        scanned.Should().NotContain(type => CompositionRoot.Any(root => type.StartsWith(root + ".", StringComparison.Ordinal)));
    }

    [Fact]
    public void ApiCode_DoesNotDependOnInfrastructure()
    {
        Offenders(ApiAssembly, "GSS.Api", CompositionRoot, ["GSS.Infrastructure"])
            .Should().BeEmpty(because: "API request handling must call Business services instead of Infrastructure directly");
    }

    [Fact]
    public void ApiCode_DoesNotUseRepositoriesUnitOfWorkOrCoreExternalServices()
    {
        Offenders(ApiAssembly, "GSS.Api", CompositionRoot, DataAccessTypes())
            .Should().BeEmpty(because: "controllers, filters and handlers go through Business services");
    }

    [Fact]
    public void Rule_FlagsEachForbiddenDependency_AndAllowsTheUserContext()
    {
        Assembly fixtures = typeof(GenericRepositoryController).Assembly;
        const string fixturesNamespace = "GSS.ArchitectureTests.Fixtures.ApiLayer";

        Offenders(fixtures, fixturesNamespace, [], DataAccessTypes()).Should().BeEquivalentTo(
            $"{fixturesNamespace}.{nameof(GenericRepositoryController)}",
            $"{fixturesNamespace}.{nameof(UnitOfWorkController)}",
            $"{fixturesNamespace}.{nameof(CoreExternalServiceController)}");
        Offenders(fixtures, fixturesNamespace, [], ["GSS.Infrastructure"]).Should().Equal(
            $"{fixturesNamespace}.{nameof(InfrastructureController)}");
    }

    private static string[] DataAccessTypes()
    {
        return
        [
            typeof(IRepository<>).FullName!,
            typeof(IUnitOfWork).FullName!,
            "GSS.Core.Interfaces.Services",
        ];
    }

    private static IReadOnlyList<string> Offenders(
        Assembly assembly,
        string rootNamespace,
        string[] allowedNamespaces,
        string[] forbidden)
    {
        TestResult result = Select(assembly, rootNamespace, allowedNamespaces).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        return result.FailingTypeNames ?? [];
    }

    private static PredicateList Select(Assembly assembly, string rootNamespace, string[] allowedNamespaces)
    {
        PredicateList types = Types.InAssembly(assembly).That().ResideInNamespace(rootNamespace);
        foreach (string allowed in allowedNamespaces)
        {
            types = types.And().DoNotResideInNamespace(allowed);
        }

        return types;
    }
}
