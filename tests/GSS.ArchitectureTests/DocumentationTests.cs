using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>Repository documentation stays navigable, current and free of source-template residue.</summary>
[Trait("Category", TestCategories.Unit)]
public partial class DocumentationTests
{
    private static readonly string[] TopicDirectories = ["architecture", "guides", "standards", "testing"];

    private static readonly string[] ApprovedTopics =
    [
        "architecture/allowed-hosts.md",
        "architecture/api-pipeline.md",
        "architecture/asm-authorization.md",
        "architecture/dapper-hybrid-approach.md",
        "architecture/error-contract.md",
        "architecture/exception-handling.md",
        "architecture/health-checks.md",
        "architecture/idempotency-analysis.md",
        "architecture/opentelemetry-integration.md",
        "architecture/project-structure.md",
        "architecture/resilience.md",
        "architecture/security-configuration-comparison.md",
        "architecture/service-registration-layering.md",
        "architecture/sso-authentication.md",
        "architecture/validation-filter.md",
        "guides/database-migration-postgresql-to-sqlserver.md",
        "guides/dbup-migrations.md",
        "guides/docker-compose.md",
        "guides/httpclient-factory.md",
        "guides/hybrid-caching.md",
        "guides/mapster-code-generation-guide.md",
        "guides/performance-optimization-guide.md",
        "guides/sonarqube-setup-guide.md",
        "guides/swagger-ui.md",
        "standards/api-response-formats.md",
        "standards/api-versioning-guidelines.md",
        "standards/cancellation-token-guidelines.md",
        "standards/collection-types-guidelines.md",
        "standards/database-connection-settings-guidelines.md",
        "standards/logging-strategy-recommendations.md",
        "standards/naming-guidelines.md",
        "standards/security-scanning.md",
        "standards/utc-time-guidelines.md",
        "standards/xml-comments-guidelines.md",
        "testing/architecture-testing-guide.md",
        "testing/testing-comprehensive-guide.md"
    ];

    [Fact]
    public void DocsInventory_ContainsOnlyApprovedTopics()
    {
        TopicFiles().Select(RelativeToDocs).Should().Equal(ApprovedTopics);
    }

    [Fact]
    public void DocsIndex_ListsEveryTopic()
    {
        string index = File.ReadAllText(Path.Combine(RepositoryPaths.Root, ".docs", "README.md"));
        string[] incorrect =
        [
            .. ApprovedTopics.Where(topic =>
                Regex.Matches(index, $@"\({Regex.Escape(topic)}\)", RegexOptions.CultureInvariant).Count == 0)
        ];

        incorrect.Should().BeEmpty("every approved topic must have an index link");
    }

    [Fact]
    public void TopicDocs_FollowTheDocumentTemplate()
    {
        List<string> problems = [];
        foreach (string file in TopicFiles())
        {
            string relative = RelativeToRoot(file);
            string[] lines = File.ReadAllLines(file);
            IReadOnlyList<string> prose = ProseLines(lines);
            string[] sections = [.. prose.Where(line => line.StartsWith("## ", StringComparison.Ordinal))];

            if (lines.Length == 0 || !lines[0].StartsWith("# ", StringComparison.Ordinal))
            {
                problems.Add($"{relative}: first line must be an H1");
            }

            if (!lines.Take(5).Contains("[← Back to README](../README.md)", StringComparer.Ordinal))
            {
                problems.Add($"{relative}: canonical back link must be within the first five lines");
            }

            if (!sections.Contains("## Overview", StringComparer.Ordinal))
            {
                problems.Add($"{relative}: missing Overview");
            }

            if (sections.Length > 3 && !sections.Contains("## Table of Contents", StringComparer.Ordinal))
            {
                problems.Add($"{relative}: four or more sections require a Table of Contents");
            }

            if (sections.Length == 0 || sections[^1] is not ("## References" or "## Related Documentation"))
            {
                problems.Add($"{relative}: final section must be References or Related Documentation");
            }
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void Standards_StateExternalGuidancePrecedence()
    {
        string[] missing =
        [
            .. Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, ".docs", "standards"), "*.md")
                .Where(file => !File.ReadAllText(file).Contains("takes precedence", StringComparison.OrdinalIgnoreCase))
                .Select(RelativeToRoot)
                .Order(StringComparer.Ordinal)
        ];

        missing.Should().BeEmpty("each mandatory standard must say what happens when external guidance conflicts");
    }

    [Fact]
    public void MarkdownFences_AreBalanced()
    {
        string[] unbalanced =
        [
            .. CurrentMarkdownFiles()
                .Where(file => File.ReadLines(file).Count(line => line.TrimStart().StartsWith("```", StringComparison.Ordinal)) % 2 != 0)
                .Select(RelativeToRoot)
        ];

        unbalanced.Should().BeEmpty();
    }

    [Fact]
    public void LocalMarkdownLinks_Resolve()
    {
        List<string> missing = [];
        foreach (string file in CurrentMarkdownFiles())
        {
            string text = File.ReadAllText(file);
            foreach (Match match in MarkdownLinkRegex().Matches(text))
            {
                string target = match.Groups["target"].Value.Trim('<', '>');
                if (target.StartsWith('#') || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                    || target.Contains("path/to/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string path = Uri.UnescapeDataString(target.Split('#')[0]);
                if (path.Length == 0)
                {
                    continue;
                }

                string resolved = Path.GetFullPath(path, Path.GetDirectoryName(file)!);
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    missing.Add($"{RelativeToRoot(file)} -> {target}");
                }
            }
        }

        missing.Should().BeEmpty();
    }

    [Fact]
    public void CurrentDocumentation_RepositoryLinksResolve()
    {
        List<string> missing = [];
        foreach (string file in CurrentMarkdownFiles().Where(IsCurrentDocumentation))
        {
            string text = File.ReadAllText(file);
            foreach (Match match in InlineCodeRegex().Matches(text))
            {
                string candidate = match.Groups["code"].Value.Trim().TrimEnd('.', ',', ':', ';');
                if (!RepositoryPathRegex().IsMatch(candidate) || candidate.Contains('{') || candidate.Contains('<')
                    || candidate.Contains('*') || candidate.Contains("...", StringComparison.Ordinal))
                {
                    continue;
                }

                candidate = LineSuffixRegex().Replace(candidate, string.Empty);
                string resolved = Path.Combine(RepositoryPaths.Root, candidate.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    missing.Add($"{RelativeToRoot(file)} -> {candidate}");
                }
            }
        }

        missing.Should().BeEmpty("paths presented as current repository locations must exist");
    }

    [Fact]
    public void TrackedMarkdown_HasNoSourceTemplateCommerceResidue()
    {
        string[] offenders =
        [
            .. CurrentMarkdownFiles()
                .Where(file => File.ReadAllText(file).Contains("web" + "shop", StringComparison.OrdinalIgnoreCase))
                .Select(RelativeToRoot)
        ];

        offenders.Should().BeEmpty();
    }

    private static string[] TopicFiles()
    {
        return
        [
            .. TopicDirectories
                .SelectMany(directory => Directory.EnumerateFiles(
                    Path.Combine(RepositoryPaths.Root, ".docs", directory), "*.md", SearchOption.TopDirectoryOnly))
                .Order(StringComparer.Ordinal)
        ];
    }

    private static string[] CurrentMarkdownFiles()
    {
        return
        [
            .. Directory.EnumerateFiles(RepositoryPaths.Root, "*.md", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}.superpowers{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
        ];
    }

    private static bool IsCurrentDocumentation(string file)
    {
        string relative = RelativeToRoot(file);
        return relative is "README.md" or "CLAUDE.md" or "scripts/README.md" or "src/GSS.Business/Mappings/README.md"
            || relative.StartsWith(".docs/", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ProseLines(IReadOnlyList<string> lines)
    {
        List<string> prose = [];
        bool fenced = false;
        foreach (string line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced)
            {
                prose.Add(line);
            }
        }

        return prose;
    }

    private static string RelativeToDocs(string file)
    {
        return Path.GetRelativePath(Path.Combine(RepositoryPaths.Root, ".docs"), file).Replace('\\', '/');
    }

    private static string RelativeToRoot(string file)
    {
        return Path.GetRelativePath(RepositoryPaths.Root, file).Replace('\\', '/');
    }

    [GeneratedRegex(@"\[[^\]]*\]\((?<target>[^)\s]+)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"`(?<code>[^`\r\n]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"^(?:src|tests|scripts|\.docs)/.+(?:\.cs|\.csproj|\.json|\.xml|\.sql|\.ps1|\.md|\.slnx|\.yaml|\.yml|\.props|\.targets)$", RegexOptions.IgnoreCase)]
    private static partial Regex RepositoryPathRegex();

    [GeneratedRegex(@":\d+(?:-\d+)?$")]
    private static partial Regex LineSuffixRegex();
}
