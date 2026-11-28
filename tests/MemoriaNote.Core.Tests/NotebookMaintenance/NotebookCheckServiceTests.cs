using MemoriaNote.Core.Tests.Infrastructure;
using MemoriaNote.NotebookMaintenance;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.NotebookMaintenance;

/// <summary>Verifies the layered read-only notebook check operation.</summary>
[TestFixture]
public sealed class NotebookCheckServiceTests
{
    /// <summary>Fixes the released busy timeout without waiting for it to elapse.</summary>
    [Test]
    public void BusyTimeout_UsesThirtySecondProductionDefault()
    {
        Assert.That(SchemaV2MaintenanceDefaults.BusyTimeoutSeconds, Is.EqualTo(30));
    }

    /// <summary>Verifies a WAL writer does not leak uncommitted values into the check snapshot.</summary>
    [Test]
    public async Task CheckAsync_ConcurrentWalWriter_ReadsCommittedSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("wal-writer.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "WAL writer");
        await ExecuteAsync(source, "PRAGMA journal_mode = WAL;");
        var beforeMain = await File.ReadAllBytesAsync(source);
        NotebookCheckResult result;
        IReadOnlyList<string> beforeFiles;
        IReadOnlyList<string> afterFiles;
        await using (var writer = await OpenWriterAsync(source))
        {
            await ExecuteAsync(
                writer,
                "BEGIN IMMEDIATE; " +
                "UPDATE Metadata SET Value = 'uncommitted-invalid' WHERE Key = 'ReadOnly';");
            beforeFiles = ExistingSqliteFiles(source);

            result = await new NotebookCheckService().CheckAsync(
                new NotebookCheckRequest(source),
                CancellationToken.None);
            afterFiles = ExistingSqliteFiles(source);
            await ExecuteAsync(writer, "ROLLBACK;");
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompleted, Is.True);
            Assert.That(result.Report!.IsValid, Is.True);
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeMain));
            Assert.That(afterFiles, Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies a locked source maps to busy without a wall-clock timeout test.</summary>
    [Test]
    public async Task CheckAsync_ExclusiveWriter_ReturnsBusyWithoutChangingFileSet()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("busy.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Busy");
        await ExecuteAsync(source, "PRAGMA journal_mode = DELETE;");
        var beforeMain = await File.ReadAllBytesAsync(source);
        NotebookCheckResult result;
        IReadOnlyList<string> beforeFiles;
        IReadOnlyList<string> afterFiles;
        await using (var writer = await OpenWriterAsync(source))
        {
            await ExecuteAsync(
                writer,
                "BEGIN EXCLUSIVE; " +
                "UPDATE Metadata SET Value = 'locked' WHERE Key = 'Title';");
            beforeFiles = ExistingSqliteFiles(source);

            result = await new NotebookCheckService(busyTimeoutSeconds: 1).CheckAsync(
                new NotebookCheckRequest(source),
                CancellationToken.None);
            afterFiles = ExistingSqliteFiles(source);
            await ExecuteAsync(writer, "ROLLBACK;");
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.EqualTo(NotebookCheckErrorCode.SourceBusy));
            Assert.That(result.Report, Is.Null);
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeMain));
            Assert.That(afterFiles, Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies a release schema v2 notebook passes every layer unchanged.</summary>
    [Test]
    public async Task CheckAsync_ValidNotebook_PassesEveryLayerWithoutChangingSource()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("valid.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Valid");
        var beforeBytes = await File.ReadAllBytesAsync(source);
        var beforeFiles = Directory.GetFileSystemEntries(directory.Path);

        var result = await new NotebookCheckService().CheckAsync(
            new NotebookCheckRequest(source),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompleted, Is.True);
            Assert.That(result.Report!.IsValid, Is.True);
            Assert.That(result.Report.FormatVersion, Is.EqualTo(2));
            Assert.That(result.Report.SourceWasModified, Is.False);
            Assert.That(result.Report.Issues, Is.Empty);
            Assert.That(
                result.Report.Layers.Select(layer => layer.Status),
                Is.All.EqualTo(NotebookCheckLayerStatus.Passed));
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeBytes));
            Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies a missing trigger is reported separately from non-trigger schema.</summary>
    [Test]
    public async Task CheckAsync_MissingTrigger_FailsTriggerLayerAndStopsValueChecks()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("trigger.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Trigger");
        await ExecuteAsync(source, "DROP TRIGGER Pages_TagsUpdate;");

        var result = await new NotebookCheckService().CheckAsync(
            new NotebookCheckRequest(source),
            CancellationToken.None);

        var report = result.Report!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompleted, Is.True);
            Assert.That(report.IsValid, Is.False);
            Assert.That(Layer(report, NotebookCheckLayerKind.Schema).Status,
                Is.EqualTo(NotebookCheckLayerStatus.Passed));
            Assert.That(Layer(report, NotebookCheckLayerKind.Triggers).Status,
                Is.EqualTo(NotebookCheckLayerStatus.Failed));
            Assert.That(Layer(report, NotebookCheckLayerKind.Triggers).Issues,
                Has.One.Property("Code").EqualTo(NotebookCheckIssueCode.TriggerMissing));
            Assert.That(Layer(report, NotebookCheckLayerKind.AuthoritativeValues).Status,
                Is.EqualTo(NotebookCheckLayerStatus.NotEvaluated));
            Assert.That(Layer(report, NotebookCheckLayerKind.DerivedData).Status,
                Is.EqualTo(NotebookCheckLayerStatus.NotEvaluated));
        }
    }

    /// <summary>Verifies a non-trigger DDL defect remains in the schema layer.</summary>
    [Test]
    public async Task CheckAsync_MalformedTable_FailsSchemaButNotTriggerLayer()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("schema.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Schema");
        await ExecuteAsync(
            source,
            "DROP TABLE Metadata; CREATE TABLE Metadata (Other TEXT NULL);");

        var result = await new NotebookCheckService().CheckAsync(
            new NotebookCheckRequest(source),
            CancellationToken.None);

        var report = result.Report!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Layer(report, NotebookCheckLayerKind.Schema).Status,
                Is.EqualTo(NotebookCheckLayerStatus.Failed));
            Assert.That(Layer(report, NotebookCheckLayerKind.Schema).Issues,
                Has.Some.Property("Code").EqualTo(
                    NotebookCheckIssueCode.SchemaObjectMismatch));
            Assert.That(Layer(report, NotebookCheckLayerKind.Triggers).Status,
                Is.EqualTo(NotebookCheckLayerStatus.Passed));
            Assert.That(Layer(report, NotebookCheckLayerKind.AuthoritativeValues).Status,
                Is.EqualTo(NotebookCheckLayerStatus.NotEvaluated));
        }
    }

    /// <summary>Verifies foreign-key defects are reported by the integrity layer.</summary>
    [Test]
    public async Task CheckAsync_ForeignKeyViolation_FailsIntegrityLayer()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("integrity.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Integrity");
        await ExecuteAsync(
            source,
            """
            PRAGMA foreign_keys = OFF;
            INSERT INTO PageTags(PageInternalId, Label) VALUES (999, 'orphan');
            """);

        var result = await new NotebookCheckService().CheckAsync(
            new NotebookCheckRequest(source),
            CancellationToken.None);

        var report = result.Report!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Layer(report, NotebookCheckLayerKind.Integrity).Status,
                Is.EqualTo(NotebookCheckLayerStatus.Failed));
            Assert.That(Layer(report, NotebookCheckLayerKind.Integrity).Issues,
                Has.One.Property("Code").EqualTo(
                    NotebookCheckIssueCode.ForeignKeyViolation));
            Assert.That(Layer(report, NotebookCheckLayerKind.AuthoritativeValues).Status,
                Is.EqualTo(NotebookCheckLayerStatus.NotEvaluated));
            Assert.That(Layer(report, NotebookCheckLayerKind.DerivedData).Status,
                Is.EqualTo(NotebookCheckLayerStatus.NotEvaluated));
        }
    }

    /// <summary>Verifies a PageTags mismatch is a derived-data issue.</summary>
    [Test]
    public async Task CheckAsync_PageTagsMismatch_FailsOnlyDerivedLayer()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("derived.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Derived");
        await ExecuteAsync(
            source,
            """
            INSERT INTO Pages(PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
            VALUES ('11111111-1111-4111-8111-111111111111', 'page',
                    '2026-11-01 00:00:00.0000000', '2026-11-01 00:00:00.0000000',
                    1, '["tag"]', 'private body');
            DELETE FROM PageTags;
            """);

        var result = await new NotebookCheckService().CheckAsync(
            new NotebookCheckRequest(source),
            CancellationToken.None);

        var report = result.Report!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.IsValid, Is.False);
            Assert.That(report.Layers.Take(5).Select(layer => layer.Status),
                Is.All.EqualTo(NotebookCheckLayerStatus.Passed));
            Assert.That(Layer(report, NotebookCheckLayerKind.DerivedData).Issues,
                Has.One.Property("Code").EqualTo(
                    NotebookCheckIssueCode.DerivedPageTagsMissing));
            Assert.That(
                string.Join(" ", report.Issues.SelectMany(issue =>
                    new[] { issue.Location, issue.Expected, issue.Actual })),
                Does.Not.Contain("private body"));
        }
    }

    /// <summary>Verifies private metadata values are replaced by safe summaries.</summary>
    [Test]
    public async Task CheckAsync_InvalidMetadata_DoesNotExposeStoredValue()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("metadata.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Metadata");
        await ExecuteAsync(
            source,
            "UPDATE Metadata SET Value = 'private-invalid-value' WHERE Key = 'ReadOnly';");

        var result = await new NotebookCheckService().CheckAsync(
            new NotebookCheckRequest(source),
            CancellationToken.None);

        var issue = Layer(
                result.Report!,
                NotebookCheckLayerKind.AuthoritativeValues)
            .Issues.Single(candidate =>
                candidate.Code == NotebookCheckIssueCode.MetadataInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issue.Location, Is.EqualTo("Metadata[key=ReadOnly]"));
            Assert.That(issue.Actual, Does.Not.Contain("private-invalid-value"));
        }
    }

    /// <summary>Verifies target and format failures remain separate from check issues.</summary>
    [Test]
    public async Task CheckAsync_TargetFailures_ReturnClassifiedFatalErrors()
    {
        using var directory = new TemporaryDirectory();
        var service = new NotebookCheckService();
        var missing = await service.CheckAsync(
            new NotebookCheckRequest(directory.GetPath("missing.mnote")),
            CancellationToken.None);
        var invalidTarget = await service.CheckAsync(
            new NotebookCheckRequest(directory.Path),
            CancellationToken.None);
        var corruptPath = directory.GetPath("corrupt.mnote");
        await File.WriteAllTextAsync(corruptPath, "not sqlite");
        var corruptBytes = await File.ReadAllBytesAsync(corruptPath);
        var corruptFiles = Directory.GetFileSystemEntries(directory.Path);
        var corrupt = await service.CheckAsync(
            new NotebookCheckRequest(corruptPath),
            CancellationToken.None);

        using var legacy = new TemporaryNotebookDatabase();
        legacy.CreateNotebook("legacy", "Legacy");
        var legacyResult = await service.CheckAsync(
            new NotebookCheckRequest(legacy.DatabasePath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.Error, Is.EqualTo(NotebookCheckErrorCode.SourceNotFound));
            Assert.That(invalidTarget.Error, Is.EqualTo(NotebookCheckErrorCode.InvalidTarget));
            Assert.That(corrupt.Error, Is.EqualTo(NotebookCheckErrorCode.UnreadableSource));
            Assert.That(legacyResult.Error, Is.EqualTo(NotebookCheckErrorCode.LegacyFormat));
            Assert.That(missing.Report, Is.Null);
            Assert.That(corrupt.Report, Is.Null);
            Assert.That(await File.ReadAllBytesAsync(corruptPath), Is.EqualTo(corruptBytes));
            Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.EqualTo(corruptFiles));
        }
    }

    /// <summary>Verifies pre-cancellation opens no notebook and produces no report.</summary>
    [Test]
    public async Task CheckAsync_PreCancelled_ThrowsCancellationWithoutChangingSource()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("cancelled.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(source, "Cancelled");
        var beforeBytes = await File.ReadAllBytesAsync(source);
        var beforeFiles = Directory.GetFileSystemEntries(directory.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await new NotebookCheckService().CheckAsync(
                new NotebookCheckRequest(source),
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeBytes));
            Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.EqualTo(beforeFiles));
        }
    }

    private static NotebookCheckLayerReport Layer(
        NotebookCheckReport report,
        NotebookCheckLayerKind layer) =>
        report.Layers.Single(candidate => candidate.Layer == layer);

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
