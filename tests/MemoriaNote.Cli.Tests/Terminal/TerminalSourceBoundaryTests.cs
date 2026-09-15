using NUnit.Framework;

namespace MemoriaNote.Cli.Tests.Terminal;

/// <summary>Guards the source boundary between command handlers and Terminal.Gui.</summary>
[TestFixture]
public sealed class TerminalSourceBoundaryTests
{
    /// <summary>Verifies that command sources do not reference the concrete terminal adapter.</summary>
    [Test]
    public void CommandSources_DoNotReferenceTerminalGuiOrItsAdapterNamespace()
    {
        var repositoryRoot = FindRepositoryRoot();
        var commandsDirectory = Path.Combine(repositoryRoot, "cli", "Commands");
        var forbiddenReferences = new[]
        {
            "using Terminal.Gui",
            "Terminal.Gui.",
            "MemoriaNote.Cli.Terminal"
        };
        var violations = Directory
            .EnumerateFiles(commandsDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => forbiddenReferences
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

    /// <summary>Verifies that every direct Terminal.Gui source dependency is isolated.</summary>
    [Test]
    public void TerminalGuiSources_AreLocatedUnderTerminalDirectory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var cliDirectory = Path.Combine(repositoryRoot, "cli");
        var terminalDirectory = Path.Combine(cliDirectory, "Terminal") +
            Path.DirectorySeparatorChar;
        var violations = Directory
            .EnumerateFiles(cliDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains("using Terminal.Gui", StringComparison.Ordinal) ||
                    source.Contains("Terminal.Gui.", StringComparison.Ordinal);
            })
            .Where(file => !file.StartsWith(
                terminalDirectory,
                StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(repositoryRoot, file))
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
