using System.Reflection;
using FluentAssertions;
using GSS.Api.Controllers;
using GSS.Business;
using GSS.Infrastructure.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// Injected dependencies are required. The container passes null for an unregistered optional parameter, so a
/// missing registration (e.g. the transaction manager) would silently change behaviour instead of failing.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ConstructorInjectionTests
{
    [Fact]
    public void InjectedDependencies_HaveNoDefaultValue()
    {
        Assembly[] assemblies =
        [
            typeof(ApiControllerBase).Assembly,
            typeof(DependencyInjection).Assembly,
            typeof(DataAccessExtensions).Assembly
        ];

        List<string> violations = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass)
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(c => !c.IsPrivate)
                .SelectMany(c => c.GetParameters().Where(p => p.HasDefaultValue && IsService(p.ParameterType))
                    .Select(p => $"{t.Name}: {p.ParameterType.Name} {p.Name}")))
            .Distinct()
            .ToList();

        string.Join(Environment.NewLine, violations).Should().BeEmpty("each of these is resolved as null when unregistered");
    }

    private static bool IsService(Type type)
    {
        return type.IsInterface || type.IsAbstract || type == typeof(TimeProvider) || typeof(ILoggerFactory).IsAssignableFrom(type);
    }
}
