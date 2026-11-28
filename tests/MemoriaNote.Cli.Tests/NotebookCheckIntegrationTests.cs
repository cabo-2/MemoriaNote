using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies notebook checking through the production CLI composition.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class NotebookCheckIntegrationTests
{
    /// <summary>Verifies help fixes the direct-path and read-only command contract.</summary>
    [Test]
    public async Task Help_DescribesCompleteReadOnlyCheck()
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync("notebooks", "check", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput,
                Does.Contain("Usage: mn notebooks check [options] <source>"));
            Assert.That(result.StandardOutput,
                Does.Contain("Existing schema v2 notebook file"));
            Assert.That(result.StandardOutput, Does.Contain("SQLite integrity"));
            Assert.That(result.StandardOutput, Does.Contain("workspace selection is not changed"));
            Assert.That(result.StandardOutput, Does.Not.Contain("--output"));
            Assert.That(result.StandardOutput, Does.Not.Contain("--json"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies a valid check reports every layer and changes no persistent state.</summary>
    [Test]
    public async Task Check_ValidNotebook_ReportsSuccessAndPreservesSourceAndWorkspace()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var source = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var configurationPath = Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
        var beforeBytes = await File.ReadAllBytesAsync(source);
        var beforeConfiguration = await File.ReadAllBytesAsync(configurationPath);
        var unrelatedWorkspace = Path.Combine(harness.WorkingDirectory, "unrelated");
        Directory.CreateDirectory(unrelatedWorkspace);
        var beforeFiles = Directory.GetFileSystemEntries(harness.WorkingDirectory);

        var result = await harness.RunAsync(
            "notebooks",
            "check",
            "work.mnote",
            "--workspace",
            unrelatedWorkspace);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain("Notebook check: valid"));
            Assert.That(result.StandardOutput, Does.Contain("identity: passed"));
            Assert.That(result.StandardOutput, Does.Contain("schema: passed"));
            Assert.That(result.StandardOutput, Does.Contain("triggers: passed"));
            Assert.That(result.StandardOutput, Does.Contain("integrity: passed"));
            Assert.That(result.StandardOutput,
                Does.Contain("authoritative-values: passed"));
            Assert.That(result.StandardOutput, Does.Contain("derived-data: passed"));
            Assert.That(result.StandardOutput, Does.Contain("Source modified: no"));
            Assert.That(result.StandardOutput, Does.Contain("Issues: 0"));
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeBytes));
            Assert.That(
                Directory.GetFileSystemEntries(harness.WorkingDirectory),
                Is.EqualTo(beforeFiles));
            Assert.That(
                await File.ReadAllBytesAsync(configurationPath),
                Is.EqualTo(beforeConfiguration));
        }
    }

    /// <summary>Verifies a trigger issue is reported on stdout with guarded repair guidance.</summary>
    [Test]
    public async Task Check_TriggerIssue_ReturnsValidationExitAndRepairGuidance()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "broken")).ExitCode, Is.Zero);
        var source = Path.Combine(harness.WorkingDirectory, "broken.mnote");
        await ExecuteAsync(source, "DROP TRIGGER Pages_TagsUpdate;");
        var beforeBytes = await File.ReadAllBytesAsync(source);
        var beforeFiles = Directory.GetFileSystemEntries(harness.WorkingDirectory);

        var result = await harness.RunAsync("notebooks", "check", source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain("Notebook check: issues found"));
            Assert.That(result.StandardOutput, Does.Contain("schema: passed"));
            Assert.That(result.StandardOutput, Does.Contain("triggers: failed"));
            Assert.That(result.StandardOutput, Does.Contain("ISSUE trigger-missing"));
            Assert.That(result.StandardOutput,
                Does.Contain("authoritative-values: not evaluated"));
            Assert.That(result.StandardOutput, Does.Contain("mn notebooks repair"));
            Assert.That(result.StandardOutput, Does.Contain("may refuse this source"));
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeBytes));
            Assert.That(
                Directory.GetFileSystemEntries(harness.WorkingDirectory),
                Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies unreadable input is a fatal resolution error without repair guidance.</summary>
    [Test]
    public async Task Check_UnreadableSource_UsesStderrAndBackupGuidance()
    {
        using var harness = new CliInProcessHarness();
        var source = Path.Combine(harness.WorkingDirectory, "unreadable.mnote");
        await File.WriteAllTextAsync(source, "not a SQLite database");

        var result = await harness.RunAsync("notebooks", "check", source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("safely readable schema v2"));
            Assert.That(result.StandardError, Does.Contain("backup"));
            Assert.That(result.StandardError, Does.Not.Contain("notebooks repair"));
        }
    }

    /// <summary>Verifies missing input keeps its distinct not-found status.</summary>
    [Test]
    public async Task Check_MissingSource_ReturnsNotFound()
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync(
            "notebooks",
            "check",
            Path.Combine(harness.WorkingDirectory, "missing.mnote"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("does not exist"));
        }
    }

    /// <summary>Verifies schema v1 is a migration resolution instead of a check issue.</summary>
    [Test]
    public async Task Check_LegacySource_UsesMigrationGuidanceWithoutSidecars()
    {
        using var harness = new CliInProcessHarness();
        var source = Path.Combine(harness.WorkingDirectory, "legacy.mnote");
        using var legacy = new TemporaryNotebookDatabase();
        legacy.CreateNotebook(
            "legacy",
            "Legacy",
            source,
            useWriteAheadLogging: true);
        var beforeFiles = ExistingSqliteFiles(source);
        Assert.That(beforeFiles, Is.EqualTo([source]));

        var result = await harness.RunAsync("notebooks", "check", source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("schema v1"));
            Assert.That(result.StandardError, Does.Contain("mn notebooks migrate"));
            Assert.That(result.StandardError, Does.Not.Contain("notebooks repair"));
            Assert.That(ExistingSqliteFiles(source), Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies a future format is directed to a compatible newer release.</summary>
    [Test]
    public async Task Check_FutureSource_UsesCompatibleApplicationGuidance()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "future")).ExitCode, Is.Zero);
        var source = Path.Combine(harness.WorkingDirectory, "future.mnote");
        await SchemaV2TestNotebook.SetUserVersionAsync(source, 3);

        var result = await harness.RunAsync("notebooks", "check", source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("version 3"));
            Assert.That(result.StandardError, Does.Contain("newer Memoria Note release"));
            Assert.That(result.StandardError, Does.Not.Contain("notebooks repair"));
        }
    }

    private static async Task ExecuteAsync(string path, string commandText)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static IReadOnlyList<string> ExistingSqliteFiles(string databasePath) =>
        new[]
            {
                databasePath,
                databasePath + "-journal",
                databasePath + "-shm",
                databasePath + "-wal"
            }
            .Where(File.Exists)
            .ToArray();
}
