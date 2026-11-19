using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using GSS.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>Integer ids are rejected by routing (<c>{id:int:min(1)}</c>), so a bad id never reaches binding or the database.</summary>
[Trait("Category", TestCategories.Unit)]
public partial class RouteConstraintTests
{
    [Fact]
    public void IntegerRouteParameters_HaveIntMin1Constraint()
    {
        string[] offenders = [.. typeof(ApiControllerBase).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Where(a => a.Template != null).Select(a => (Method: m, Template: a.Template!)))
            .SelectMany(x => RouteParameterRegex().Matches(x.Template).Select(p => (x.Method, x.Template, Name: p.Groups["name"].Value, Constraints: p.Groups["constraints"].Value)))
            .Where(p => p.Method.GetParameters().Any(a => a.Name == p.Name && a.ParameterType == typeof(int))
                && !p.Constraints.Contains(":int:min(1)", StringComparison.Ordinal))
            .Select(p => $"{p.Method.DeclaringType!.Name}.{p.Method.Name}: {p.Template}")];

        offenders.Should().BeEmpty(because: "an int route value must be constrained with :int:min(1)");
    }

    [GeneratedRegex(@"\{(?<name>\w+)(?<constraints>[^}]*)\}")]
    private static partial Regex RouteParameterRegex();
}
