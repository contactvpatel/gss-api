using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// Build settings as MSBuild evaluates them, not as the files read: a package's build props (CodeStyle.NET sets
/// LangVersion=latest, AnalysisLevel=latest-default) override Directory.Build.props, so the pins live in
/// Directory.Build.targets.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public partial class BuildSettingsTests
{
    // One MSBuild evaluation per project, shared by the theories (each costs a dotnet process).
    private static readonly ConcurrentDictionary<string, JsonElement> Evaluations = new(StringComparer.Ordinal);

    public static TheoryData<string> Projects()
    {
        TheoryData<string> projects = [];
        foreach (string project in new[] { "src", "tests" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, dir), "*.csproj", SearchOption.AllDirectories))
            .Select(p => Path.GetRelativePath(RepositoryPaths.Root, p))
            .Order(StringComparer.Ordinal))
        {
            projects.Add(project);
        }

        return projects;
    }

    /// <summary>net10.0 compiles as C# 14.0 with analysis level 10.0; an SDK upgrade alone changes neither.</summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void Project_UsesTheLanguageAndAnalysisLevelOfItsTargetFramework(string project)
    {
        var properties = Evaluate(project).GetProperty("Properties");
        var framework = TargetFrameworkRegex().Match(properties.GetProperty("TargetFramework").GetString()!);
        framework.Success.Should().BeTrue();
        int major = int.Parse(framework.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture);

        properties.GetProperty("LangVersion").GetString().Should().Be($"{major + 4}.0",
            because: "C# version = .NET major + 4; set it in Directory.Build.targets, which package props cannot override");
        properties.GetProperty("EffectiveAnalysisLevel").GetString().Should().Be($"{major}.0",
            because: "a floating analysis level adds rules on an SDK upgrade, and warnings are errors");
    }

    /// <summary>
    /// CodeStyle.NET's rules reach the compiler only as a global analyzer config: a normal .editorconfig shipped in a
    /// package applies to files under the NuGet cache, i.e. to none of ours, and every style rule silently stops.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void Project_GetsCodeStyleRulesAsAGlobalAnalyzerConfig(string project)
    {
        string[] codeStyleConfigs = [.. Evaluate(project).GetProperty("Items").GetProperty("EditorConfigFiles").EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!)
            .Where(path => path.Contains("codestyle.net", StringComparison.OrdinalIgnoreCase) && File.Exists(path))];

        codeStyleConfigs.Should().NotBeEmpty(because: "every project references CodeStyle.NET");
        codeStyleConfigs.Should().OnlyContain(path => File.ReadLines(path).Any(line => line.Trim() == "is_global = true"),
            because: "a non-global .editorconfig from a package applies to no project file");
    }

    /// <summary>Directory.Build.props sets these once; a project that repeats one overrides it, so a central TFM bump misses it.</summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void Project_DoesNotRedeclareCentralSettings(string project)
    {
        string[] redeclared = [.. System.Xml.Linq.XDocument.Load(Path.Combine(RepositoryPaths.Root, project)).Descendants()
            .Select(e => e.Name.LocalName)
            .Where(name => name is "TargetFramework" or "TargetFrameworks" or "Nullable" or "ImplicitUsings")];

        redeclared.Should().BeEmpty(because: "Directory.Build.props owns the target framework, nullable and implicit usings");
    }

    /// <summary>The Docker build copies root build files one by one, so a new one must be listed.</summary>
    [Fact]
    public void Dockerfile_CopiesEveryRootBuildFile()
    {
        string dockerfile = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "Dockerfile"));
        string[] rootBuildFiles = [.. Directory.EnumerateFiles(RepositoryPaths.Root, "Directory.*")
            .Select(Path.GetFileName)
            .Where(f => f!.EndsWith(".props", StringComparison.Ordinal) || f.EndsWith(".targets", StringComparison.Ordinal))
            .Select(f => f!)];

        rootBuildFiles.Should().Contain(["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"]);
        rootBuildFiles.Where(f => !dockerfile.Contains($"COPY [\"{f}\", \"./\"]", StringComparison.Ordinal))
            .Should().BeEmpty(because: "a build file missing from the image silently changes the container build");
    }

    /// <summary>
    /// COPY src/ runs after the in-container restore, so without .dockerignore the host's obj/ (project.assets.json
    /// with host package paths) overwrites it and publish --no-restore fails. It must be committed, not gitignored.
    /// </summary>
    [Fact]
    public void Dockerignore_IsCommittedAndExcludesBuildOutputAndSecrets()
    {
        string dockerignorePath = Path.Combine(RepositoryPaths.Root, ".dockerignore");
        File.Exists(dockerignorePath).Should().BeTrue(because: "a fresh clone must build the image");

        string[] rules = ReadIgnoreRules(dockerignorePath);
        rules.Should().Contain(["**/bin", "**/obj", ".git", ".env"]);

        ReadIgnoreRules(Path.Combine(RepositoryPaths.Root, ".gitignore")).Should().NotContain(".dockerignore",
            because: "a gitignored .dockerignore never reaches a clone or CI");
    }

    private static string[] ReadIgnoreRules(string path)
    {
        return [.. File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];
    }

    /// <summary>The project's properties and items as MSBuild evaluates them (<c>Properties</c>, <c>Items</c>).</summary>
    private static JsonElement Evaluate(string project) => Evaluations.GetOrAdd(project, EvaluateUncached);

    private static JsonElement EvaluateUncached(string project)
    {
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = RepositoryPaths.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project);
        foreach (string property in new[] { "TargetFramework", "LangVersion", "EffectiveAnalysisLevel" })
        {
            start.ArgumentList.Add($"-getProperty:{property}");
        }

        start.ArgumentList.Add("-getItem:EditorConfigFiles");

        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, because: output + error);

        using JsonDocument json = JsonDocument.Parse(output);
        return json.RootElement.Clone();
    }

    [GeneratedRegex(@"^net(?<major>\d+)\.0$")]
    private static partial Regex TargetFrameworkRegex();
}
