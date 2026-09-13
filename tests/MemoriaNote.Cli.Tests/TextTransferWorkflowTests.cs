using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies text transfer and completion workflows through the CLI process boundary.
/// </summary>
[TestFixture]
public sealed class TextTransferWorkflowTests
{
    /// <summary>
    /// Verifies that root and nested files survive import, listing, and export.
    /// </summary>
    [Test]
    public async Task ImportListAndExport_RoundTripsFilesAcrossProcesses()
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

        var listResult = await harness.RunAsync("list");

        AssertSucceeded(listResult);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listResult.StandardOutput, Does.Contain(rootName));
            Assert.That(listResult.StandardOutput, Does.Contain(nestedName));
            Assert.That(listResult.StandardOutput, Does.Contain("Total count: 2"));
        }

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

    /// <summary>
    /// Verifies that page and note completion emit line-oriented candidates.
    /// </summary>
    [Test]
    public async Task Completion_WritesMachineReadablePageAndNoteCandidates()
    {
        using var harness = new CliProcessHarness();
        var importDirectory = Path.Combine(harness.TemporaryDirectory, "import");
        Directory.CreateDirectory(importDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(importDirectory, "Meeting Notes.txt"),
            "Agenda");

        var importResult = await harness.RunAsync("import", importDirectory);

        AssertSucceeded(importResult);

        var pageCompletionResult = await harness.RunAsync(
            "list",
            "meet",
            "--completion");
        var noteCompletionResult = await harness.RunAsync(
            "work",
            "list",
            "--completion");

        AssertSucceeded(pageCompletionResult);
        AssertSucceeded(noteCompletionResult);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                GetOutputLines(pageCompletionResult.StandardOutput),
                Is.EqualTo(new[] { "meeting" }));
            Assert.That(
                GetOutputLines(noteCompletionResult.StandardOutput),
                Is.EqualTo(new[] { "note" }));
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

    static string[] GetOutputLines(string output)
    {
        return output.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();
    }
}
