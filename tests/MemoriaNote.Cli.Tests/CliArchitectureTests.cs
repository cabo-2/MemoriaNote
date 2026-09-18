using System.Xml.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Guards the CLI boundary after the TUI and reactive stack removal.</summary>
[TestFixture]
public sealed class CliArchitectureTests
{
    static readonly string[] ForbiddenSourceReferences =
    {
        "Terminal.Gui",
        "ReactiveUI",
        "ReactiveMarbles",
        "Fody",
        "MemoriaNoteViewModel",
        "ITerminalUi"
    };

    static readonly string[] RemovedFiles =
    {
        "cli/MemoriaNoteViewModel.cs",
        "cli/Commands/ITerminalUi.cs",
        "cli/Editors/EditorMode.cs",
        "cli/Editors/IPageEditorWorkflow.cs",
        "cli/Editors/PageEditorNameDocument.cs",
        "cli/Editors/PageEditorWorkflow.cs",
        "cli/SearchOptionDisplay.cs",
        "cli/FodyWeavers.xml",
        "cli/FodyWeavers.xsd"
    };

    /// <summary>Verifies that production CLI source has no removed framework references.</summary>
    [Test]
    public void ProductionSources_DoNotReferenceRemovedTuiTypes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var cliDirectory = Path.Combine(repositoryRoot, "cli");
        var sourceFiles = Directory.EnumerateFiles(
            cliDirectory,
            "*.cs",
            SearchOption.AllDirectories)
            .Where(file => !IsBuildArtifact(cliDirectory, file));
        var violations = sourceFiles
            .SelectMany(file => ForbiddenSourceReferences
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

    /// <summary>Verifies that production and CLI test projects omit removed packages.</summary>
    [Test]
    public void Projects_DoNotReferenceRemovedPackages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPaths = new[]
        {
            Path.Combine(repositoryRoot, "cli", "mn.csproj"),
            Path.Combine(
                repositoryRoot,
                "tests",
                "MemoriaNote.Cli.Tests",
                "MemoriaNote.Cli.Tests.csproj")
        };
        var violations = projectPaths
            .SelectMany(projectPath => XDocument.Load(projectPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => new
                {
                    ProjectPath = projectPath,
                    PackageName = (string?)element.Attribute("Include")
                }))
            .Where(reference => reference.PackageName != null &&
                ForbiddenSourceReferences.Any(forbidden =>
                    reference.PackageName.Contains(
                        forbidden,
                        StringComparison.OrdinalIgnoreCase)))
            .Select(reference =>
                $"{Path.GetRelativePath(repositoryRoot, reference.ProjectPath)}: " +
                reference.PackageName)
            .ToList();

        Assert.That(
            violations,
            Is.Empty,
            () => string.Join(Environment.NewLine, violations));
    }

    /// <summary>Verifies that restored transitive dependency graphs omit removed packages.</summary>
    [Test]
    public void RestoredDependencies_DoNotContainRemovedPackages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var assetPaths = new[]
        {
            Path.Combine(repositoryRoot, "cli", "obj", "project.assets.json"),
            Path.Combine(
                repositoryRoot,
                "tests",
                "MemoriaNote.Cli.Tests",
                "obj",
                "project.assets.json")
        };
        var violations = assetPaths
            .SelectMany(assetPath => ReadPackageNames(assetPath)
                .Where(packageName => ForbiddenSourceReferences.Any(forbidden =>
                    packageName.Contains(
                        forbidden,
                        StringComparison.OrdinalIgnoreCase)))
                .Select(packageName =>
                    $"{Path.GetRelativePath(repositoryRoot, assetPath)}: {packageName}"))
            .ToList();

        Assert.That(
            violations,
            Is.Empty,
            () => string.Join(Environment.NewLine, violations));
    }

    /// <summary>Verifies that removed source and weaving files are absent.</summary>
    [Test]
    public void RemovedTuiFiles_AreAbsent()
    {
        var repositoryRoot = FindRepositoryRoot();
        var remainingFiles = RemovedFiles
            .Select(relativePath => Path.Combine(
                repositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .Select(file => Path.GetRelativePath(repositoryRoot, file))
            .ToList();

        Assert.That(
            remainingFiles,
            Is.Empty,
            () => string.Join(Environment.NewLine, remainingFiles));
    }

    static bool IsBuildArtifact(string cliDirectory, string path)
    {
        return Path.GetRelativePath(cliDirectory, path)
                .Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(directory => directory == "bin" || directory == "obj");
    }

    static IEnumerable<string> ReadPackageNames(string assetPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetPath));
        return document.RootElement
            .GetProperty("libraries")
            .EnumerateObject()
            .Where(library => library.Value.GetProperty("type").GetString() == "package")
            .Select(library => library.Name.Split('/')[0])
            .ToArray();
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
