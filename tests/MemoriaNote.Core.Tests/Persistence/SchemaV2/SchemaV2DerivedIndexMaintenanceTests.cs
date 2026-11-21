using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies PageTags validation and transactional rebuilds.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2DerivedIndexMaintenanceTests
{
    /// <summary>Verifies checking corrupt PageTags is read-only and classifies the mismatch.</summary>
    [Test]
    public async Task CheckAsync_WithCorruptPageTags_LeavesFileSetUnchanged()
    {
        using var database = await TestDatabase.CreateAsync("check-unchanged");
        await database.InsertPageAsync("[\"tag\"]", "searchable body");
        await database.ExecuteAsync("DELETE FROM PageTags;");
        var beforeBytes = await File.ReadAllBytesAsync(database.DatabasePath);
        var beforeFiles = Directory.GetFileSystemEntries(database.DirectoryPath);

        var result = await SchemaV2DerivedIndexMaintenance.CheckAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.PageTags);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Index, Is.EqualTo(SchemaV2DerivedIndexKind.PageTags));
            Assert.That(
                result.Issues[0].Kind,
                Is.EqualTo(SchemaV2DerivedDataIssueKind.MissingRows));
            Assert.That(await File.ReadAllBytesAsync(database.DatabasePath), Is.EqualTo(beforeBytes));
            Assert.That(Directory.GetFileSystemEntries(database.DirectoryPath), Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies rebuilding PageTags preserves authoritative page values.</summary>
    [Test]
    public async Task RebuildAsync_WithCorruptPageTags_RestoresReleaseDerivedData()
    {
        using var database = await TestDatabase.CreateAsync("rebuild-page-tags");
        await database.InsertPageAsync("[\"alpha\",\"beta\"]", "searchable body");
        await database.ExecuteAsync("DELETE FROM PageTags;");
        var authoritativeBefore = await database.ReadAuthoritativePageAsync();

        var result = await SchemaV2DerivedIndexMaintenance.RebuildAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.PageTags);
        var validation = await SchemaV2DatabaseValidator.ValidateAsync(database.DatabasePath);
        var authoritativeAfter = await database.ReadAuthoritativePageAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.WasCommitted, Is.True);
            Assert.That(result.Validation.IsValid, Is.True);
            Assert.That(validation.IsValid, Is.True);
            Assert.That(authoritativeAfter, Is.EqualTo(authoritativeBefore));
            Assert.That(await database.CountRowsAsync("PageTags"), Is.EqualTo(2));
        }
    }

    /// <summary>Verifies failed post-validation rolls back a PageTags rebuild.</summary>
    [Test]
    public async Task RebuildAsync_WhenPostValidationFails_RollsBackChange()
    {
        using var database = await TestDatabase.CreateAsync("rollback");
        await database.InsertPageAsync("[\"missing\"]", "searchable body");
        await database.ExecuteAsync("DELETE FROM PageTags;");
        var injected = SchemaV2DerivedDataValidation.Evaluated(
        [
            new SchemaV2DerivedDataIssue(
                SchemaV2DerivedIndexKind.PageTags,
                SchemaV2DerivedDataIssueKind.MissingRows,
                RowCount: 1,
                "Injected post-validation failure.")
        ]);

        var result = await SchemaV2DerivedIndexMaintenance.RebuildAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.PageTags,
            (_, _, _, _) => Task.FromResult(injected));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.WasCommitted, Is.False);
            Assert.That(await database.CountRowsAsync("PageTags"), Is.Zero);
        }
    }

    /// <summary>Verifies cancellation after rebuilding starts rolls back the change.</summary>
    [Test]
    public async Task RebuildAsync_WhenPostValidationCancels_RollsBackChange()
    {
        using var database = await TestDatabase.CreateAsync("cancel-rollback");
        await database.InsertPageAsync("[\"missing\"]", "searchable body");
        await database.ExecuteAsync("DELETE FROM PageTags;");
        using var cancellation = new CancellationTokenSource();

        Assert.That(
            async () => await SchemaV2DerivedIndexMaintenance.RebuildAsync(
                database.DatabasePath,
                SchemaV2DerivedIndexSelection.PageTags,
                (_, _, _, token) =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Unreachable.");
                },
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(await database.CountRowsAsync("PageTags"), Is.Zero);
    }

    /// <summary>Verifies schema defects are reported rather than repaired.</summary>
    [Test]
    public async Task RebuildAsync_WithMissingPageTagsTable_RefusesSchemaRepair()
    {
        using var database = await TestDatabase.CreateAsync("schema-defect");
        await database.ExecuteAsync("DROP TABLE PageTags;");

        Assert.That(
            async () => await SchemaV2DerivedIndexMaintenance.RebuildAsync(
                database.DatabasePath,
                SchemaV2DerivedIndexSelection.PageTags),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("prerequisite validation failed"));
    }

    /// <summary>Verifies unknown and empty selections are rejected.</summary>
    [Test]
    public void CheckAsync_WithInvalidSelection_RejectsRequest()
    {
        Assert.That(
            async () => await SchemaV2DerivedIndexMaintenance.CheckAsync(
                "unused.mnote",
                SchemaV2DerivedIndexSelection.None),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            async () => await SchemaV2DerivedIndexMaintenance.CheckAsync(
                "unused.mnote",
                (SchemaV2DerivedIndexSelection)2),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private sealed class TestDatabase : IDisposable
    {
        private TestDatabase(string directoryPath, string databasePath)
        {
            DirectoryPath = directoryPath;
            DatabasePath = databasePath;
        }

        internal string DirectoryPath { get; }

        internal string DatabasePath { get; }

        internal static async Task<TestDatabase> CreateAsync(string name)
        {
            var directoryPath = Path.Combine(
                Path.GetTempPath(),
                "MemoriaNote.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directoryPath);
            var databasePath = Path.Combine(directoryPath, name + ".mnote");
            await SchemaV2DatabaseFactory.CreateAsync(databasePath, name);
            return new TestDatabase(directoryPath, databasePath);
        }

        internal async Task InsertPageAsync(string tags, string text)
        {
            await ExecuteAsync(
                $"""
                INSERT INTO Pages(
                    PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
                VALUES (
                    '11111111-1111-1111-1111-111111111111',
                    'page',
                    '2026-10-31 00:00:00.0000000',
                    '2026-10-31 00:00:00.0000000',
                    1,
                    '{tags}',
                    '{text}');
                """);
        }

        internal async Task ExecuteAsync(string commandText)
        {
            await using var connection = await SqliteConnectionFactory.OpenAsync(
                DatabasePath,
                SqliteOpenMode.ReadWrite);
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<long> CountRowsAsync(string table)
        {
            await using var connection = await SqliteConnectionFactory.OpenAsync(
                DatabasePath,
                SqliteOpenMode.ReadOnly);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return (long)(await command.ExecuteScalarAsync())!;
        }

        internal async Task<string> ReadAuthoritativePageAsync()
        {
            await using var connection = await SqliteConnectionFactory.OpenAsync(
                DatabasePath,
                SqliteOpenMode.ReadOnly);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT PageId || '|' || Name || '|' || CreateTime || '|' ||
                       UpdateTime || '|' || Revision || '|' || Tags || '|' || Text
                FROM Pages;
                """;
            return (string)(await command.ExecuteScalarAsync())!;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
