using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies commands that reject redirected standard input.</summary>
[TestFixture]
[Category("Process")]
public sealed class RedirectedInputProcessTests
{
    /// <summary>Verifies redirected input cannot enter interactive editor setup.</summary>
    [Test]
    public async Task EditorSetup_RedirectedInput_ReturnsValidationFailure()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("config", "editor", "setup");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("requires interactive input"));
            Assert.That(File.Exists(harness.UserConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies redirected input cannot implicitly confirm a destructive operation.</summary>
    [Test]
    public async Task Delete_NonInteractiveWithoutForce_LeavesPageUnchanged()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var repository = new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
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
                await repository.FindPageAsync(
                    notebookPath,
                    page.Guid,
                    CancellationToken.None),
                Is.Not.Null);
        }
    }
}
