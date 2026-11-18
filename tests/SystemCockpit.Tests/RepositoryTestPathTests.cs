using System.IO;

internal static class RepositoryTestPathTests
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool valid, string message)
        {
            assertions++;
            if (!valid) throw new InvalidOperationException(message);
        }

        var root = Path.Combine(Path.GetTempPath(), "AetherRepositoryRoot-" + Guid.NewGuid().ToString("N"));
        var isolated = Path.Combine(root, "isolated-test-host");
        var output = Path.Combine(root, "checkout", "tests", "SystemCockpit.Tests", "bin", "Release", "net10.0-windows");
        var checkout = Path.Combine(root, "checkout");
        var work = Path.Combine(root, "other-working-directory");
        try
        {
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(isolated);
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(checkout, "SystemCockpit.slnx"), "<Solution/>");
            Directory.CreateDirectory(Path.Combine(checkout, "src", "SystemCockpit"));
            Directory.CreateDirectory(Path.Combine(checkout, "tests", "SystemCockpit.Tests"));
            Directory.CreateDirectory(Path.Combine(checkout, "build"));
            File.WriteAllText(Path.Combine(checkout, "src", "SystemCockpit", "SystemCockpit.csproj"), "<Project/>");
            var installer = Path.Combine(checkout, "build", "AetherCockpit.iss");
            File.WriteAllText(installer, "[Setup]");

            Check(PathsEqual(RepositoryTestPaths.FindFile(checkout, null, work, isolated,
                    "build", "AetherCockpit.iss"), installer),
                "Explicit repository root must resolve from an isolated test host");
            Check(PathsEqual(RepositoryTestPaths.FindFile(null, checkout, isolated, isolated,
                    "build", "AetherCockpit.iss"), installer),
                "GitHub workspace must resolve independently of the test output directory");
            Check(PathsEqual(RepositoryTestPaths.FindFile(null, null, output, isolated,
                    "build", "AetherCockpit.iss"), installer),
                "Repository-root execution and nested working directories must be supported");
            Check(PathsEqual(RepositoryTestPaths.FindFile(null, null, work, output,
                    "build", "AetherCockpit.iss"), installer),
                "Build-output execution must resolve ancestors of the test assembly directory");
            Check(Throws<DirectoryNotFoundException>(() => RepositoryTestPaths.FindFile(
                    work, null, output, isolated, "build", "AetherCockpit.iss")),
                "An invalid explicit repository root must fail instead of selecting an unrelated checkout");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        return assertions;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return true; }
        return false;
    }
}
