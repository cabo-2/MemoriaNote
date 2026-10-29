using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies the temporary dual-index Schema V2 candidate.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2DualIndexTests
{
    private static readonly string[] IndexTables =
    [
        "FtsIndexUnicode61",
        "FtsIndexTrigram"
    ];

    /// <summary>Verifies insert, update, and delete triggers synchronize both FTS5 indexes.</summary>
    [Test]
    public async Task Triggers_SynchronizeBothIndexesAcrossReopen()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("dual-index.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "dual-index", "Dual index");

        await ExecuteAsync(
            databasePath,
            """
            INSERT INTO Pages(
                PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
            VALUES (
                '11111111-1111-1111-1111-111111111111',
                'alpha-title',
                '2026-10-29 12:00:00.0000000',
                '2026-10-29 12:00:00.0000000',
                1,
                '[]',
                'first alpha body');
            """);

        foreach (var table in IndexTables)
            Assert.That(await CountMatchesAsync(databasePath, table, "alpha"), Is.EqualTo(1));

        await ExecuteAsync(
            databasePath,
            """
            UPDATE Pages
            SET Name = 'bravo-title',
                Text = 'second bravo body',
                UpdateTime = '2026-10-29 12:01:00.0000000',
                Revision = 2
            WHERE PageId = '11111111-1111-1111-1111-111111111111';
            """);

        foreach (var table in IndexTables)
        {
            Assert.That(await CountMatchesAsync(databasePath, table, "alpha"), Is.Zero);
            Assert.That(await CountMatchesAsync(databasePath, table, "bravo"), Is.EqualTo(1));
        }

        await ExecuteAsync(
            databasePath,
            "DELETE FROM Pages WHERE PageId = '11111111-1111-1111-1111-111111111111';");

        foreach (var table in IndexTables)
            Assert.That(await CountMatchesAsync(databasePath, table, "bravo"), Is.Zero);
    }

    /// <summary>Verifies an index synchronization failure rolls back the authoritative write.</summary>
    [Test]
    public async Task Insert_WhenTrigramIndexIsMissing_RollsBackEveryWrite()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("atomic-sync.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "atomic-sync", "Atomic sync");
        await ExecuteAsync(databasePath, "DROP TABLE FtsIndexTrigram;");

        Assert.That(
            async () => await ExecuteAsync(
                databasePath,
                """
                INSERT INTO Pages(
                    PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
                VALUES (
                    '22222222-2222-2222-2222-222222222222',
                    'rollback-title',
                    '2026-10-29 12:00:00.0000000',
                    '2026-10-29 12:00:00.0000000',
                    1,
                    '[]',
                    'rollback body');
                """),
            Throws.TypeOf<SqliteException>());

        Assert.That(await CountRowsAsync(databasePath, "Pages"), Is.Zero);
        Assert.That(
            await CountMatchesAsync(databasePath, "FtsIndexUnicode61", "rollback"),
            Is.Zero);
    }

    private static async Task ExecuteAsync(string databasePath, string commandText)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(
            databasePath,
            SqliteOpenMode.ReadWrite);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountMatchesAsync(
        string databasePath,
        string table,
        string query)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(
            databasePath,
            SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {table} MATCH $query;";
        command.Parameters.AddWithValue("$query", query);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountRowsAsync(string databasePath, string table)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(
            databasePath,
            SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)(await command.ExecuteScalarAsync())!;
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
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
