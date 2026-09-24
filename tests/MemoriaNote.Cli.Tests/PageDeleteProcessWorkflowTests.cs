using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies page deletion workflows through the CLI process boundary.</summary>
[TestFixture]
public sealed class PageDeleteProcessWorkflowTests
{
    /// <summary>Verifies force deletes exactly the named page in a non-interactive process.</summary>
    [Test]
    public async Task Delete_ForcedByName_RemovesOnlyTheResolvedPage()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var repository = CreateRepository();
        var target = await repository.CreatePageAsync(
            notebookPath,
            "Delete me",
            "target",
            null!,
            CancellationToken.None);
        var retained = await repository.CreatePageAsync(
            notebookPath,
            "Keep me",
            "retained",
            null!,
            CancellationToken.None);

        var result = await harness.RunAsync("delete", "Delete me", "--force");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("deleted successfully"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(
                await repository.FindPageAsync(notebookPath, target.Guid, CancellationToken.None),
                Is.Null);
            Assert.That(
                await repository.FindPageAsync(notebookPath, retained.Guid, CancellationToken.None),
                Is.Not.Null);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies Page ID selects one page when legacy names are duplicated.</summary>
    [Test]
    public async Task Delete_ByPageId_DisambiguatesDuplicateNames()
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

        var ambiguous = await harness.RunAsync("delete", "Daily", "--force");
        var explicitResult = await harness.RunAsync(
            "delete",
            "--id",
            first.Guid.ToString("D"),
            "--force");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ambiguous.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(ambiguous.StandardError, Does.Contain("--id"));
            Assert.That(explicitResult.ExitCode, Is.Zero);
            Assert.That(
                await repository.FindPageAsync(notebookPath, first.Guid, CancellationToken.None),
                Is.Null);
            Assert.That(
                await repository.FindPageAsync(notebookPath, second.Guid, CancellationToken.None),
                Is.Not.Null);
        }
    }

    /// <summary>Verifies redirected input cannot implicitly confirm a destructive operation.</summary>
    [Test]
    public async Task Delete_NonInteractiveWithoutForce_LeavesPageUnchanged()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var repository = CreateRepository();
        var page = await repository.CreatePageAsync(
            notebookPath,
            "Keep",
            "body",
            null!,
            CancellationToken.None);

        var result = await harness.RunAsync("delete", "Keep");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("--force"));
            Assert.That(
                await repository.FindPageAsync(notebookPath, page.Guid, CancellationToken.None),
                Is.Not.Null);
        }
    }

    /// <summary>Verifies dry-run resolves and reports a page without deleting it.</summary>
    [Test]
    public async Task Delete_DryRun_ReportsTargetWithoutMutation()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var repository = CreateRepository();
        var page = await repository.CreatePageAsync(
            notebookPath,
            "Preview",
            "body",
            null!,
            CancellationToken.None);

        var result = await harness.RunAsync("delete", "Preview", "--dry-run");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Would delete page 'Preview'"));
            Assert.That(result.StandardOutput, Does.Contain(page.Guid.ToString("D")));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(
                await repository.FindPageAsync(notebookPath, page.Guid, CancellationToken.None),
                Is.Not.Null);
        }
    }

    /// <summary>Verifies an explicit notebook is required when the workspace is ambiguous.</summary>
    [Test]
    public async Task Delete_WithMultipleNotebooks_RequiresExplicitTarget()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "first.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("create", "second.mnote")).ExitCode, Is.Zero);
        var secondPath = Path.Combine(harness.WorkingDirectory, "second.mnote");
        var repository = CreateRepository();
        var page = await repository.CreatePageAsync(
            secondPath,
            "Second page",
            "body",
            null!,
            CancellationToken.None);

        var ambiguous = await harness.RunAsync("delete", "Second page", "--force");
        var explicitResult = await harness.RunAsync(
            "delete",
            "Second page",
            "--notebook",
            "second.mnote",
            "--force");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ambiguous.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(ambiguous.StandardError, Does.Contain("--notebook"));
            Assert.That(explicitResult.ExitCode, Is.Zero);
            Assert.That(
                await repository.FindPageAsync(secondPath, page.Guid, CancellationToken.None),
                Is.Null);
        }
    }

    /// <summary>Verifies help and invalid selectors expose the public contract.</summary>
    [Test]
    public async Task Delete_HelpAndInvalidArguments_ExposePublicContract()
    {
        using var harness = new CliProcessHarness();

        var help = await harness.RunAsync("delete", "--help");
        var missing = await harness.RunAsync("delete", "--force");
        var both = await harness.RunAsync(
            "delete",
            "Page",
            "--id",
            "abcd",
            "--force");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(help.ExitCode, Is.Zero);
            Assert.That(help.StandardOutput, Does.Contain("Usage: mn delete"));
            Assert.That(help.StandardOutput, Does.Contain("--force"));
            Assert.That(help.StandardOutput, Does.Contain("--dry-run"));
            Assert.That(missing.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(both.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(missing.StandardOutput, Is.Empty);
            Assert.That(both.StandardOutput, Is.Empty);
        }
    }

    static SqlitePageRepository CreateRepository()
    {
        return new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
    }
}
