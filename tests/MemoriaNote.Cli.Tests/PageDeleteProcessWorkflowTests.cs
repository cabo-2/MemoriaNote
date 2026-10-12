using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the redirected-input page deletion boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class PageDeleteProcessWorkflowTests
{
    /// <summary>Verifies redirected input cannot implicitly confirm a destructive operation.</summary>
    [Test]
    public async Task Delete_NonInteractiveWithoutForce_LeavesPageUnchanged()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
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

    static SqlitePageRepository CreateRepository()
    {
        return new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
    }
}
