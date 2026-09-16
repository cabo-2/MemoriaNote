using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>
/// Guards Domain and Application production sources from framework dependencies.
/// </summary>
[TestFixture]
public sealed class SourceDependencyTests
{
    static readonly string[] ForbiddenReferences =
    {
        "ReactiveUI",
        "DynamicData",
        "ReactiveObject",
        "ReactiveCommand",
        "ObservableAsPropertyHelper",
        "ObservableCollectionExtended",
        "[Reactive]",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.Sqlite"
    };

    /// <summary>
    /// Verifies that UI and persistence frameworks do not enter Domain or Application source.
    /// </summary>
    [Test]
    public void DomainAndApplicationSources_DoNotReferenceUiOrPersistenceFrameworks()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreDirectory = Path.Combine(repositoryRoot, "core");
        var sourceFiles = Directory
            .EnumerateFiles(
                Path.Combine(coreDirectory, "Domain"),
                "*.cs",
                SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(coreDirectory, "Application"),
                "*.cs",
                SearchOption.AllDirectories))
            .Append(Path.Combine(coreDirectory, "Workspace.cs"));
        var violations = sourceFiles
            .SelectMany(file => ForbiddenReferences
                .Where(reference => File.ReadAllText(file).Contains(
                    reference,
                    StringComparison.Ordinal))
                .Select(reference =>
                    $"{Path.GetRelativePath(repositoryRoot, file)}: {reference}"))
            .ToList();

        Assert.That(
            violations,
            Is.Empty,
            () => string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies that core production namespaces match their source directories.
    /// </summary>
    [Test]
    public void CoreSources_UseNamespaceMatchingDirectory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreDirectory = Path.Combine(repositoryRoot, "core");
        var violations = Directory
            .EnumerateFiles(coreDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(coreDirectory, file)
                .Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(directory => directory == "bin" || directory == "obj"))
            .Select(file =>
            {
                var relativeDirectory = Path.GetRelativePath(
                    coreDirectory,
                    Path.GetDirectoryName(file)!);
                var expectedNamespace = relativeDirectory == "."
                    ? "MemoriaNote"
                    : "MemoriaNote." + relativeDirectory
                        .Replace(Path.DirectorySeparatorChar, '.')
                        .Replace(Path.AltDirectorySeparatorChar, '.');
                var namespaceLine = File.ReadLines(file)
                    .FirstOrDefault(line => line.TrimStart().StartsWith(
                        "namespace ",
                        StringComparison.Ordinal));
                var actualNamespace = namespaceLine == null
                    ? null
                    : namespaceLine.Trim()["namespace ".Length..].Trim().TrimEnd(';');
                return actualNamespace == expectedNamespace
                    ? null
                    : $"{Path.GetRelativePath(repositoryRoot, file)}: " +
                        $"expected {expectedNamespace}, found {actualNamespace ?? "<missing>"}";
            })
            .Where(violation => violation != null)
            .ToList();

        Assert.That(
            violations,
            Is.Empty,
            () => string.Join(Environment.NewLine, violations));
    }

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MemoriaNote.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
