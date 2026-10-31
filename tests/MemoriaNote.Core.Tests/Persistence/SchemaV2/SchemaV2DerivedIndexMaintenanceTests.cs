using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies Schema V2 derived-index validation and transactional rebuilds.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2DerivedIndexMaintenanceTests
{
    /// <summary>Verifies each derived index can be selected independently.</summary>
    [Test]
    public async Task CheckAsync_WithIndependentCorruption_ReportsSelectedIndexOnly()
    {
        using var database = await TestDatabase.CreateAsync("selection");
        await database.InsertPageAsync("[\"tag\"]", "searchable body");
        await database.ExecuteAsync(
            """
            DELETE FROM PageTags;
            INSERT INTO FtsIndexUnicode61(FtsIndexUnicode61) VALUES('delete-all');
            INSERT INTO FtsIndexTrigram(FtsIndexTrigram) VALUES('delete-all');
            """);

        var pageTags = await SchemaV2DerivedIndexMaintenance.CheckAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.PageTags);
        var unicode61 = await SchemaV2DerivedIndexMaintenance.CheckAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.FtsUnicode61);
        var trigram = await SchemaV2DerivedIndexMaintenance.CheckAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.FtsTrigram);

        using (Assert.EnterMultipleScope())
        {
            AssertSingleIssue(pageTags, SchemaV2DerivedIndexKind.PageTags);
            AssertSingleIssue(unicode61, SchemaV2DerivedIndexKind.FtsUnicode61);
            AssertSingleIssue(trigram, SchemaV2DerivedIndexKind.FtsTrigram);
        }
    }

    /// <summary>Verifies a write-capable integrity check never changes the notebook.</summary>
    [Test]
    public async Task CheckAsync_WithCorruptNonEmptyIndexes_LeavesFileSetUnchanged()
    {
        using var database = await TestDatabase.CreateAsync("check-unchanged");
        await database.InsertPageAsync("[\"tag\"]", "searchable body");
        await database.ExecuteAsync(
            """
            DELETE FROM PageTags;
            INSERT INTO FtsIndexUnicode61(FtsIndexUnicode61) VALUES('delete-all');
            INSERT INTO FtsIndexTrigram(FtsIndexTrigram) VALUES('delete-all');
            """);
        var beforeBytes = await File.ReadAllBytesAsync(database.DatabasePath);
        var beforeFiles = Directory.GetFileSystemEntries(database.DirectoryPath);

        var result = await SchemaV2DerivedIndexMaintenance.CheckAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.All);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(3));
            Assert.That(await File.ReadAllBytesAsync(database.DatabasePath),
                Is.EqualTo(beforeBytes));
            Assert.That(Directory.GetFileSystemEntries(database.DirectoryPath),
                Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies a complete rebuild restores the dual-index draft candidate.</summary>
    [Test]
    public async Task RebuildAsync_WithAllIndexesCorrupt_RestoresCompleteCandidate()
    {
        using var database = await TestDatabase.CreateAsync("rebuild-all");
        await database.InsertPageAsync("[\"alpha\",\"beta\"]", "searchable body");
        await database.ExecuteAsync(
            """
            DELETE FROM PageTags;
            INSERT INTO FtsIndexUnicode61(FtsIndexUnicode61) VALUES('delete-all');
            INSERT INTO FtsIndexTrigram(FtsIndexTrigram) VALUES('delete-all');
            """);
        var authoritativeBefore = await database.ReadAuthoritativePageAsync();

        var result = await SchemaV2DerivedIndexMaintenance.RebuildAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.All);
        var validation = await SchemaV2DatabaseValidator.ValidateAsync(database.DatabasePath);
        var authoritativeAfter = await database.ReadAuthoritativePageAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.WasCommitted, Is.True);
            Assert.That(result.Validation.IsValid, Is.True);
            Assert.That(validation.IsValid, Is.True);
            Assert.That(authoritativeAfter, Is.EqualTo(authoritativeBefore));
            Assert.That(await database.CountRowsAsync("PageTags"), Is.EqualTo(2));
            Assert.That(
                await database.CountMatchesAsync("FtsIndexUnicode61", "searchable"),
                Is.EqualTo(1));
            Assert.That(
                await database.CountMatchesAsync("FtsIndexTrigram", "searchable"),
                Is.EqualTo(1));
        }
    }

    /// <summary>Verifies rebuilding one FTS index does not repair the other index.</summary>
    [Test]
    public async Task RebuildAsync_WithUnicode61Selected_LeavesTrigramUnchanged()
    {
        using var database = await TestDatabase.CreateAsync("rebuild-selected");
        await database.InsertPageAsync("[]", "searchable body");
        await database.ExecuteAsync(
            """
            INSERT INTO FtsIndexUnicode61(FtsIndexUnicode61) VALUES('delete-all');
            INSERT INTO FtsIndexTrigram(FtsIndexTrigram) VALUES('delete-all');
            """);

        var result = await SchemaV2DerivedIndexMaintenance.RebuildAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.FtsUnicode61);
        var unicode61 = await SchemaV2DerivedIndexMaintenance.CheckAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.FtsUnicode61);
        var trigram = await SchemaV2DerivedIndexMaintenance.CheckAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.FtsTrigram);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.WasCommitted, Is.True);
            Assert.That(unicode61.IsValid, Is.True);
            AssertSingleIssue(trigram, SchemaV2DerivedIndexKind.FtsTrigram);
        }
    }

    /// <summary>Verifies failed post-validation rolls back every selected rebuild.</summary>
    [Test]
    public async Task RebuildAsync_WhenPostValidationFails_RollsBackEveryChange()
    {
        using var database = await TestDatabase.CreateAsync("rollback");
        await database.InsertPageAsync("[\"missing\"]", "searchable body");
        await database.ExecuteAsync(
            """
            DELETE FROM PageTags;
            INSERT INTO FtsIndexUnicode61(FtsIndexUnicode61) VALUES('delete-all');
            """);
        var injected = SchemaV2DerivedDataValidation.Evaluated(
        [
            new SchemaV2DerivedDataIssue(
                SchemaV2DerivedIndexKind.FtsUnicode61,
                SchemaV2DerivedDataIssueKind.ContentMismatch,
                RowCount: null,
                "Injected post-validation failure.")
        ]);

        var result = await SchemaV2DerivedIndexMaintenance.RebuildAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.PageTags |
            SchemaV2DerivedIndexSelection.FtsUnicode61,
            (_, _, _, _) => Task.FromResult(injected));
        var actual = await SchemaV2DerivedIndexMaintenance.CheckAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.PageTags |
            SchemaV2DerivedIndexSelection.FtsUnicode61);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.WasCommitted, Is.False);
            Assert.That(await database.CountRowsAsync("PageTags"), Is.Zero);
            Assert.That(actual.Issues, Has.Some.Matches<SchemaV2DerivedDataIssue>(
                issue => issue is not null &&
                         issue.Index == SchemaV2DerivedIndexKind.PageTags));
            Assert.That(actual.Issues, Has.Some.Matches<SchemaV2DerivedDataIssue>(
                issue => issue is not null &&
                         issue.Index == SchemaV2DerivedIndexKind.FtsUnicode61));
        }
    }

    /// <summary>Verifies cancellation after rebuilding starts rolls back every change.</summary>
    [Test]
    public async Task RebuildAsync_WhenPostValidationCancels_RollsBackEveryChange()
    {
        using var database = await TestDatabase.CreateAsync("cancel-rollback");
        await database.InsertPageAsync("[\"missing\"]", "searchable body");
        await database.ExecuteAsync(
            """
            DELETE FROM PageTags;
            INSERT INTO FtsIndexUnicode61(FtsIndexUnicode61) VALUES('delete-all');
            """);
        using var cancellation = new CancellationTokenSource();

        Assert.That(
            async () => await SchemaV2DerivedIndexMaintenance.RebuildAsync(
                database.DatabasePath,
                SchemaV2DerivedIndexSelection.PageTags |
                SchemaV2DerivedIndexSelection.FtsUnicode61,
                (_, _, _, token) =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Unreachable.");
                },
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        var actual = await SchemaV2DerivedIndexMaintenance.CheckAsync(
            database.DatabasePath,
            SchemaV2DerivedIndexSelection.PageTags |
            SchemaV2DerivedIndexSelection.FtsUnicode61);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await database.CountRowsAsync("PageTags"), Is.Zero);
            Assert.That(actual.Issues, Has.Count.EqualTo(2));
        }
    }

    /// <summary>Verifies schema defects are reported rather than repaired.</summary>
    [Test]
    public async Task RebuildAsync_WithMissingIndexTable_RefusesSchemaRepair()
    {
        using var database = await TestDatabase.CreateAsync("schema-defect");
        await database.ExecuteAsync("DROP TABLE FtsIndexTrigram;");

        Assert.That(
            async () => await SchemaV2DerivedIndexMaintenance.RebuildAsync(
                database.DatabasePath,
                SchemaV2DerivedIndexSelection.All),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("prerequisite validation failed"));
    }

    /// <summary>Verifies pre-cancellation leaves the database and file set unchanged.</summary>
    [Test]
    public async Task RebuildAsync_WhenPreCancelled_LeavesDatabaseUnchanged()
    {
        using var database = await TestDatabase.CreateAsync("cancelled");
        await database.InsertPageAsync("[\"tag\"]", "body");
        var beforeBytes = await File.ReadAllBytesAsync(database.DatabasePath);
        var beforeFiles = Directory.GetFileSystemEntries(database.DirectoryPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await SchemaV2DerivedIndexMaintenance.RebuildAsync(
                database.DatabasePath,
                SchemaV2DerivedIndexSelection.All,
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(database.DatabasePath),
                Is.EqualTo(beforeBytes));
            Assert.That(Directory.GetFileSystemEntries(database.DirectoryPath),
                Is.EqualTo(beforeFiles));
        }
    }

    private static void AssertSingleIssue(
        SchemaV2DerivedDataValidation validation,
        SchemaV2DerivedIndexKind index)
    {
        Assert.That(validation.Issues, Has.Count.EqualTo(1));
        Assert.That(validation.Issues[0].Index, Is.EqualTo(index));
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
            await SchemaV2DatabaseFactory.CreateAsync(databasePath, name, name);
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

        internal async Task<long> CountMatchesAsync(string table, string query)
        {
            await using var connection = await SqliteConnectionFactory.OpenAsync(
                DatabasePath,
                SqliteOpenMode.ReadOnly);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {table} MATCH $query;";
            command.Parameters.AddWithValue("$query", query);
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
