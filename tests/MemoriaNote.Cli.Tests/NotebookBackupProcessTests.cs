using System.Text;
using MemoriaNote.Archive;
using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies raw archive backup through the CLI process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class NotebookBackupProcessTests
{
    /// <summary>Verifies omitted and explicit dash destinations emit only archive bytes.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task StandardOutputBackup_Redirected_EmitsValidatedArchiveOnly(
        bool explicitDash)
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "stream");
        await AddPagesAsync(notebookPath, 1);
        var arguments = new List<string> { "notebooks", "backup" };
        if (explicitDash)
            arguments.Add("-");

        var result = await harness.RunAsync(
            arguments,
            null,
            TimeSpan.FromSeconds(10));

        using var archive = new MemoryStream(result.StandardOutputBytes, writable: false);
        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutputBytes, Is.Not.Empty);
            Assert.That(report.IsValid, Is.True);
            Assert.That(
                Encoding.UTF8.GetString(result.StandardOutputBytes),
                Does.Not.Contain("Backup completed"));
        }
    }

    /// <summary>Verifies a target failure emits no raw archive bytes.</summary>
    [Test]
    public async Task StandardOutputBackup_WithoutSelection_ReturnsConflictWithoutBytes()
    {
        using var harness = new CliProcessHarness();
        await CreateNotebookAsync(harness, "available");

        var result = await harness.RunAsync("notebooks", "backup");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutputBytes, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("mn use"));
        }
    }

    static async Task<string> CreateAndSelectNotebookAsync(
        CliProcessHarness harness,
        string name)
    {
        var notebookPath = await CreateNotebookAsync(harness, name);
        var use = await harness.RunAsync("use", name);
        Assert.That(use.ExitCode, Is.Zero, use.StandardError);
        return notebookPath;
    }

    static async Task<string> CreateNotebookAsync(CliProcessHarness harness, string name)
    {
        var create = await harness.RunAsync("create", name);
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        return Path.Combine(harness.WorkingDirectory, name + ".mnote");
    }

    static async Task AddPagesAsync(string notebookPath, int count)
    {
        var repository = new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
        for (var index = 1; index <= count; index++)
        {
            await repository.CreatePageAsync(
                notebookPath,
                "Page " + index,
                "Body " + index,
                null,
                CancellationToken.None);
        }
    }
}
