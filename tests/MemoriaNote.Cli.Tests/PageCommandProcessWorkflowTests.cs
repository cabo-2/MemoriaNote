using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies stateless page commands through the CLI process boundary.</summary>
[TestFixture]
public sealed class PageCommandProcessWorkflowTests
{
    const string EditedTextEnvironmentVariable = "MEMORIA_NOTE_TEST_EDITOR_TEXT";

    /// <summary>Verifies new, list, and edit with a real external editor process.</summary>
    [Test]
    public async Task NewListAndEdit_PersistsPageAcrossProcesses()
    {
        using var harness = new CliProcessHarness();
        harness.SetEnvironmentVariable("EDITOR", harness.TestEditorExecutablePath);
        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Created body");

        var createResult = await harness.RunAsync("new", "Roadmap");

        AssertSucceeded(createResult);
        Assert.That(createResult.StandardOutput, Does.Contain("created successfully"));

        var listResult = await harness.RunAsync("list");

        AssertSucceeded(listResult);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listResult.StandardOutput, Does.Contain("Roadmap"));
            Assert.That(listResult.StandardOutput, Does.Contain("Total count: 1"));
        }

        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Edited body");
        var editResult = await harness.RunAsync("edit", "Roadmap");

        AssertSucceeded(editResult);
        Assert.That(editResult.StandardOutput, Does.Contain("updated successfully"));

        var exportDirectory = Path.Combine(harness.TemporaryDirectory, "export");
        Directory.CreateDirectory(exportDirectory);
        var exportResult = await harness.RunAsync("export", exportDirectory);

        AssertSucceeded(exportResult);
        Assert.That(
            await File.ReadAllTextAsync(Path.Combine(exportDirectory, "Roadmap.txt")),
            Is.EqualTo("Edited body"));
    }

    /// <summary>Verifies that a missing editor executable is an external I/O failure.</summary>
    [Test]
    public async Task New_WhenEditorCannotStart_ReturnsStorageFailure()
    {
        using var harness = new CliProcessHarness();
        var missingEditor = Path.Combine(
            harness.TemporaryDirectory,
            "missing-editor");
        harness.SetEnvironmentVariable("EDITOR", missingEditor);

        var result = await harness.RunAsync("new", "Roadmap");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Storage));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.StartWith("Error: External editor"));
            Assert.That(result.StandardError, Does.Not.StartWith("Fatal:"));
        }

        var listResult = await harness.RunAsync("list");
        AssertSucceeded(listResult);
        Assert.That(listResult.StandardOutput, Does.Contain("Total count: 0"));
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
