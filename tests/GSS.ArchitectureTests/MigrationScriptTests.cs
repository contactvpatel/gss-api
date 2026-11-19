using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using GSS.Infrastructure.DbUp.Core;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// DbUp journals by file name, so scripts are append-only: never rename, delete or edit one.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public partial class MigrationScriptTests
{
    private const string Prefix = "GSS.Infrastructure.DbUp.";

    // Append new scripts at the end of their folder; never rename or remove an entry, and never change a hash: an
    // applied script that changes is not re-run, so environments drift. Fix a script with a new one. A new script's
    // hash is in the failure message of EmbeddedScripts_MatchRecordedHashes.
    private static readonly (string Name, string Sha256)[] KnownScripts =
    [
        // The scaffold ships one generic script (Idempotency-Key storage); feature migrations are appended after it.
        ("Migrations.20261119-140000-GSS-001-Idempotency-Key.sql", "f0c889e4ee74cfd6fb99400e0c91ce18e9cdabd01cc37f0a662fb10860d3d02e"),
    ];

    private static readonly Assembly InfrastructureAssembly = typeof(DatabaseMigrationInitFilter).Assembly;

    [Fact]
    public void EmbeddedScripts_MatchAppendOnlyList()
    {
        string[] embedded = [.. InfrastructureAssembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..])];

        embedded.Should().BeEquivalentTo(KnownScripts.Select(s => s.Name),
            because: "applied scripts must never be renamed or deleted, and new scripts must be added to KnownScripts");
    }

    [Fact]
    public void EmbeddedScripts_MatchRecordedHashes()
    {
        string[] changed = [.. KnownScripts
            .Select(s => (s.Name, s.Sha256, Actual: ContentHash(Prefix + s.Name)))
            .Where(s => !string.Equals(s.Sha256, s.Actual, StringComparison.Ordinal))
            .Select(s => $"{s.Name}: recorded {s.Sha256}, actual {s.Actual}")];

        changed.Should().BeEmpty(
            because: "an applied script is never re-run, so editing it leaves environments different; add a new script instead");
    }

    [Theory]
    [InlineData("SELECT 1;\nSELECT 2;\n")]
    [InlineData("SELECT 1;\r\nSELECT 2;\r\n")]
    [InlineData("\uFEFFSELECT 1;\r\nSELECT 2;\n")]
    public void ContentHash_IgnoresLineEndingsAndBom(string text)
    {
        Hash(Encoding.UTF8.GetBytes(text)).Should().Be(Hash(Encoding.UTF8.GetBytes("SELECT 1;\nSELECT 2;\n")),
            because: "a checkout with other line endings (git autocrlf) is the same script");
    }

    /// <summary>SHA-256 of the script with any BOM dropped and CRLF as LF, so the checkout's line endings don't matter.</summary>
    private static string ContentHash(string resourceName)
    {
        using var stream = InfrastructureAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded script '{resourceName}' not found.");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return Hash(buffer.ToArray());
    }

    private static string Hash(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void KnownScripts_AreAscendingPerFolder()
    {
        foreach (var folder in KnownScripts.Select(s => s.Name).GroupBy(s => s[..s.LastIndexOf('.', s.Length - 5)]))
        {
            folder.Should().BeInAscendingOrder(StringComparer.Ordinal,
                because: $"a new script in {folder.Key} needs a later timestamp than the scripts already applied");
        }
    }

    [Fact]
    public void ScriptNames_FollowConvention()
    {
        foreach (string script in KnownScripts.Select(s => s.Name))
        {
            string fileName = script[(script.LastIndexOf('.', script.Length - 5) + 1)..];
            ScriptNamePattern().IsMatch(fileName).Should().BeTrue(
                because: $"'{fileName}' must look like yyyyMMdd-HHmmss-TICKET-NNN-Description.sql");
        }
    }

    [GeneratedRegex(@"^\d{8}-\d{6}-[A-Z]+-\d{3}-[A-Za-z0-9-]+\.sql$")]
    private static partial Regex ScriptNamePattern();
}
