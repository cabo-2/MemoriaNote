using System.Reflection;

namespace MemoriaNote.Performance.Tests;

internal static class TestPaths
{
    internal static string TestTargetExecutable => GetAssemblyMetadataPath(
        "PerformanceTestTargetPath");

    internal static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null &&
                !File.Exists(Path.Combine(directory.FullName, "MemoriaNote.sln")))
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? throw new DirectoryNotFoundException(
                "The repository root could not be found from the test output directory.");
        }
    }

    static string GetAssemblyMetadataPath(string key)
    {
        var path = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The '{key}' test artifact was not built before the test run.",
                path);
        }
        return path;
    }
}
