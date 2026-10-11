using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies text transfer through the production CLI entry point.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class TextTransferIntegrationTests
{
    /// <summary>Verifies multiple invocations persist imported flat files.</summary>
    [Test]
    public async Task NotebookImport_PersistsFlatFilesAcrossInvocations()
    {
        using var harness = new CliInProcessHarness();
        var importDirectory = Path.Combine(harness.TemporaryDirectory, "import");
        Directory.CreateDirectory(importDirectory);
        AssertSucceeded(await harness.RunAsync("create", "transfer"));
        AssertSucceeded(await harness.RunAsync("use", "transfer"));

        const string rootName = "Meeting Notes";
        const string rootText = "Agenda and decisions";
        const string secondName = "Release Checklist";
        const string secondText = "Build, test, and publish";
        await File.WriteAllTextAsync(
            Path.Combine(importDirectory, rootName + ".txt"),
            rootText);
        await File.WriteAllTextAsync(
            Path.Combine(importDirectory, secondName + ".txt"),
            secondText);

        var importResult = await harness.RunAsync(
            "notebooks",
            "import",
            importDirectory);

        AssertSucceeded(importResult);
        Assert.That(importResult.StandardOutput, Does.Contain("Import completed:"));
        Assert.That(importResult.StandardOutput, Does.Contain("created=2"));

        var repository = new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
        var notebookPath = Path.Combine(harness.WorkingDirectory, "transfer.mnote");
        var importedRoot = await repository.FindPageAsync(
            notebookPath,
            rootName,
            1,
            CancellationToken.None);
        var importedSecond = await repository.FindPageAsync(
            notebookPath,
            secondName,
            1,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(importedRoot?.Text, Is.EqualTo(rootText));
            Assert.That(importedSecond?.Text, Is.EqualTo(secondText));
            Assert.That(File.Exists(notebookPath), Is.True);
        }
    }

    static void AssertSucceeded(CliInProcessResult result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardError, Is.Empty);
        }
    }
}
