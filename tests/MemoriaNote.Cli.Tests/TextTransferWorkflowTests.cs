using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies text transfer workflows through the CLI process boundary.
/// </summary>
[TestFixture]
public sealed class TextTransferWorkflowTests
{
    /// <summary>
    /// Verifies that root and nested files survive import and export.
    /// </summary>
    [Test]
    public async Task ImportAndExport_RoundTripsFilesAcrossProcesses()
    {
        using var harness = new CliProcessHarness();
        var importDirectory = Path.Combine(harness.TemporaryDirectory, "import");
        var nestedImportDirectory = Path.Combine(importDirectory, "archive");
        var exportDirectory = Path.Combine(harness.TemporaryDirectory, "export");
        Directory.CreateDirectory(nestedImportDirectory);
        Directory.CreateDirectory(exportDirectory);

        const string rootName = "Meeting Notes";
        const string rootText = "Agenda and decisions";
        const string nestedName = "Release Checklist";
        const string nestedText = "Build, test, and publish";
        await File.WriteAllTextAsync(
            Path.Combine(importDirectory, rootName + ".txt"),
            rootText);
        await File.WriteAllTextAsync(
            Path.Combine(nestedImportDirectory, nestedName + ".txt"),
            nestedText);

        var importResult = await harness.RunAsync(
            "import",
            importDirectory,
            "--recursive");

        AssertSucceeded(importResult);
        Assert.That(importResult.StandardOutput, Does.Contain("Import completed"));

        var exportResult = await harness.RunAsync("export", exportDirectory);

        AssertSucceeded(exportResult);
        Assert.That(exportResult.StandardOutput, Does.Contain("Export completed"));

        var rootExportPath = Path.Combine(exportDirectory, rootName + ".txt");
        var nestedExportPath = Path.Combine(
            exportDirectory,
            "archive",
            nestedName + ".txt");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                await File.ReadAllTextAsync(rootExportPath),
                Is.EqualTo(rootText));
            Assert.That(
                await File.ReadAllTextAsync(nestedExportPath),
                Is.EqualTo(nestedText));
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
        }
    }

    static void AssertSucceeded(CliProcessResult result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardError, Is.Empty);
        }
    }

}
