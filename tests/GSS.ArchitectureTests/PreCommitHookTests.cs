using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>The formatting hook validates the proposed commit without changing either the index or working tree.</summary>
[Trait("Category", TestCategories.Unit)]
public class PreCommitHookTests
{
    [Fact]
    public void Hook_VerifiesTheStagedCSharpPath_WithoutRestagingUnstagedLines()
    {
        string temp = Path.Combine(Path.GetTempPath(), "gss-pre-commit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "scripts"));

        try
        {
            Run(temp, "git", "init", "--quiet").ExitCode.Should().Be(0);
            File.Copy(
                Path.Combine(RepositoryPaths.Root, "scripts", "pre-commit-hook.ps1"),
                Path.Combine(temp, "scripts", "pre-commit-hook.ps1"));
            File.WriteAllText(Path.Combine(temp, "GSS.slnx"), "<Solution />");

            const string staged = "namespace Demo;\npublic class Sample { }\n";
            const string workingTree = staged + "public class Unstaged { }\n";
            string source = Path.Combine(temp, "Sample.cs");
            File.WriteAllText(source, staged);
            Run(temp, "git", "add", "Sample.cs").ExitCode.Should().Be(0);
            File.WriteAllText(source, workingTree);

            string calls = Path.Combine(temp, "dotnet-calls.txt");
            string hook = Path.Combine(temp, "scripts", "pre-commit-hook.ps1");
            string command = $"$log = '{PsLiteral(calls)}'; "
                + "function global:dotnet { Add-Content -LiteralPath $log -Value ($args -join '|'); $global:LASTEXITCODE = 0 }; "
                + $"& '{PsLiteral(hook)}'; exit $LASTEXITCODE";

            ProcessResult result = Run(temp, "pwsh", "-NoProfile", "-NonInteractive", "-Command", command);

            result.ExitCode.Should().Be(0, result.Error + result.Output);
            Run(temp, "git", "show", ":Sample.cs").Output.Should().Be(staged,
                because: "the hook must not add unstaged lines to a partial stage");
            File.ReadAllText(source).Should().Be(workingTree,
                because: "the hook is a read-only verifier");

            string formatCall = File.ReadLines(calls).Single(line => line.StartsWith("format|", StringComparison.Ordinal));
            formatCall.Should().Contain("|--verify-no-changes|").And.Contain("|--include|Sample.cs");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static string PsLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static ProcessResult Run(string workingDirectory, string fileName, params string[] arguments)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
