using System.Reflection;
using FluentAssertions;
using GSS.Business.DTOs;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>DTOs are immutable sealed records: values are set once, at construction or deserialization.</summary>
[Trait("Category", TestCategories.Unit)]
public class DtoShapeTests
{
    private static readonly Type[] Dtos = [.. typeof(SsoAuthResponse).Assembly.GetTypes()
        .Where(t => t.Namespace == typeof(SsoAuthResponse).Namespace && t.IsClass && !t.IsNested && t.IsPublic)];

    [Fact]
    public void Dtos_AreSealedRecords()
    {
        Dtos.Should().NotBeEmpty();
        Dtos.Where(t => !t.IsSealed || t.GetMethod("<Clone>$") is null).Select(t => t.Name)
            .Should().BeEmpty(because: "DTOs are sealed records");
    }

    [Fact]
    public void DtoProperties_AreInitOnly()
    {
        Dtos.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (Type: t, Property: p)))
            .Where(x => x.Property.SetMethod is { IsPublic: true } setter
                && !setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit"))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .Should().BeEmpty(because: "DTO properties use init, not set");
    }
}
