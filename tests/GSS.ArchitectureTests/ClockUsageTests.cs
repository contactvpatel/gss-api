using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>"Now" comes only from the injected <see cref="TimeProvider"/>, so expiry and audit logic is testable.</summary>
[Trait("Category", TestCategories.Unit)]
public partial class ClockUsageTests
{
    [Fact]
    public void Source_NeverReadsTheWallClockDirectly()
    {
        string[] offenders = [.. RepositoryPaths.SourceFiles()
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && WallClockRegex().IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(RepositoryPaths.Root, l.File)}:{l.Line}")];

        offenders.Should().BeEmpty(because: "read the time from the injected TimeProvider, never DateTime/DateTimeOffset.UtcNow/Now/Today");
    }

    [GeneratedRegex(@"\b(DateTime|DateTimeOffset)\.(UtcNow|Now|Today)\b")]
    private static partial Regex WallClockRegex();
}
