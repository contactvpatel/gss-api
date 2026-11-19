namespace GSS.ArchitectureTests;

/// <summary>Paths for tests that read repository files.</summary>
internal static class RepositoryPaths
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    /// <summary>Directory holding <c>GSS.slnx</c>.</summary>
    public static string Root => RootPath.Value;

    /// <summary>C# source files under <c>src/</c>, excluding build output.</summary>
    public static IEnumerable<string> SourceFiles()
    {
        return Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GSS.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("GSS.slnx not found above " + AppContext.BaseDirectory);
    }
}
