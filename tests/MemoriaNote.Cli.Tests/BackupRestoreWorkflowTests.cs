using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies backup and restore wiring through the CLI process boundary.
/// </summary>
[TestFixture]
[Category("Process")]
public sealed class BackupRestoreWorkflowTests
{
    /// <summary>
    /// Verifies the legacy restore remains available until its dedicated replacement ships.
    /// </summary>
    [Test]
    public async Task LegacyRestore_WithPreparedArchive_CreatesExpectedArtifact()
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

        await CreateLegacyBackupAsync(
            Path.Combine(harness.ApplicationDataDirectory, noteName + ".db"),
            backupPath);

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

    /// <summary>
    /// Verifies the legacy backup command only reports migration guidance and changes nothing.
    /// </summary>
    [Test]
    public async Task LegacyBackup_ReturnsGuidanceWithoutCreatingStateOrOutput()
    {
        using var harness = new CliProcessHarness();
        var outputPath = Path.Combine(harness.TemporaryDirectory, "legacy.zip");

        var result = await harness.RunAsync(
            "work",
            "backup",
            "anything",
            "--output",
            outputPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("mn notebooks backup"));
            Assert.That(File.Exists(outputPath), Is.False);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
        }
    }

    static async Task CreateLegacyBackupAsync(string notebookPath, string backupPath)
    {
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var metadataRepository = new SqliteNotebookMetadataRepository(factory);
        var service = new NotebookBackupService(
            new SqliteNotebookTransferRepository(factory),
            metadataRepository,
            new SqliteNotebookMigrator(factory, metadataRepository),
            new NotebookFilePathFactory());
        await service.CreateBackupAsync(
            NotebookId.FromDatabasePath(notebookPath),
            backupPath,
            CancellationToken.None);
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
