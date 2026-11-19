using System.Reflection;
using FluentAssertions;
using GSS.Api.Controllers;
using GSS.Core.Entities;
using GSS.Infrastructure.Extensions;
using Xunit;

namespace GSS.ArchitectureTests;

[Trait("Category", TestCategories.Unit)]
public class ScaffoldCompositionTests
{
    private static readonly Assembly ApiAssembly = typeof(ApiControllerBase).Assembly;
    private static readonly Assembly BusinessAssembly = typeof(Business.DependencyInjection).Assembly;
    private static readonly Assembly CoreAssembly = typeof(EntityBase).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(DataAccessExtensions).Assembly;
    private static readonly Assembly UtilAssembly = typeof(Util.ConfigurationKeys).Assembly;

    private static readonly string[] ForbiddenResourceNames =
    [
        "Address",
        "Article",
        "Color",
        "Customer",
        "Label",
        "Order",
        "OrderPosition",
        "Product",
        "Size",
        "Stock"
    ];

    [Fact]
    public void CommerceDomainTypes_ShouldBeAbsent()
    {
        IReadOnlyList<string> forbiddenTypes = SourceAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsCommerceType)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        forbiddenTypes.Should().BeEmpty(
            because: "the GSS scaffold must not ship the template's commerce domain");
    }

    [Fact]
    public void EnterpriseCapabilities_ShouldRemainAvailable()
    {
        Type[] requiredTypes =
        [
            typeof(SsoController),
            typeof(MisController),
            typeof(AsmController),
            typeof(Api.Authentication.SsoAuthenticationHandler),
            typeof(Api.Authorization.AsmAuthorizeAttribute),
            typeof(Api.Authorization.AsmPermissionHandler),
            typeof(Business.Services.Interfaces.ISsoService),
            typeof(Business.Services.Interfaces.IMisService),
            typeof(Business.Services.Interfaces.IAsmService),
            typeof(Business.Services.SsoService),
            typeof(Business.Services.MisService),
            typeof(Business.Services.AsmService),
            typeof(Core.Interfaces.Services.ISsoService),
            typeof(Core.Interfaces.Services.IMisService),
            typeof(Core.Interfaces.Services.IAsmService),
            typeof(Infrastructure.Services.External.SsoService),
            typeof(Infrastructure.Services.External.MisService),
            typeof(Infrastructure.Services.External.AsmService)
        ];

        requiredTypes.Should().OnlyContain(type => type.IsPublic);
    }

    [Fact]
    public void CleanArchitectureProjectReferences_ShouldMatchApprovedGraph()
    {
        Dictionary<string, string[]> expectedReferences = new(StringComparer.Ordinal)
        {
            ["GSS.Api"] = ["GSS.Business", "GSS.Core", "GSS.Infrastructure", "GSS.Util"],
            ["GSS.Business"] = ["GSS.Core", "GSS.Util"],
            ["GSS.Core"] = [],
            ["GSS.Infrastructure"] = ["GSS.Core", "GSS.Util"],
            ["GSS.Util"] = []
        };

        foreach (Assembly assembly in SourceAssemblies(includeUtil: true))
        {
            string assemblyName = assembly.GetName().Name!;
            IReadOnlyList<string> actualReferences = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => name.StartsWith("GSS.", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            actualReferences.Should().Equal(
                expectedReferences[assemblyName].OrderBy(name => name, StringComparer.Ordinal),
                because: $"{assemblyName} must follow the approved Clean Architecture project graph");
        }
    }

    private static IReadOnlyList<Assembly> SourceAssemblies(bool includeUtil = false)
    {
        List<Assembly> assemblies = [ApiAssembly, BusinessAssembly, CoreAssembly, InfrastructureAssembly];
        if (includeUtil)
        {
            assemblies.Add(UtilAssembly);
        }

        return assemblies;
    }

    private static bool IsCommerceType(Type type)
    {
        string candidate = type.Name;
        if (type.IsInterface && candidate.StartsWith('I'))
        {
            candidate = candidate[1..];
        }

        foreach (string prefix in new[] { "Create", "Update" })
        {
            if (candidate.StartsWith(prefix, StringComparison.Ordinal))
            {
                candidate = candidate[prefix.Length..];
                break;
            }
        }

        return ForbiddenResourceNames.Any(resource =>
            candidate.Equals(resource, StringComparison.Ordinal)
            || candidate.StartsWith(resource + "Controller", StringComparison.Ordinal)
            || candidate.StartsWith(resource + "Dto", StringComparison.Ordinal)
            || candidate.StartsWith(resource + "Repository", StringComparison.Ordinal)
            || candidate.StartsWith(resource + "Service", StringComparison.Ordinal)
            || candidate.StartsWith(resource + "Validator", StringComparison.Ordinal));
    }
}
