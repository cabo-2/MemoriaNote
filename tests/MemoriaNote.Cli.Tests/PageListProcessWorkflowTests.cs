using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the read-only page-list workflow through the CLI process boundary.</summary>
[TestFixture]
public sealed class PageListProcessWorkflowTests
{
    /// <summary>
    /// Verifies ls and its list alias share stable name ordering, limit, and read-only behavior.
    /// </summary>
    [Test]
    public async Task Ls_ListsStableNamesAndListUsesTheSameReadOnlyCommand()
    {
        using var harness = new CliProcessHarness();
        var create = await harness.RunAsync("create", "work.mnote");
        Assert.That(create.ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var repository = CreateRepository();
        await repository.CreatePageAsync(
            notebookPath,
            "alpha",
            "lower",
            null!,
            CancellationToken.None);
        await repository.CreatePageAsync(
            notebookPath,
            "Roadmap",
            "middle",
            null!,
            CancellationToken.None);
        await repository.CreatePageAsync(
            notebookPath,
            "Alpha",
            "upper",
            null!,
            CancellationToken.None);
        var before = await File.ReadAllBytesAsync(notebookPath);
        var workspaceConfigurationPath = Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
        var configurationBefore = await File.ReadAllBytesAsync(
            workspaceConfigurationPath);

        var limited = await harness.RunAsync("ls", "--limit", "2");
        var alias = await harness.RunAsync("list");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(limited.ExitCode, Is.Zero);
            Assert.That(limited.StandardError, Is.Empty);
            Assert.That(
                GetOutputLines(limited.StandardOutput),
                Is.EqualTo(new[] { "Alpha", "Roadmap" }));
            Assert.That(alias.ExitCode, Is.Zero);
            Assert.That(alias.StandardError, Is.Empty);
            Assert.That(
                GetOutputLines(alias.StandardOutput),
                Is.EqualTo(new[] { "Alpha", "Roadmap", "alpha" }));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(await File.ReadAllBytesAsync(notebookPath), Is.EqualTo(before));
            Assert.That(
                await File.ReadAllBytesAsync(workspaceConfigurationPath),
                Is.EqualTo(configurationBefore));
        }
    }

    /// <summary>Verifies long output exposes complete page metadata including the full Page ID.</summary>
    [Test]
    public async Task Ls_LongFormat_WritesAllSummaryColumns()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var page = await CreateRepository().CreatePageAsync(
            notebookPath,
            "Meeting - Project A",
            "body",
            "projects",
            CancellationToken.None);

        var result = await harness.RunAsync("ls", "--long");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain("CREATED-UTC"));
            Assert.That(result.StandardOutput, Does.Contain("UPDATED-UTC"));
            Assert.That(result.StandardOutput, Does.Contain("PAGE-ID"));
            Assert.That(result.StandardOutput, Does.Contain(page.Guid.ToString("D")));
            Assert.That(result.StandardOutput, Does.Contain("{\"Dir\":\"projects\"}"));
            Assert.That(result.StandardOutput, Does.EndWith(
                "Meeting - Project A" + Environment.NewLine));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies an explicit notebook disambiguates multiple root notebooks.</summary>
    [Test]
    public async Task Ls_WithMultipleNotebooks_RequiresExplicitTarget()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "first.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("create", "second.mnote")).ExitCode, Is.Zero);
        await CreateRepository().CreatePageAsync(
            Path.Combine(harness.WorkingDirectory, "second.mnote"),
            "Second page",
            "body",
            null!,
            CancellationToken.None);

        var ambiguous = await harness.RunAsync("ls");
        var explicitResult = await harness.RunAsync(
            "ls",
            "--notebook",
            "second.mnote");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ambiguous.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(ambiguous.StandardOutput, Is.Empty);
            Assert.That(ambiguous.StandardError, Does.Contain("--notebook"));
            Assert.That(explicitResult.ExitCode, Is.Zero);
            Assert.That(
                GetOutputLines(explicitResult.StandardOutput),
                Is.EqualTo(new[] { "Second page" }));
            Assert.That(explicitResult.StandardError, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies the retired dynamic-completion option is not exposed by ls.</summary>
    [Test]
    public async Task Ls_CompletionOption_IsUnavailable()
    {
        using var harness = new CliProcessHarness();

        var help = await harness.RunAsync("ls", "--help");
        var completion = await harness.RunAsync("ls", "--completion");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(help.ExitCode, Is.Zero);
            Assert.That(help.StandardOutput, Does.Contain("Usage: mn ls"));
            Assert.That(help.StandardOutput, Does.Contain("--limit <count>"));
            Assert.That(help.StandardOutput, Does.Contain("-l|--long"));
            Assert.That(help.StandardOutput, Does.Not.Contain("--completion"));
            Assert.That(completion.ExitCode, Is.Not.Zero);
        }
    }

    static SqlitePageRepository CreateRepository()
    {
        return new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
    }

    static string[] GetOutputLines(string output)
    {
        return output.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
    }
}
