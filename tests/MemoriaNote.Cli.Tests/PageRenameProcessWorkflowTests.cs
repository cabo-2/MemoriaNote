using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies page rename workflows through the CLI process boundary.</summary>
[TestFixture]
public sealed class PageRenameProcessWorkflowTests
{
    /// <summary>Verifies rename preserves identity and content while replacing the exact name.</summary>
    [Test]
    public async Task Rename_ByName_PreservesPageIdentityAndContent()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var repository = CreateRepository();
        var page = await repository.CreatePageAsync(
            notebookPath,
            "Meeting - Project A",
            "meeting body",
            null!,
            CancellationToken.None);

        var result = await harness.RunAsync(
            "rename",
            "Meeting - Project A",
            "Meeting - Project B");
        var renamed = await repository.FindPageAsync(
            notebookPath,
            page.Guid,
            CancellationToken.None);
        var byId = await harness.RunAsync(
            "cat",
            "--id",
            page.Guid.ToString("D"));
        var oldName = await harness.RunAsync("cat", "Meeting - Project A");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("renamed successfully"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(renamed, Is.Not.Null);
            Assert.That(renamed!.Guid, Is.EqualTo(page.Guid));
            Assert.That(renamed.Name, Is.EqualTo("Meeting - Project B"));
            Assert.That(renamed.Text, Is.EqualTo("meeting body"));
            Assert.That(renamed.CreateTime, Is.EqualTo(page.CreateTime));
            Assert.That(byId.ExitCode, Is.Zero);
            Assert.That(byId.StandardOutput, Is.EqualTo("meeting body"));
            Assert.That(oldName.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies a Page ID disambiguates legacy duplicate names.</summary>
    [Test]
    public async Task Rename_ByPageId_DisambiguatesDuplicateSourceNames()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var repository = CreateRepository();
        var first = await repository.CreatePageAsync(
            notebookPath,
            "Daily",
            "first",
            null!,
            CancellationToken.None);
        var second = await repository.CreatePageAsync(
            notebookPath,
            "Daily",
            "second",
            null!,
            CancellationToken.None);

        var ambiguous = await harness.RunAsync(
            "rename",
            "Daily",
            "Archive");
        var explicitResult = await harness.RunAsync(
            "rename",
            "--id",
            first.Guid.ToString("D"),
            "Archive");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ambiguous.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(ambiguous.StandardOutput, Is.Empty);
            Assert.That(ambiguous.StandardError, Does.Contain("--id"));
            Assert.That(explicitResult.ExitCode, Is.Zero);
            Assert.That(
                (await repository.FindPageAsync(
                    notebookPath,
                    first.Guid,
                    CancellationToken.None))?.Name,
                Is.EqualTo("Archive"));
            Assert.That(
                (await repository.FindPageAsync(
                    notebookPath,
                    second.Guid,
                    CancellationToken.None))?.Name,
                Is.EqualTo("Daily"));
        }
    }

    /// <summary>Verifies a destination collision fails without changing either page.</summary>
    [Test]
    public async Task Rename_ToExistingName_ReturnsConflictWithoutChanges()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var repository = CreateRepository();
        var source = await repository.CreatePageAsync(
            notebookPath,
            "Source",
            "source body",
            null!,
            CancellationToken.None);
        var destination = await repository.CreatePageAsync(
            notebookPath,
            "Destination",
            "destination body",
            null!,
            CancellationToken.None);

        var result = await harness.RunAsync(
            "rename",
            "Source",
            "Destination");
        var unchangedName = await harness.RunAsync(
            "rename",
            "Source",
            "Source");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("already in use"));
            Assert.That(unchangedName.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(unchangedName.StandardOutput, Is.Empty);
            Assert.That(unchangedName.StandardError, Does.Contain("already in use"));
            Assert.That(
                (await repository.FindPageAsync(
                    notebookPath,
                    source.Guid,
                    CancellationToken.None))?.Name,
                Is.EqualTo("Source"));
            Assert.That(
                (await repository.FindPageAsync(
                    notebookPath,
                    destination.Guid,
                    CancellationToken.None))?.Name,
                Is.EqualTo("Destination"));
        }
    }

    /// <summary>Verifies multiple notebooks require an explicit invocation target.</summary>
    [Test]
    public async Task Rename_WithMultipleNotebooks_RequiresExplicitTarget()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "first.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("create", "second.mnote")).ExitCode, Is.Zero);
        var secondPath = Path.Combine(harness.WorkingDirectory, "second.mnote");
        var repository = CreateRepository();
        var page = await repository.CreatePageAsync(
            secondPath,
            "Second page",
            "second body",
            null!,
            CancellationToken.None);

        var ambiguous = await harness.RunAsync(
            "rename",
            "Second page",
            "Renamed page");
        var explicitResult = await harness.RunAsync(
            "rename",
            "--notebook",
            "second.mnote",
            "Second page",
            "Renamed page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ambiguous.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(ambiguous.StandardOutput, Is.Empty);
            Assert.That(ambiguous.StandardError, Does.Contain("--notebook"));
            Assert.That(explicitResult.ExitCode, Is.Zero);
            Assert.That(
                (await repository.FindPageAsync(
                    secondPath,
                    page.Guid,
                    CancellationToken.None))?.Name,
                Is.EqualTo("Renamed page"));
        }
    }

    /// <summary>Verifies help and invalid argument forms expose the public contract.</summary>
    [Test]
    public async Task Rename_HelpAndInvalidArguments_ExposePublicContract()
    {
        using var harness = new CliProcessHarness();

        var help = await harness.RunAsync("rename", "--help");
        var missingTarget = await harness.RunAsync("rename");
        var missingNewName = await harness.RunAsync("rename", "Before");
        var bothSelectors = await harness.RunAsync(
            "rename",
            "Before",
            "After",
            "--id",
            "abcd");
        var removedPageCommand = await harness.RunAsync(
            "page",
            "rename",
            "Before",
            "After");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(help.ExitCode, Is.Zero);
            Assert.That(help.StandardOutput, Does.Contain("Usage: mn rename"));
            Assert.That(help.StandardOutput, Does.Contain("--id <uuid-or-prefix>"));
            Assert.That(help.StandardOutput, Does.Contain("--notebook <path>"));
            Assert.That(missingTarget.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(missingNewName.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(bothSelectors.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(removedPageCommand.ExitCode, Is.Not.Zero);
            Assert.That(missingTarget.StandardOutput, Is.Empty);
            Assert.That(missingNewName.StandardOutput, Is.Empty);
            Assert.That(bothSelectors.StandardOutput, Is.Empty);
            Assert.That(removedPageCommand.StandardError, Does.Contain("Unrecognized"));
        }
    }

    static SqlitePageRepository CreateRepository()
    {
        return new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
    }
}
