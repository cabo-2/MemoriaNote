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
            .Append(Path.Combine(coreDirectory, "Workgroup.cs"));
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
