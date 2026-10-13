using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the CLI-to-editor child-process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class ExternalEditorProcessTests
{
    const string EditedTextEnvironmentVariable = "MEMORIA_NOTE_TEST_EDITOR_TEXT";

    /// <summary>Verifies new launches the configured editor process and persists its output.</summary>
    [Test]
    public async Task New_ConfiguredEditorProcess_CreatesPage()
    {
        using var harness = new CliProcessHarness();
        harness.SetEnvironmentVariable("EDITOR", harness.TestEditorExecutablePath);
        harness.ConfigureEnvironmentEditor();
        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Created body");
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");

        AssertSucceeded(await harness.RunAsync("create", "work.mnote"));
        AssertSucceeded(await harness.RunAsync("use", "work"));

        var result = await harness.RunAsync("new", "Roadmap");

        AssertSucceeded(result);
        var repository = new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
        var page = (await repository.ListPagesByHeadingAsync(
            notebookPath,
            "Roadmap",
            CancellationToken.None)).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StandardOutput, Does.Contain("Created page \"Roadmap\""));
            Assert.That(result.StandardOutput, Does.Contain(page.Guid.ToString("D")));
            Assert.That(page.Text, Is.EqualTo("Created body"));
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
