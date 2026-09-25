using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies stateless page commands through the CLI process boundary.</summary>
[TestFixture]
public sealed class PageCommandProcessWorkflowTests
{
    const string EditedTextEnvironmentVariable = "MEMORIA_NOTE_TEST_EDITOR_TEXT";

    /// <summary>
    /// Verifies new uses the selected notebook and rejects the same exact name later.
    /// </summary>
    [Test]
    public async Task New_WithSingleNotebook_PersistsPageAndRejectsDuplicateName()
    {
        using var harness = new CliProcessHarness();
        harness.SetEnvironmentVariable("EDITOR", harness.TestEditorExecutablePath);
        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Created body");
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");

        var notebookResult = await harness.RunAsync("create", "work.mnote");
        AssertSucceeded(notebookResult);
        AssertSucceeded(await harness.RunAsync("use", "work"));

        var createResult = await harness.RunAsync("new", "Roadmap");

        AssertSucceeded(createResult);
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var repository = new SqlitePageRepository(factory);
        var pages = await repository.ListPagesByHeadingAsync(
            notebookPath,
            "Roadmap",
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(createResult.StandardOutput, Does.Contain("Created page \"Roadmap\""));
            Assert.That(
                createResult.StandardOutput,
                Does.Contain(pages.Single().Guid.ToString("D")));
            Assert.That(pages.Single().Text, Is.EqualTo("Created body"));
        }

        var duplicateResult = await harness.RunAsync("new", "Roadmap");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(duplicateResult.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(duplicateResult.StandardOutput, Is.Empty);
            Assert.That(duplicateResult.StandardError, Does.Contain("already in use"));
            Assert.That(
                (await repository.ListPagesByHeadingAsync(
                    notebookPath,
                    "Roadmap",
                    CancellationToken.None)).Count,
                Is.EqualTo(1));
        }

        var differentCaseResult = await harness.RunAsync("new", "roadmap");
        AssertSucceeded(differentCaseResult);
        Assert.That(
            await repository.ListPagesByHeadingAsync(
                notebookPath,
                "roadmap",
                CancellationToken.None),
            Has.Count.EqualTo(1));
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
        var notebookResult = await harness.RunAsync("create", "work.mnote");
        AssertSucceeded(notebookResult);
        AssertSucceeded(await harness.RunAsync("use", "work"));

        var result = await harness.RunAsync("new", "Roadmap");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Storage));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.StartWith("Error: External editor"));
            Assert.That(result.StandardError, Does.Not.StartWith("Fatal:"));
        }

        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var repository = new SqlitePageRepository(factory);
        Assert.That(
            await repository.ListPagesByHeadingAsync(
                Path.Combine(harness.WorkingDirectory, "work.mnote"),
                "Roadmap",
                CancellationToken.None),
            Is.Empty);
    }

    /// <summary>
    /// Verifies edit resolves a Page ID and an invocation editor override with arguments.
    /// </summary>
    [Test]
    public async Task Edit_ByPageId_WithEditorOverride_UpdatesExistingPage()
    {
        using var harness = new CliProcessHarness();
        harness.SetEnvironmentVariable("EDITOR", harness.TestEditorExecutablePath);
        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Initial body");
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");

        AssertSucceeded(await harness.RunAsync("create", "work.mnote"));
        AssertSucceeded(await harness.RunAsync("use", "work"));
        AssertSucceeded(await harness.RunAsync("new", "Roadmap"));

        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var repository = new SqlitePageRepository(factory);
        var page = (await repository.ListPagesByHeadingAsync(
            notebookPath,
            "Roadmap",
            CancellationToken.None)).Single();
        harness.SetEnvironmentVariable(
            "EDITOR",
            Path.Combine(harness.TemporaryDirectory, "missing-editor"));
        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Edited body");

        var result = await harness.RunAsync(
            "edit",
            "--id",
            page.Guid.ToString("D"),
            "--editor",
            harness.TestEditorExecutablePath,
            "--editor-arg=--wait");

        AssertSucceeded(result);
        var updated = await repository.FindPageAsync(
            notebookPath,
            page.Guid,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StandardOutput, Does.Contain("updated successfully"));
            Assert.That(updated.Text, Is.EqualTo("Edited body"));
            Assert.That(updated.Guid, Is.EqualTo(page.Guid));
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
