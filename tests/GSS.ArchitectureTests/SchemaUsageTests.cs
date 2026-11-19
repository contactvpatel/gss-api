using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// Repository SQL names only the service's own schema: a type or table in <c>public</c> is shared with every other
/// service in the database.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public partial class SchemaUsageTests
{
    [Fact]
    public void RepositorySql_NeverNamesThePublicSchema()
    {
        string repositories = Path.Combine(RepositoryPaths.Root, "src", "GSS.Infrastructure", "Repositories");

        string[] offenders = [.. RepositoryPaths.SourceFiles()
            .Where(f => f.StartsWith(repositories, StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => PublicSchemaRegex().IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(RepositoryPaths.Root, l.File)}:{l.Line}")];

        offenders.Should().BeEmpty(because: "qualify types and tables with gss, never public");
    }

    [Theory]
    [InlineData("CAST(@Gender AS public.gender)", true)]
    [InlineData(@"FROM ""public"".""colors""", true)]
    [InlineData("CAST(@Gender AS gss.gender)", false)]
    [InlineData("public sealed class WidgetRepository", false)]
    public void Rule_FlagsOnlyThePublicSchema(string line, bool expected)
    {
        PublicSchemaRegex().IsMatch(line).Should().Be(expected);
    }

    [GeneratedRegex(@"\bpublic\s*\.|""public""\s*\.", RegexOptions.IgnoreCase)]
    private static partial Regex PublicSchemaRegex();
}
