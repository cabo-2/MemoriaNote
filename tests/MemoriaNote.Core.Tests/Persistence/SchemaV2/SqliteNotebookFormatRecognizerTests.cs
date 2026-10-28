using MemoriaNote.Core.Tests.Infrastructure;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies read-only notebook format recognition.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SqliteNotebookFormatRecognizerTests
{
    /// <summary>Verifies a missing path is classified without creating a file.</summary>
    [Test]
    public async Task RecognizeAsync_MissingPath_ReturnsMissingWithoutOutput()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("missing.mnote");

        var result = await SqliteNotebookFormatRecognizer.RecognizeAsync(databasePath);

        AssertRecognition(
            result,
            NotebookFormatClassification.Missing,
            NotebookFormatRecognitionReason.MissingPath,
            NotebookFormatRecommendedAction.None,
            null);
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
    }

    /// <summary>Verifies the exact schema v1 identity receives migration guidance.</summary>
    [Test]
    public async Task RecognizeAsync_SchemaV1_ReturnsLegacyWithMigrationAction()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("work", "Work");
        var before = await CaptureAsync(database.DatabasePath, database.DirectoryPath);

        var result = await SqliteNotebookFormatRecognizer.RecognizeAsync(
            database.DatabasePath);

        AssertRecognition(
            result,
            NotebookFormatClassification.Legacy,
            NotebookFormatRecognitionReason.SchemaV1Identity,
            NotebookFormatRecommendedAction.Migrate,
            "1");
        await AssertUnchangedAsync(
            database.DatabasePath,
            database.DirectoryPath,
            before);
    }

    /// <summary>Verifies the exact schema v2 identity is current and needs no action.</summary>
    [Test]
    public async Task RecognizeAsync_SchemaV2_ReturnsCurrentWithoutChanges()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("current.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "work", "Work");
        var before = await CaptureAsync(databasePath, directory.Path);

        var result = await SqliteNotebookFormatRecognizer.RecognizeAsync(databasePath);

        AssertRecognition(
            result,
            NotebookFormatClassification.Current,
            NotebookFormatRecognitionReason.SchemaV2Identity,
            NotebookFormatRecommendedAction.None,
            "2");
        await AssertUnchangedAsync(databasePath, directory.Path, before);
    }

    /// <summary>Verifies newer schema v2 generations are unsupported.</summary>
    [Test]
    public async Task RecognizeAsync_NewerSchemaV2_ReturnsUnsupported()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("future-v2.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "work", "Work");
        await ExecuteAsync(
            databasePath,
            "PRAGMA user_version = 3; DROP TABLE Metadata;");

        var result = await SqliteNotebookFormatRecognizer.RecognizeAsync(databasePath);

        AssertRecognition(
            result,
            NotebookFormatClassification.Unsupported,
            NotebookFormatRecognitionReason.NewerSchemaV2Version,
            NotebookFormatRecommendedAction.None,
            "3");
    }

    /// <summary>Verifies a newer version on the exact schema v1 lineage is unsupported.</summary>
    [Test]
    public async Task RecognizeAsync_NewerSchemaV1_ReturnsUnsupported()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("work", "Work");
        await ExecuteAsync(
            database.DatabasePath,
            "UPDATE Metadata SET Value = '2' WHERE Key = 'Version';");

        var result = await SqliteNotebookFormatRecognizer.RecognizeAsync(
            database.DatabasePath);

        AssertRecognition(
            result,
            NotebookFormatClassification.Unsupported,
            NotebookFormatRecognitionReason.NewerSchemaV1Version,
            NotebookFormatRecommendedAction.None,
            "2");
    }

    /// <summary>Verifies schema v1 requires the complete migration identity.</summary>
    [Test]
    public async Task RecognizeAsync_IncompleteSchemaV1Identity_ReturnsCorrupt()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("work", "Work");
        await ExecuteAsync(
            database.DatabasePath,
            """
            DELETE FROM __EFMigrationsHistory
            WHERE MigrationId = '20230404004813_FtsIndexTable';
            """);

        var result = await SqliteNotebookFormatRecognizer.RecognizeAsync(
            database.DatabasePath);

        AssertRecognition(
            result,
            NotebookFormatClassification.Corrupt,
            NotebookFormatRecognitionReason.ConflictingIdentity,
            NotebookFormatRecommendedAction.None,
            null);
    }

    /// <summary>Verifies contradictory schema v2 identity is corrupt.</summary>
    [Test]
    public async Task RecognizeAsync_ConflictingSchemaV2Identity_ReturnsCorrupt()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("conflicting.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "work", "Work");
        await ExecuteAsync(
            databasePath,
            "UPDATE Metadata SET Value = '1' WHERE Key = 'Version';");

        var result = await SqliteNotebookFormatRecognizer.RecognizeAsync(databasePath);

        AssertRecognition(
            result,
            NotebookFormatClassification.Corrupt,
            NotebookFormatRecognitionReason.ConflictingIdentity,
            NotebookFormatRecommendedAction.None,
            null);
    }

    /// <summary>Verifies arbitrary SQLite databases are not accepted as notebooks.</summary>
    [Test]
    public async Task RecognizeAsync_UnrecognizedSqlite_ReturnsCorrupt()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("other.sqlite");
        await ExecuteAsync(
            databasePath,
            "CREATE TABLE Arbitrary (Value TEXT NULL);",
            SqliteOpenMode.ReadWriteCreate);

        var result = await SqliteNotebookFormatRecognizer.RecognizeAsync(databasePath);

        AssertRecognition(
            result,
            NotebookFormatClassification.Corrupt,
            NotebookFormatRecognitionReason.UnrecognizedSqlite,
            NotebookFormatRecommendedAction.None,
            null);
    }

    /// <summary>Verifies a non-SQLite input is corrupt and remains unchanged.</summary>
    [Test]
    public async Task RecognizeAsync_NonSqliteInput_ReturnsCorruptWithoutChanges()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("invalid.mnote");
        await File.WriteAllTextAsync(databasePath, "not a SQLite database");
        var before = await CaptureAsync(databasePath, directory.Path);

        var result = await SqliteNotebookFormatRecognizer.RecognizeAsync(databasePath);

        AssertRecognition(
            result,
            NotebookFormatClassification.Corrupt,
            NotebookFormatRecognitionReason.UnreadableIdentity,
            NotebookFormatRecommendedAction.None,
            null);
        await AssertUnchangedAsync(databasePath, directory.Path, before);
    }

    /// <summary>Verifies recognition does not perform schema contract validation.</summary>
    [Test]
    public async Task RecognizeAsync_CurrentIdentityWithSchemaDefect_RemainsCurrent()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("schema-defect.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "work", "Work");
        await ExecuteAsync(databasePath, "DROP INDEX IX_Pages_Name;");

        var result = await SqliteNotebookFormatRecognizer.RecognizeAsync(databasePath);

        AssertRecognition(
            result,
            NotebookFormatClassification.Current,
            NotebookFormatRecognitionReason.SchemaV2Identity,
            NotebookFormatRecommendedAction.None,
            "2");
    }

    /// <summary>Verifies pre-cancellation does not open or change the input.</summary>
    [Test]
    public async Task RecognizeAsync_PreCancelled_LeavesInputUnchanged()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("cancelled.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "work", "Work");
        var before = await CaptureAsync(databasePath, directory.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await SqliteNotebookFormatRecognizer.RecognizeAsync(
                databasePath,
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        await AssertUnchangedAsync(databasePath, directory.Path, before);
    }

    private static void AssertRecognition(
        NotebookFormatRecognition result,
        NotebookFormatClassification classification,
        NotebookFormatRecognitionReason reason,
        NotebookFormatRecommendedAction recommendedAction,
        string? detectedVersion)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Classification, Is.EqualTo(classification));
            Assert.That(result.Reason, Is.EqualTo(reason));
            Assert.That(result.RecommendedAction, Is.EqualTo(recommendedAction));
            Assert.That(result.DetectedVersion, Is.EqualTo(detectedVersion));
        }
    }

    private static async Task ExecuteAsync(
        string databasePath,
        string commandText,
        SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(
            databasePath,
            mode);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<FileSetSnapshot> CaptureAsync(
        string databasePath,
        string directoryPath)
    {
        return new FileSetSnapshot(
            await File.ReadAllBytesAsync(databasePath),
            Directory.GetFileSystemEntries(directoryPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray());
    }

    private static async Task AssertUnchangedAsync(
        string databasePath,
        string directoryPath,
        FileSetSnapshot before)
    {
        var after = await CaptureAsync(databasePath, directoryPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.DatabaseBytes, Is.EqualTo(before.DatabaseBytes));
            Assert.That(after.DirectoryEntries, Is.EqualTo(before.DirectoryEntries));
        }
    }

    private sealed record FileSetSnapshot(
        byte[] DatabaseBytes,
        IReadOnlyList<string> DirectoryEntries);

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
