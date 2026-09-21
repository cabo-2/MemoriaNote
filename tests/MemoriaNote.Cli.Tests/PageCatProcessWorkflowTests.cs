using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the read-only page cat workflow through the process boundary.</summary>
[TestFixture]
public sealed class PageCatProcessWorkflowTests
{
    /// <summary>Verifies name, complete ID, and short ID selectors write only the exact body.</summary>
    [Test]
    public async Task Cat_NameAndPageIdSelectors_WriteRawBodyWithoutPersistentChanges()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var page = await CreateRepository().CreatePageAsync(
            notebookPath,
            "Meeting - Project A",
            "first line\nsecond line",
            null!,
            CancellationToken.None);
        var before = await File.ReadAllBytesAsync(notebookPath);

        var byName = await harness.RunAsync("cat", "Meeting - Project A");
        var byCompleteId = await harness.RunAsync(
            "cat",
            "--id",
            page.Guid.ToString("D").ToUpperInvariant());
        var byPrefix = await harness.RunAsync(
            "cat",
            "--id",
            page.Guid.ToString("N")[..4].ToUpperInvariant());

        foreach (var result in new[] { byName, byCompleteId, byPrefix })
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(result.StandardOutput, Is.EqualTo("first line\nsecond line"));
                Assert.That(result.StandardError, Is.Empty);
            }
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(await File.ReadAllBytesAsync(notebookPath), Is.EqualTo(before));
        }
    }

    /// <summary>Verifies duplicate names conflict and a complete Page ID disambiguates them.</summary>
    [Test]
    public async Task Cat_DuplicateName_RequiresPageId()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var repository = CreateRepository();
        var first = await repository.CreatePageAsync(
            notebookPath,
            "Daily",
            "first body",
            null!,
            CancellationToken.None);
        await repository.CreatePageAsync(
            notebookPath,
            "Daily",
            "second body",
            null!,
            CancellationToken.None);

        var ambiguous = await harness.RunAsync("cat", "Daily");
        var explicitResult = await harness.RunAsync(
            "cat",
            "--id",
            first.Guid.ToString("D"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ambiguous.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(ambiguous.StandardOutput, Is.Empty);
            Assert.That(ambiguous.StandardError, Does.Contain("--id"));
            Assert.That(explicitResult.ExitCode, Is.Zero);
            Assert.That(explicitResult.StandardOutput, Is.EqualTo("first body"));
            Assert.That(explicitResult.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies a colliding Page ID prefix is rejected instead of choosing a page.</summary>
    [Test]
    public async Task Cat_AmbiguousPageIdPrefix_ReturnsConflict()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        using (var context = factory.CreateDbContext(notebookPath))
        {
            var first = Page.Create("First", "first");
            first.Guid = Guid.Parse("abcd0000-0000-0000-0000-000000000001");
            var second = Page.Create("Second", "second");
            second.Guid = Guid.Parse("abcd0000-0000-0000-0000-000000000002");
            context.Pages.AddRange(first, second);
            await context.SaveChangesAsync();
        }

        var result = await harness.RunAsync("cat", "--id", "ABCD");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("more characters"));
        }
    }

    /// <summary>Verifies multiple notebooks require the existing explicit notebook option.</summary>
    [Test]
    public async Task Cat_WithMultipleNotebooks_RequiresExplicitTarget()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "first.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("create", "second.mnote")).ExitCode, Is.Zero);
        await CreateRepository().CreatePageAsync(
            Path.Combine(harness.WorkingDirectory, "second.mnote"),
            "Second page",
            "second body",
            null!,
            CancellationToken.None);

        var ambiguous = await harness.RunAsync("cat", "Second page");
        var explicitResult = await harness.RunAsync(
            "cat",
            "--notebook",
            "second.mnote",
            "Second page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ambiguous.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(ambiguous.StandardOutput, Is.Empty);
            Assert.That(ambiguous.StandardError, Does.Contain("--notebook"));
            Assert.That(explicitResult.ExitCode, Is.Zero);
            Assert.That(explicitResult.StandardOutput, Is.EqualTo("second body"));
            Assert.That(explicitResult.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies a missing page reports not found without writing data output.</summary>
    [Test]
    public async Task Cat_MissingPage_ReturnsNotFound()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);

        var result = await harness.RunAsync("cat", "Missing page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("No page matched"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies cat exposes its selector contract and validates it before notebook access.</summary>
    [Test]
    public async Task Cat_HelpAndInvalidSelectors_ExposeThePublicContract()
    {
        using var harness = new CliProcessHarness();

        var help = await harness.RunAsync("cat", "--help");
        var missing = await harness.RunAsync("cat");
        var both = await harness.RunAsync("cat", "Page", "--id", "abcd");
        var shortId = await harness.RunAsync("cat", "--id", "abc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(help.ExitCode, Is.Zero);
            Assert.That(help.StandardOutput, Does.Contain("Usage: mn cat"));
            Assert.That(help.StandardOutput, Does.Contain("--id <uuid-or-prefix>"));
            Assert.That(help.StandardOutput, Does.Contain("--notebook <path>"));
            Assert.That(missing.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(both.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(shortId.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(missing.StandardOutput, Is.Empty);
            Assert.That(both.StandardOutput, Is.Empty);
            Assert.That(shortId.StandardOutput, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    static SqlitePageRepository CreateRepository()
    {
        return new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
    }
}
