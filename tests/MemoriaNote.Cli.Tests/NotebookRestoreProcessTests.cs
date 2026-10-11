using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies raw archive restore through the CLI process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class NotebookRestoreProcessTests
{
    /// <summary>Verifies omitted and explicit dash sources read exact archive bytes from stdin.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task StandardInputRestore_Redirected_CreatesNotebook(bool explicitDash)
    {
        using var harness = new CliProcessHarness();
        await PrepareArchiveAsync(harness, "stream-source", "stream.zip", 1);
        var archiveBytes = await File.ReadAllBytesAsync(
            Path.Combine(harness.WorkingDirectory, "stream.zip"));
        var arguments = new List<string> { "notebooks", "restore" };
        if (explicitDash)
            arguments.Add("-");
        arguments.Add("--target");
        arguments.Add(explicitDash ? "explicit-stream" : "implicit-stream");

        var result = await harness.RunWithStandardInputAsync(
            arguments,
            archiveBytes,
            TimeSpan.FromSeconds(10));

        var destination = Path.Combine(
            harness.WorkingDirectory,
            (explicitDash ? "explicit-stream" : "implicit-stream") + ".mnote");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("pages=1"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(destination), Is.True);
        }
    }

    /// <summary>Verifies stream dry-run consumes the archive without publishing state.</summary>
    [Test]
    public async Task StandardInputDryRun_ValidArchive_ReportsCountsWithoutCreatingNotebook()
    {
        using var harness = new CliProcessHarness();
        await PrepareArchiveAsync(harness, "dry-source", "dry.zip", 1);
        var archiveBytes = await File.ReadAllBytesAsync(
            Path.Combine(harness.WorkingDirectory, "dry.zip"));

        var result = await harness.RunWithStandardInputAsync(
            new[]
            {
                "notebooks",
                "restore",
                "--target",
                "dry-target",
                "--dry-run"
            },
            archiveBytes,
            TimeSpan.FromSeconds(10));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.StartWith("Restore validated: "));
            Assert.That(result.StandardOutput, Does.Contain("pages=1"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "dry-target.mnote")),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    harness.WorkingDirectory,
                    WorkspaceConfigurationStore.FileName)),
                Is.False);
        }
    }

    static async Task PrepareArchiveAsync(
        CliProcessHarness harness,
        string notebookName,
        string archiveName,
        int pageCount)
    {
        var create = await harness.RunAsync("create", notebookName);
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var notebookPath = Path.Combine(
            harness.WorkingDirectory,
            notebookName + ".mnote");
        var repository = new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
        for (var index = 1; index <= pageCount; index++)
        {
            await repository.CreatePageAsync(
                notebookPath,
                "Page " + index,
                "Body " + index,
                null,
                CancellationToken.None);
        }

        var backup = await harness.RunAsync(
            "notebooks",
            "backup",
            "--notebook",
            notebookName,
            archiveName);
        Assert.That(backup.ExitCode, Is.Zero, backup.StandardError);
    }
}
