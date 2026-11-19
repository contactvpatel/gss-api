using FluentAssertions;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// NetArchTest results are asserted through <c>FailingTypeNames</c>, so a failure names the types that broke the rule;
/// <c>IsSuccessful</c> only says "expected True, found False".
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class NetArchTestAssertionTests
{
    [Fact]
    public void ArchitectureTests_AssertFailingTypeNames_NotIsSuccessful()
    {
        // Built by concatenation so this file does not match itself.
        string forbidden = "." + "IsSuccessful";
        string directory = Path.Combine(RepositoryPaths.Root, "tests", "GSS.ArchitectureTests");

        string[] offenders = [.. Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && File.ReadAllText(f).Contains(forbidden, StringComparison.Ordinal))
            .Select(Path.GetFileName)!];

        offenders.Should().BeEmpty(
            because: "assert result.FailingTypeNames.Should().BeNullOrEmpty(...) so a failure lists the offending types");
    }
}
