using System.IO;

internal static class RepositoryTestPaths
{
    internal const string RepositoryRootVariable = "AETHER_REPOSITORY_ROOT";

    public static string FindFile(params string[] path) =>
        FindFile(Environment.GetEnvironmentVariable(RepositoryRootVariable),
            Environment.GetEnvironmentVariable("GITHUB_WORKSPACE"),
            Environment.CurrentDirectory, AppContext.BaseDirectory, path);

    internal static string FindFile(string? configuredRoot, string? githubWorkspace,
        string currentDirectory, string applicationDirectory, params string[] path)
    {
        var repositoryRoot = ResolveRoot(configuredRoot, githubWorkspace, currentDirectory, applicationDirectory);
        var candidate = Path.GetFullPath(Path.Combine([repositoryRoot, .. path]));
        if (!File.Exists(candidate))
            throw new FileNotFoundException(
                $"Repository file not found beneath resolved root '{repositoryRoot}': " +
                string.Join(Path.DirectorySeparatorChar, path));
        return candidate;
    }

    internal static string ResolveRoot(string? configuredRoot, string? githubWorkspace,
        string currentDirectory, string applicationDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var explicitRoot = Path.GetFullPath(configuredRoot);
            if (!IsRepositoryRoot(explicitRoot))
                throw new DirectoryNotFoundException(
                    $"{RepositoryRootVariable} is not an AETHER repository root: {explicitRoot}");
            return explicitRoot;
        }

        var directCandidates = new[] { githubWorkspace };
        var ancestorStarts = new[] { currentDirectory, applicationDirectory };
        foreach (var candidate in directCandidates
                     .Concat(ancestorStarts.SelectMany(GetAncestors))
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => Path.GetFullPath(value!))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            if (IsRepositoryRoot(candidate!))
                return candidate!;

        throw new DirectoryNotFoundException(
            "AETHER repository root not found. Set AETHER_REPOSITORY_ROOT explicitly when running a test host " +
            "outside the checkout.");
    }

    private static IEnumerable<string> GetAncestors(string start)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(start));
             directory is not null;
             directory = directory.Parent)
            yield return directory.FullName;
    }

    private static bool IsRepositoryRoot(string path) =>
        File.Exists(Path.Combine(path, "SystemCockpit.slnx")) &&
        File.Exists(Path.Combine(path, "src", "SystemCockpit", "SystemCockpit.csproj")) &&
        Directory.Exists(Path.Combine(path, "tests", "SystemCockpit.Tests"));
}
