using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// Calls of one request run one at a time, except where a file is listed here: a Unit of Work has one connection, so
/// two of its database calls in parallel fail (at run time, IDapperTransactionManager.EnterCommand), and a parallel
/// service call can reach the database without showing it. A new parallel call needs a reviewed entry.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public partial class ParallelCallTests
{
    /// <summary>Files allowed to start work in parallel, and why it never touches the database.</summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["src/GSS.Business/Services/MisService.cs"] = "MIS HTTP call and a cached department list; no database",
    };

    [Fact]
    public void Source_StartsParallelWorkOnlyWhereReviewed()
    {
        string[] offenders = [.. RepositoryPaths.SourceFiles()
            .Select(f => (File: f, Relative: Path.GetRelativePath(RepositoryPaths.Root, f).Replace('\\', '/')))
            .Where(f => !Allowed.ContainsKey(f.Relative))
            .SelectMany(f => File.ReadLines(f.File).Select((line, i) => (f.Relative, Line: i + 1, Text: line)))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && ParallelRegex().IsMatch(l.Text))
            .Select(l => $"{l.Relative}:{l.Line}")];

        offenders.Should().BeEmpty(
            because: "a Unit of Work's calls share one connection; await each in turn, or list the file in Allowed with why it never reaches the database");
    }

    [Fact]
    public void AllowedFiles_Exist()
    {
        Allowed.Keys.Where(f => !File.Exists(Path.Combine(RepositoryPaths.Root, f))).Should().BeEmpty(because: "remove entries for deleted files");
    }

    // Call syntax only, so docs and messages may name them.
    [GeneratedRegex(@"\b(Task\.(WhenAll|WhenAny|WhenEach)|Parallel\.(For|ForEach|ForEachAsync|Invoke))\s*[(<]")]
    private static partial Regex ParallelRegex();
}
