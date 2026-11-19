using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>global.json, the Dockerfile build image and the Jenkinsfile must use the same .NET SDK version.</summary>
[Trait("Category", TestCategories.Unit)]
public partial class SdkVersionAlignmentTests
{
    [Fact]
    public void DockerfileSdkImage_MatchesGlobalJson()
    {
        string dockerfile = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "Dockerfile"));
        var match = DockerSdkImageRegex().Match(dockerfile);

        match.Success.Should().BeTrue(because: "the Dockerfile builds from mcr.microsoft.com/dotnet/sdk:<version>");
        match.Groups["version"].Value.Should().Be(GlobalJsonSdkVersion(),
            because: "the SDK image tag must equal global.json's sdk.version, or restore fails with 'compatible .NET SDK not found'");
    }

    [Fact]
    public void JenkinsfileDotnetVersion_MatchesGlobalJson()
    {
        string jenkinsfile = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "Jenkinsfile"));
        var match = JenkinsDotnetVersionRegex().Match(jenkinsfile);

        match.Success.Should().BeTrue(because: "the Jenkinsfile sets dotnetVersion: '<version>[-<os>]'");
        match.Groups["version"].Value.Should().Be(GlobalJsonSdkVersion(),
            because: "the Jenkins SDK image must equal global.json's sdk.version");
    }

    private static string GlobalJsonSdkVersion()
    {
        using JsonDocument globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.Root, "global.json")));
        return globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()
            ?? throw new InvalidOperationException("global.json has no sdk.version");
    }

    // Version part of the tag only: "10.0.404" from "sdk:10.0.404" or "sdk:10.0.404-noble".
    [GeneratedRegex(@"^FROM\s+mcr\.microsoft\.com/dotnet/sdk:(?<version>\d+\.\d+\.\d+)", RegexOptions.Multiline)]
    private static partial Regex DockerSdkImageRegex();

    // "10.0.404" from dotnetVersion: '10.0.404-alpine3.24'.
    [GeneratedRegex(@"dotnetVersion:\s*'(?<version>\d+\.\d+\.\d+)")]
    private static partial Regex JenkinsDotnetVersionRegex();
}
