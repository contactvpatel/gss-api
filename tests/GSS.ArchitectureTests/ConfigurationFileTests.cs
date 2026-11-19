using System.Reflection;
using Caching.NET.Options;
using FluentAssertions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>Configuration comes from appsettings.json only; there are no environment-specific files.</summary>
[Trait("Category", TestCategories.Unit)]
public class ConfigurationFileTests
{
    [Fact]
    public void Source_HasNoEnvironmentSpecificAppSettings()
    {
        string src = Path.Combine(RepositoryPaths.Root, "src");

        string[] extra = [.. Directory.EnumerateFiles(src, "appsettings.*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(src, f))];

        extra.Should().BeEmpty(
            because: "settings live in appsettings.json (overridden by environment variables); AppSettings:Environment is the only environment switch");
    }

    /// <summary>
    /// An [EnableRateLimiting] policy missing from config fails only when a request reaches the endpoint, so every name
    /// the Api uses must be defined in ApiPipeline:RateLimiting:Policies.
    /// </summary>
    [Fact]
    public void RateLimitPolicies_UsedByEndpoints_AreConfigured()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryPaths.Root, "src", "GSS.Api", "appsettings.json"))
            .Build();
        string?[] configured = [.. configuration.GetSection("ApiPipeline:RateLimiting:Policies").GetChildren().Select(p => p["Name"])];

        string[] used = [.. typeof(GSS.Api.Controllers.SsoController).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods().Cast<MemberInfo>().Append(t))
            .SelectMany(m => m.GetCustomAttributes<EnableRateLimitingAttribute>())
            .Select(a => a.PolicyName!)
            .Distinct()];

        used.Should().NotBeEmpty().And.BeSubsetOf(configured);
    }

    /// <summary>Database calls appear as spans: Npgsql's own "Npgsql" ActivitySource is exported.</summary>
    [Fact]
    public void Tracing_ExportsNpgsqlSpans()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryPaths.Root, "src", "GSS.Api", "appsettings.json"))
            .Build();

        configuration.GetValue<bool>("OpenTelemetryOptions:EnableNpgsqlInstrumentation").Should().BeTrue();
    }

    /// <summary>
    /// Services call the cache without try/catch: Caching.NET logs and absorbs Redis, serialization and backplane
    /// failures, so a cache outage never fails a request or repeats an external call. That holds only while the
    /// ThrowOn* switches stay off, which UseDevelopmentDefaults() would turn on.
    /// </summary>
    [Fact]
    public void Caching_DegradesCacheFailuresInsteadOfThrowing()
    {
        string root = RepositoryPaths.Root;
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src", "GSS.Api", "appsettings.json"))
            .Build();
        CachingOptions options = new();
        configuration.GetSection("CacheOptions").Bind(options);

        options.Resilience.ThrowOnDistributedCacheErrors.Should().BeFalse();
        options.Resilience.ThrowOnSerializationErrors.Should().BeFalse();
        options.Resilience.ThrowOnBackplaneErrors.Should().BeFalse();

        string[] offenders = [.. RepositoryPaths.SourceFiles()
            .Where(f => File.ReadAllText(f) is string code
                && (code.Contains("UseDevelopmentDefaults", StringComparison.Ordinal) || code.Contains("ThrowOn", StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(root, f))];
        offenders.Should().BeEmpty(because: "cache failures must degrade, not throw");
    }
}
