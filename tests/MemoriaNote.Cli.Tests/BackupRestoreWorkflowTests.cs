using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies backup and restore wiring through the CLI process boundary.
/// </summary>
[TestFixture]
public sealed class BackupRestoreWorkflowTests
{
    /// <summary>
    /// Verifies that explicit backup and restore paths produce the expected artifacts.
    /// </summary>
    [Test]
    public async Task BackupAndRestore_WithExplicitPaths_CreatesExpectedArtifacts()
    {
        using var harness = new CliProcessHarness();
        const string noteName = "archive";
        const string noteTitle = "Archive Note";
        var backupPath = Path.Combine(
            harness.TemporaryDirectory,
            noteName + ".json.zip");
        var restoreDirectory = Path.Combine(
            harness.TemporaryDirectory,
            "restored");
        var restoredDatabasePath = Path.Combine(
            restoreDirectory,
            noteName + ".db");
        Directory.CreateDirectory(restoreDirectory);

        var createResult = await harness.RunAsync(
            "work",
            "create",
            noteName,
            noteTitle);

        AssertSucceeded(createResult);

        var backupResult = await harness.RunAsync(
            "work",
            "backup",
            noteName,
            "--output",
            backupPath);

        AssertSucceeded(backupResult);
        Assert.That(backupResult.StandardOutput, Does.Contain("Backup completed"));
        Assert.That(File.Exists(backupPath), Is.True);
        Assert.That(new FileInfo(backupPath).Length, Is.GreaterThan(0));

        var restoreResult = await harness.RunAsync(
            "work",
            "restore",
            backupPath,
            "--output-dir",
            restoreDirectory);

        AssertSucceeded(restoreResult);
        Assert.That(restoreResult.StandardOutput, Does.Contain("Restore completed"));
        Assert.That(File.Exists(restoredDatabasePath), Is.True);
        Assert.That(new FileInfo(restoredDatabasePath).Length, Is.GreaterThan(0));
        Assert.That(
            Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
            Is.Empty);
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
