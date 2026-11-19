using System.Reflection;
using FluentAssertions;
using GSS.Api.Controllers;
using GSS.Business;
using GSS.Core.Entities;
using GSS.Infrastructure.Extensions;
using GSS.Util.Models;
using NetArchTest.Rules;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// Dependencies point inward: Business -> Core, Util; Infrastructure -> Core, Util; Api -> Business, Infrastructure, Util
/// (Api -> Infrastructure only for DI registration). Core and Util reference no other layer; Business and Infrastructure
/// never reference each other; nothing references Api. Checked on emitted types (NetArchTest) and on the csproj
/// references, which also catch a reference no code uses yet.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DependencyTests
{
    private static readonly Assembly CoreAssembly = typeof(EntityBase).Assembly;
    private static readonly Assembly BusinessAssembly = typeof(DependencyInjection).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(DataAccessExtensions).Assembly;
    private static readonly Assembly ApiAssembly = typeof(ApiControllerBase).Assembly;
    private static readonly Assembly UtilAssembly = typeof(DbConnectionModel).Assembly;

    #region Forbidden inward dependencies

    [Fact]
    public void Core_ShouldNotDependOn_BusinessLayer()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot().HaveDependencyOn("GSS.Business")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Core is the innermost layer and must not reference GSS.Business");
    }

    [Fact]
    public void Core_ShouldNotDependOn_InfrastructureLayer()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot().HaveDependencyOn("GSS.Infrastructure")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Core is the innermost layer and must not reference GSS.Infrastructure");
    }

    [Fact]
    public void Core_ShouldNotDependOn_ApiLayer()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot().HaveDependencyOn("GSS.Api")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Core is the innermost layer and must not reference GSS.Api");
    }

    [Fact]
    public void Business_ShouldNotDependOn_InfrastructureLayer()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .ShouldNot().HaveDependencyOn("GSS.Infrastructure")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Business must not depend on GSS.Infrastructure to preserve dependency inversion");
    }

    [Fact]
    public void Business_ShouldNotDependOn_ApiLayer()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .ShouldNot().HaveDependencyOn("GSS.Api")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Business must not depend on the presentation layer GSS.Api");
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOn_BusinessLayer()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot().HaveDependencyOn("GSS.Business")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Infrastructure implements Core's interfaces; a Business type (DTO, service) there inverts the layering");
    }

    [Fact]
    public void Core_ShouldNotDependOn_Util()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot().HaveDependencyOn("GSS.Util")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Core is the innermost layer and references no other project");
    }

    /// <summary>Util (options, configuration keys, security helpers) is shared by every layer, so it can use none of them.</summary>
    [Theory]
    [InlineData("GSS.Core")]
    [InlineData("GSS.Business")]
    [InlineData("GSS.Infrastructure")]
    [InlineData("GSS.Api")]
    public void Util_ShouldNotDependOn_AnyLayer(string layer)
    {
        Types.InAssembly(UtilAssembly).GetTypes().Should().NotBeEmpty(because: "the rule must scan real types");

        var result = Types.InAssembly(UtilAssembly)
            .ShouldNot().HaveDependencyOn(layer)
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: $"GSS.Util is referenced by the layers, so it must not reference {layer}");
    }

    /// <summary>
    /// The csproj graph, which NetArchTest cannot see: a ProjectReference no code uses yet compiles away, and is how a
    /// layering mistake starts. Each project may reference only these (fewer is fine).
    /// </summary>
    [Theory]
    [InlineData("GSS.Core")]
    [InlineData("GSS.Util")]
    [InlineData("GSS.Business", "GSS.Core", "GSS.Util")]
    [InlineData("GSS.Infrastructure", "GSS.Core", "GSS.Util")]
    [InlineData("GSS.Api", "GSS.Business", "GSS.Infrastructure", "GSS.Util")]
    public void ProjectReferences_FollowTheLayerGraph(string project, params string[] allowed)
    {
        string csproj = Path.Combine(RepositoryPaths.Root, "src", project, project + ".csproj");
        string[] referenced = [.. System.Xml.Linq.XDocument.Load(csproj).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')))];

        referenced.Should().BeSubsetOf(allowed, because: $"{project} may reference only {(allowed.Length == 0 ? "no other project" : string.Join(", ", allowed))}");
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOn_ApiLayer()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot().HaveDependencyOn("GSS.Api")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Infrastructure must not depend on the presentation layer GSS.Api");
    }

    [Theory]
    [MemberData(nameof(ProviderFreeAssemblies))]
    public void OnlyInfrastructure_ReferencesTheDatabaseProvider(string layer)
    {
        var assembly = layer switch
        {
            "Core" => CoreAssembly,
            "Business" => BusinessAssembly,
            "Api" => ApiAssembly,
            _ => UtilAssembly
        };

        Types.InAssembly(assembly).ShouldNot().HaveDependencyOn("Npgsql").GetResult().FailingTypeNames.Should().BeNullOrEmpty(
            because: $"GSS.{layer} must not use Npgsql types; database access belongs to GSS.Infrastructure");
        assembly.GetReferencedAssemblies().Select(a => a.Name).Should().NotContain("Npgsql",
            because: $"GSS.{layer} must not reference the database provider");
    }

    // Api too: ApiExceptionHandler classifies database failures through ADO.NET's DbException, not Npgsql types.
    public static TheoryData<string> ProviderFreeAssemblies => new() { "Core", "Business", "Util", "Api" };

    [Theory]
    [MemberData(nameof(ResilienceFreeAssemblies))]
    public void OnlyInfrastructure_UsesTheResilienceLibraries(string layer)
    {
        var assembly = layer switch
        {
            "Core" => CoreAssembly,
            "Business" => BusinessAssembly,
            "Api" => ApiAssembly,
            _ => UtilAssembly
        };

        foreach (string library in new[] { "Polly", "HttpResilience.NET" })
        {
            Types.InAssembly(assembly).ShouldNot().HaveDependencyOn(library).GetResult().FailingTypeNames.Should().BeNullOrEmpty(
                because: $"GSS.{layer} must not use {library} types; Infrastructure turns outbound failures into ExternalServiceException");
        }
    }

    public static TheoryData<string> ResilienceFreeAssemblies => new() { "Core", "Business", "Api", "Util" };

    #endregion

    #region Required inward dependencies

    [Fact]
    public void Business_ShouldDependOn_CoreLayer()
    {
        var dependentTypes = Types.InAssembly(BusinessAssembly)
            .That().HaveDependencyOn("GSS.Core")
            .GetTypes();

        dependentTypes.Should().NotBeEmpty(
            because: "GSS.Business services and validators should reference GSS.Core types");
    }

    [Fact]
    public void Infrastructure_ShouldDependOn_CoreLayer()
    {
        var dependentTypes = Types.InAssembly(InfrastructureAssembly)
            .That().HaveDependencyOn("GSS.Core")
            .GetTypes();

        dependentTypes.Should().NotBeEmpty(
            because: "GSS.Infrastructure repositories must reference GSS.Core entities and interfaces");
    }

    [Fact]
    public void Api_ShouldDependOn_BusinessLayer()
    {
        var dependentTypes = Types.InAssembly(ApiAssembly)
            .That().HaveDependencyOn("GSS.Business")
            .GetTypes();

        dependentTypes.Should().NotBeEmpty(
            because: "GSS.Api controllers must orchestrate through GSS.Business services");
    }

    #endregion
}
