using MemoriaNote.Core.Tests.Infrastructure;
using MemoriaNote.NotebookMaintenance;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.NotebookMaintenance;

/// <summary>Verifies repairability preflight and safe Schema V2 publication.</summary>
[TestFixture]
public sealed class NotebookRepairServiceTests
{
    /// <summary>Verifies repair copies one committed snapshot while a WAL writer is active.</summary>
    [Test]
    public async Task RepairAsync_ConcurrentWalWriter_CopiesCommittedSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("wal-writer.mnote");
        var output = directory.GetPath("repaired.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Committed title");
        await ExecuteAsync(source, "PRAGMA journal_mode = WAL;");
        var beforeMain = await File.ReadAllBytesAsync(source);
        NotebookRepairResult result;
        IReadOnlyList<string> beforeFiles;
        IReadOnlyList<string> afterFiles;
        await using (var writer = await OpenWriterAsync(source))
        {
            await ExecuteAsync(
                writer,
                "BEGIN IMMEDIATE; " +
                "UPDATE Metadata SET Value = 'uncommitted title' WHERE Key = 'Title';");
            beforeFiles = ExistingSqliteFiles(source);

            result = await new NotebookRepairService().RepairAsync(
                new NotebookRepairRequest(source, output),
                CancellationToken.None);
            afterFiles = ExistingSqliteFiles(source);
            await ExecuteAsync(writer, "ROLLBACK;");
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());
            Assert.That(
                await ScalarAsync<string>(
                    output,
                    "SELECT Value FROM Metadata WHERE Key = 'Title';"),
                Is.EqualTo("Committed title"));
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeMain));
            Assert.That(afterFiles, Is.EqualTo(beforeFiles));
            Assert.That(
                Directory.EnumerateDirectories(directory.Path, ".mn-*"),
                Is.Empty);
        }
    }

    /// <summary>Verifies a locked source maps to busy without publishing private output.</summary>
    [Test]
    public async Task RepairAsync_ExclusiveWriter_ReturnsBusyAndCleansOutput()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("busy.mnote");
        var output = directory.GetPath("unused.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Busy");
        await ExecuteAsync(source, "PRAGMA journal_mode = DELETE;");
        var beforeMain = await File.ReadAllBytesAsync(source);
        NotebookRepairResult result;
        IReadOnlyList<string> beforeFiles;
        IReadOnlyList<string> afterFiles;
        await using (var writer = await OpenWriterAsync(source))
        {
            await ExecuteAsync(
                writer,
                "BEGIN EXCLUSIVE; " +
                "UPDATE Metadata SET Value = 'locked' WHERE Key = 'Title';");
            beforeFiles = ExistingSqliteFiles(source);

            result = await new NotebookRepairService(busyTimeoutSeconds: 1).RepairAsync(
                new NotebookRepairRequest(source, output),
                CancellationToken.None);
            afterFiles = ExistingSqliteFiles(source);
            await ExecuteAsync(writer, "ROLLBACK;");
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.EqualTo(NotebookRepairErrorCode.SourceBusy));
            Assert.That(File.Exists(output), Is.False);
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeMain));
            Assert.That(afterFiles, Is.EqualTo(beforeFiles));
            Assert.That(
                Directory.EnumerateDirectories(directory.Path, ".mn-*"),
                Is.Empty);
        }
    }

    /// <summary>Verifies derived defects are rebuilt from exact authoritative records.</summary>
    [Test]
    public async Task RepairAsync_DerivedDefects_PreservesAuthoritativeValuesAndSource()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("source.mnote");
        var output = directory.GetPath("repaired.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Repair source");
        await ExecuteAsync(
            source,
            """
            INSERT INTO Metadata(Key, Value) VALUES ('NullExtension', NULL);
            INSERT INTO Pages(PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
            VALUES ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'discarded',
                    '2026-11-01 00:00:00.0000000', '2026-11-01 00:00:00.0000000',
                    1, '[]', 'discarded');
            DELETE FROM Pages WHERE Name = 'discarded';
            INSERT INTO Pages(PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
            VALUES ('11111111-2222-4333-8444-555555555555', '日本語',
                    '2026-11-01 00:00:01.0000000', '2026-11-01 00:00:02.0000000',
                    7, '["alpha","雪"]', 'private\nbody');
            DROP TRIGGER Pages_TagsInsert;
            DROP TRIGGER Pages_TagsUpdate;
            DROP TRIGGER Pages_RejectInternalIdUpdate;
            DROP TRIGGER Pages_RejectPageIdUpdate;
            DROP TABLE PageTags;
            DROP INDEX IX_Pages_Name;
            DROP INDEX IX_Pages_PageId;
            """);
        var sourceInternalId = await ScalarAsync<long>(
            source,
            "SELECT InternalId FROM Pages;");
        var beforeBytes = await File.ReadAllBytesAsync(source);
        var beforeFiles = Directory.GetFileSystemEntries(directory.Path);

        var result = await new NotebookRepairService().RepairAsync(
            new NotebookRepairRequest(source, output),
            CancellationToken.None);

        var validation = await SchemaV2DatabaseValidator.ValidateAsync(output);
        var outputInternalId = await ScalarAsync<long>(
            output,
            "SELECT InternalId FROM Pages;");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompleted, Is.True);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Report!.PreflightStatus,
                Is.EqualTo(NotebookRepairPreflightStatus.Repairable));
            Assert.That(result.Report.MetadataCount, Is.EqualTo(7));
            Assert.That(result.Report.PageCount, Is.EqualTo(1));
            Assert.That(result.Report.LogicalFingerprint,
                Does.Match("^[0-9a-f]{64}:[0-9a-f]{64}$"));
            Assert.That(result.Report.OutputValidationPassed, Is.True);
            Assert.That(result.Report.OutputPublished, Is.True);
            Assert.That(result.Report.SourceWasModified, Is.False);
            Assert.That(validation.IsValid, Is.True);
            Assert.That(sourceInternalId, Is.EqualTo(2));
            Assert.That(outputInternalId, Is.EqualTo(1));
            Assert.That(
                await ScalarAsync<string?>(output,
                    "SELECT Value FROM Metadata WHERE Key = 'NullExtension';"),
                Is.Null);
            Assert.That(
                await ScalarAsync<string>(output,
                    "SELECT Text FROM Pages WHERE Name = '日本語';"),
                Is.EqualTo("private\\nbody"));
            Assert.That(
                await ScalarAsync<long>(output, "SELECT COUNT(*) FROM PageTags;"),
                Is.EqualTo(2));
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeBytes));
            Assert.That(
                Directory.GetFileSystemEntries(directory.Path),
                Is.EquivalentTo(beforeFiles.Append(output)));
        }
    }

    /// <summary>Verifies an authoritative column defect is refused without output.</summary>
    [Test]
    public async Task RepairAsync_AuthoritativeSchemaDefect_RefusesAndPreservesSource()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("schema.mnote");
        var output = directory.GetPath("unused.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Schema defect");
        await ExecuteAsync(source, "ALTER TABLE Pages DROP COLUMN Text;");
        var beforeBytes = await File.ReadAllBytesAsync(source);
        var beforeFiles = Directory.GetFileSystemEntries(directory.Path);

        var result = await new NotebookRepairService().RepairAsync(
            new NotebookRepairRequest(source, output),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompleted, Is.True);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Report!.PreflightStatus,
                Is.EqualTo(NotebookRepairPreflightStatus.Refused));
            Assert.That(result.Report.Issues,
                Has.One.Property("Code").EqualTo(
                    NotebookRepairIssueCode.AuthoritativeSchemaMismatch));
            Assert.That(result.Report.OutputPublished, Is.False);
            Assert.That(File.Exists(output), Is.False);
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeBytes));
            Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies invalid authoritative values are never normalized or defaulted.</summary>
    [Test]
    public async Task RepairAsync_InvalidAuthoritativeValue_RefusesWithoutPrivateValue()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("values.mnote");
        var output = directory.GetPath("unused.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Value defect");
        await ExecuteAsync(
            source,
            "UPDATE Metadata SET Value = 'private-invalid-value' WHERE Key = 'ReadOnly';");

        var result = await new NotebookRepairService().RepairAsync(
            new NotebookRepairRequest(source, output),
            CancellationToken.None);

        var issue = result.Report!.Issues.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(issue.Code,
                Is.EqualTo(NotebookRepairIssueCode.AuthoritativeValueInvalid));
            Assert.That(issue.Location, Is.EqualTo("database.authoritative-values"));
            Assert.That(issue.Location, Does.Not.Contain("private-invalid-value"));
            Assert.That(File.Exists(output), Is.False);
        }
    }

    /// <summary>Verifies missing, legacy, unreadable, and output conflicts remain fatal results.</summary>
    [Test]
    public async Task RepairAsync_TargetFailures_ReturnClassifiedErrors()
    {
        using var directory = new TemporaryDirectory();
        var service = new NotebookRepairService();
        var missing = await service.RepairAsync(
            new NotebookRepairRequest(
                directory.GetPath("missing.mnote"),
                directory.GetPath("missing-output.mnote")),
            CancellationToken.None);

        var corruptPath = directory.GetPath("corrupt.mnote");
        await File.WriteAllTextAsync(corruptPath, "not sqlite");
        var corrupt = await service.RepairAsync(
            new NotebookRepairRequest(
                corruptPath,
                directory.GetPath("corrupt-output.mnote")),
            CancellationToken.None);

        using var legacy = new TemporaryNotebookDatabase();
        legacy.CreateNotebook("legacy", "Legacy");
        var legacyResult = await service.RepairAsync(
            new NotebookRepairRequest(
                legacy.DatabasePath,
                directory.GetPath("legacy-output.mnote")),
            CancellationToken.None);

        var source = directory.GetPath("source.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Conflict");
        var samePath = await service.RepairAsync(
            new NotebookRepairRequest(source, source),
            CancellationToken.None);
        var existingOutput = directory.GetPath("existing.mnote");
        await File.WriteAllTextAsync(existingOutput, "existing");
        var existing = await service.RepairAsync(
            new NotebookRepairRequest(source, existingOutput),
            CancellationToken.None);
        await ExecuteAsync(source, "PRAGMA user_version = 3;");
        var future = await service.RepairAsync(
            new NotebookRepairRequest(
                source,
                directory.GetPath("future-output.mnote")),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.Error, Is.EqualTo(NotebookRepairErrorCode.SourceNotFound));
            Assert.That(corrupt.Error, Is.EqualTo(NotebookRepairErrorCode.UnreadableSource));
            Assert.That(legacyResult.Error, Is.EqualTo(NotebookRepairErrorCode.LegacyFormat));
            Assert.That(samePath.Error, Is.EqualTo(NotebookRepairErrorCode.OutputConflict));
            Assert.That(existing.Error, Is.EqualTo(NotebookRepairErrorCode.OutputConflict));
            Assert.That(future.Error, Is.EqualTo(NotebookRepairErrorCode.UnsupportedFormat));
            Assert.That(future.DetectedVersion, Is.EqualTo("3"));
            Assert.That(await File.ReadAllTextAsync(existingOutput), Is.EqualTo("existing"));
        }
    }

    /// <summary>Verifies pre-cancellation opens no source and publishes no output.</summary>
    [Test]
    public async Task RepairAsync_PreCancelled_ThrowsAndLeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("source.mnote");
        var output = directory.GetPath("output.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Cancelled");
        var beforeBytes = await File.ReadAllBytesAsync(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await new NotebookRepairService().RepairAsync(
                new NotebookRepairRequest(source, output),
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(output), Is.False);
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeBytes));
        }
    }

    private static async Task ExecuteAsync(string path, string commandText)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(
            path,
            SqliteOpenMode.ReadWrite);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<SqliteConnection> OpenWriterAsync(string path)
    {
        var connection = SqliteConnectionFactory.Create(path, SqliteOpenMode.ReadWrite);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static IReadOnlyList<string> ExistingSqliteFiles(string databasePath) =>
        SqliteDatabaseFiles.Enumerate(databasePath)
            .Where(File.Exists)
            .ToArray();

    private static async Task<T?> ScalarAsync<T>(string path, string commandText)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(
            path,
            SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync();
        return result is null || result is DBNull ? default : (T)result;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MemoriaNote.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string GetPath(string fileName) => System.IO.Path.Combine(Path, fileName);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
