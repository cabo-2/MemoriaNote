using MemoriaNote.Core.Tests.Infrastructure;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.Repositories;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies direct-SQL Schema V2 page operations and repository contracts.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2PageRepositoryTests
{
    /// <summary>Verifies the provider advertises only Schema V2 operational capabilities.</summary>
    [Test]
    public void Provider_ExposesVersionedReadWritePorts()
    {
        var provider = new SchemaV2RepositoryProvider(
            new FixedClock(new DateTimeOffset(2026, 10, 30, 1, 0, 0, TimeSpan.Zero)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.Descriptor.FormatVersion, Is.EqualTo(NotebookFormatVersion.Version2));
            Assert.That(
                provider.Descriptor.Capabilities,
                Is.EqualTo(
                    NotebookRepositoryCapabilities.OperationalRead |
                    NotebookRepositoryCapabilities.OperationalWrite));
            Assert.That(provider.Descriptor.Capabilities.HasFlag(
                NotebookRepositoryCapabilities.MigrationRead), Is.False);
            Assert.That(provider.MetadataReader, Is.SameAs(provider.MetadataWriter));
            Assert.That(provider.PageReader, Is.SameAs(provider.PageWriter));
        }
    }

    /// <summary>Verifies CRUD updates authoritative data and both derived index kinds.</summary>
    [Test]
    public async Task Crud_MaintainsRevisionTagsAndFtsIndexes()
    {
        using var database = await TestDatabase.CreateAsync("crud");
        var clock = new FixedClock(
            new DateTimeOffset(2026, 10, 30, 1, 2, 3, TimeSpan.Zero).AddTicks(4567890));
        var repository = new SchemaV2PageRepository(clock);

        var created = await repository.CreateAsync(
            database.NotebookId,
            new RepositoryPageCreateRequest(
                "alpha",
                "first searchable body",
                ["日本語", "alpha", "alpha"]),
            CancellationToken.None);

        Assert.That(created.Status, Is.EqualTo(RepositoryPageWriteStatus.Success));
        Assert.That(created.Page, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Page.Name, Is.EqualTo("alpha"));
            Assert.That(created.Page.Revision, Is.EqualTo(1));
            Assert.That(created.Page.Tags, Is.EqualTo(new[] { "alpha", "日本語" }));
            Assert.That(created.Page.CreateTimeUtc, Is.EqualTo(clock.UtcNow));
            Assert.That(created.Page.UpdateTimeUtc, Is.EqualTo(clock.UtcNow));
            Assert.That(await database.ReadTagsAsync(created.Page.PageId),
                Is.EqualTo(new[] { "alpha", "日本語" }));
            Assert.That(await database.CountMatchesAsync("FtsIndexUnicode61", "searchable"),
                Is.EqualTo(1));
            Assert.That(await database.CountMatchesAsync("FtsIndexTrigram", "searchable"),
                Is.EqualTo(1));
        }

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var updated = await repository.UpdateAsync(
            database.NotebookId,
            created.Page.PageId,
            expectedRevision: 1,
            new RepositoryPageUpdateRequest(
                "bravo",
                "second updated body",
                ["reviewed"]),
            CancellationToken.None);

        Assert.That(updated.Status, Is.EqualTo(RepositoryPageWriteStatus.Success));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Page.PageId, Is.EqualTo(created.Page.PageId));
            Assert.That(updated.Page.Name, Is.EqualTo("bravo"));
            Assert.That(updated.Page.Revision, Is.EqualTo(2));
            Assert.That(updated.Page.CreateTimeUtc, Is.EqualTo(created.Page.CreateTimeUtc));
            Assert.That(updated.Page.UpdateTimeUtc, Is.EqualTo(clock.UtcNow));
            Assert.That(updated.Page.Tags, Is.EqualTo(new[] { "reviewed" }));
            Assert.That(await database.ReadTagsAsync(updated.Page.PageId),
                Is.EqualTo(new[] { "reviewed" }));
            Assert.That(await database.CountMatchesAsync("FtsIndexUnicode61", "searchable"),
                Is.Zero);
            Assert.That(await database.CountMatchesAsync("FtsIndexTrigram", "searchable"),
                Is.Zero);
            Assert.That(await database.CountMatchesAsync("FtsIndexUnicode61", "updated"),
                Is.EqualTo(1));
            Assert.That(await database.CountMatchesAsync("FtsIndexTrigram", "updated"),
                Is.EqualTo(1));
        }

        var deleted = await repository.DeleteAsync(
            database.NotebookId,
            updated.Page.PageId,
            expectedRevision: 2,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Status, Is.EqualTo(RepositoryPageWriteStatus.Success));
            Assert.That(await repository.CountAsync(database.NotebookId, CancellationToken.None),
                Is.Zero);
            Assert.That(await database.ReadTagsAsync(updated.Page.PageId), Is.Empty);
            Assert.That(await database.CountMatchesAsync("FtsIndexUnicode61", "updated"),
                Is.Zero);
            Assert.That(await database.CountMatchesAsync("FtsIndexTrigram", "updated"),
                Is.Zero);
        }
    }

    /// <summary>Verifies duplicate names and stale revisions are classified without writes.</summary>
    [Test]
    public async Task Writes_ClassifyNameRevisionAndMissingConflicts()
    {
        using var database = await TestDatabase.CreateAsync("conflicts");
        var clock = new FixedClock(DateTimeOffset.Parse("2026-10-30T02:00:00Z"));
        var repository = new SchemaV2PageRepository(clock);
        var first = (await repository.CreateAsync(
            database.NotebookId,
            new RepositoryPageCreateRequest("first", "one", []),
            CancellationToken.None)).Page;
        var second = (await repository.CreateAsync(
            database.NotebookId,
            new RepositoryPageCreateRequest("second", "two", []),
            CancellationToken.None)).Page;

        var duplicateCreate = await repository.CreateAsync(
            database.NotebookId,
            new RepositoryPageCreateRequest("first", "duplicate", []),
            CancellationToken.None);
        var duplicateRename = await repository.UpdateAsync(
            database.NotebookId,
            second.PageId,
            second.Revision,
            new RepositoryPageUpdateRequest("first", "two", []),
            CancellationToken.None);
        var staleUpdate = await repository.UpdateAsync(
            database.NotebookId,
            first.PageId,
            expectedRevision: 2,
            new RepositoryPageUpdateRequest("first", "stale", []),
            CancellationToken.None);
        var staleDelete = await repository.DeleteAsync(
            database.NotebookId,
            first.PageId,
            expectedRevision: 2,
            CancellationToken.None);
        var missing = await repository.DeleteAsync(
            database.NotebookId,
            PageId.FromGuid(Guid.NewGuid()),
            expectedRevision: 1,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(duplicateCreate.Status, Is.EqualTo(RepositoryPageWriteStatus.NameConflict));
            Assert.That(duplicateRename.Status, Is.EqualTo(RepositoryPageWriteStatus.NameConflict));
            Assert.That(staleUpdate.Status, Is.EqualTo(RepositoryPageWriteStatus.RevisionConflict));
            Assert.That(staleDelete.Status, Is.EqualTo(RepositoryPageWriteStatus.RevisionConflict));
            Assert.That(missing.Status, Is.EqualTo(RepositoryPageWriteStatus.NotFound));
            Assert.That(await repository.CountAsync(database.NotebookId, CancellationToken.None),
                Is.EqualTo(2));
            Assert.That((await repository.FindByIdAsync(
                database.NotebookId,
                first.PageId,
                CancellationToken.None))!.Text, Is.EqualTo("one"));
            Assert.That((await repository.FindByIdAsync(
                database.NotebookId,
                second.PageId,
                CancellationToken.None))!.Name, Is.EqualTo("second"));
        }
    }

    /// <summary>Verifies exact lookup, normalized prefix resolution, and stable summary paging.</summary>
    [Test]
    public async Task Reads_ResolveIdsNamesAndBodyFreeSummaries()
    {
        using var database = await TestDatabase.CreateAsync("queries");
        await database.InsertPageAsync(
            "11111111-1111-1111-1111-111111111111",
            "bravo",
            "bravo body");
        await database.InsertPageAsync(
            "1111aaaa-2222-2222-2222-222222222222",
            "alpha",
            "alpha body");
        var repository = new SchemaV2PageRepository(
            new FixedClock(DateTimeOffset.Parse("2026-10-30T03:00:00Z")));

        var byName = await repository.FindByNameAsync(
            database.NotebookId,
            "alpha",
            CancellationToken.None);
        var byId = await repository.FindByIdAsync(
            database.NotebookId,
            PageId.FromGuid(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            CancellationToken.None);
        var prefix = await repository.ListByIdPrefixAsync(
            database.NotebookId,
            "1111",
            maximumCount: 2,
            CancellationToken.None);
        var firstSummary = await repository.ListSummariesAsync(
            database.NotebookId,
            skipCount: 0,
            takeCount: 1,
            CancellationToken.None);
        var secondSummary = await repository.ListSummariesAsync(
            database.NotebookId,
            skipCount: 1,
            takeCount: 1,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(byName!.Text, Is.EqualTo("alpha body"));
            Assert.That(byId!.Name, Is.EqualTo("bravo"));
            Assert.That(prefix.Select(page => page.PageId.ToString()), Is.EqualTo(new[]
            {
                "11111111-1111-1111-1111-111111111111",
                "1111aaaa-2222-2222-2222-222222222222"
            }));
            Assert.That(firstSummary.Single().Name, Is.EqualTo("alpha"));
            Assert.That(secondSummary.Single().Name, Is.EqualTo("bravo"));
            Assert.That(await repository.CountAsync(database.NotebookId, CancellationToken.None),
                Is.EqualTo(2));
        }

        await database.ExecuteAsync(
            "UPDATE Pages SET Tags = '[ \"noncanonical\" ]' WHERE Name = 'alpha';");
        Assert.That(
            async () => await repository.FindByNameAsync(
                database.NotebookId,
                "alpha",
                CancellationToken.None),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(
            (await repository.ListSummariesAsync(
                database.NotebookId,
                0,
                2,
                CancellationToken.None)).Select(summary => summary.Name),
            Is.EqualTo(new[] { "alpha", "bravo" }));
    }

    /// <summary>Verifies trigger failure rolls back authoritative and earlier derived writes.</summary>
    [Test]
    public async Task Create_WhenOneFtsIndexIsMissing_RollsBackEntireWrite()
    {
        using var database = await TestDatabase.CreateAsync("rollback");
        await database.ExecuteAsync("DROP TABLE FtsIndexTrigram;");
        var repository = new SchemaV2PageRepository(
            new FixedClock(DateTimeOffset.Parse("2026-10-30T04:00:00Z")));

        Assert.That(
            async () => await repository.CreateAsync(
                database.NotebookId,
                new RepositoryPageCreateRequest("rollback", "rollback body", ["tag"]),
                CancellationToken.None),
            Throws.TypeOf<SqliteException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await repository.CountAsync(database.NotebookId, CancellationToken.None),
                Is.Zero);
            Assert.That(await database.CountRowsAsync("PageTags"), Is.Zero);
            Assert.That(await database.CountMatchesAsync("FtsIndexUnicode61", "rollback"),
                Is.Zero);
        }
    }

    /// <summary>Verifies invalid values and pre-cancellation are rejected before writes.</summary>
    [Test]
    public async Task Create_RejectsInvalidValuesAndPreCancellation()
    {
        using var database = await TestDatabase.CreateAsync("validation");
        var repository = new SchemaV2PageRepository(
            new FixedClock(DateTimeOffset.Parse("2026-10-30T05:00:00Z")));

        Assert.That(
            async () => await repository.CreateAsync(
                database.NotebookId,
                new RepositoryPageCreateRequest(" padded ", "body", []),
                CancellationToken.None),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            async () => await repository.CreateAsync(
                database.NotebookId,
                new RepositoryPageCreateRequest("valid", "body", ["bad\u0001tag"]),
                CancellationToken.None),
            Throws.TypeOf<ArgumentException>());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(
            async () => await repository.CreateAsync(
                database.NotebookId,
                new RepositoryPageCreateRequest("valid", "body", []),
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(await repository.CountAsync(database.NotebookId, CancellationToken.None),
            Is.Zero);
    }

    private sealed class TestDatabase : IDisposable
    {
        private TestDatabase(string directoryPath, string databasePath)
        {
            DirectoryPath = directoryPath;
            DatabasePath = databasePath;
            NotebookId = NotebookId.FromDatabasePath(databasePath);
        }

        internal string DirectoryPath { get; }

        internal string DatabasePath { get; }

        internal NotebookId NotebookId { get; }

        internal static async Task<TestDatabase> CreateAsync(string name)
        {
            var directoryPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MemoriaNote.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directoryPath);
            var databasePath = System.IO.Path.Combine(directoryPath, name + ".mnote");
            await SchemaV2DatabaseFactory.CreateAsync(databasePath, name, name);
            return new TestDatabase(directoryPath, databasePath);
        }

        internal async Task InsertPageAsync(string pageId, string name, string text)
        {
            await using var connection = await SqliteConnectionFactory.OpenAsync(
                DatabasePath,
                SqliteOpenMode.ReadWrite);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Pages(
                    PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
                VALUES (
                    $pageId, $name,
                    '2026-10-30 00:00:00.0000000',
                    '2026-10-30 00:00:00.0000000',
                    1, '[]', $text);
                """;
            command.Parameters.AddWithValue("$pageId", pageId);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$text", text);
            await command.ExecuteNonQueryAsync();
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

        internal async Task<IReadOnlyList<string>> ReadTagsAsync(PageId pageId)
        {
            await using var connection = await SqliteConnectionFactory.OpenAsync(
                DatabasePath,
                SqliteOpenMode.ReadOnly);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT PageTags.Label
                FROM PageTags
                JOIN Pages ON Pages.InternalId = PageTags.PageInternalId
                WHERE Pages.PageId = $pageId COLLATE BINARY
                ORDER BY PageTags.Label COLLATE BINARY;
                """;
            command.Parameters.AddWithValue("$pageId", pageId.ToString());
            await using var reader = await command.ExecuteReaderAsync();
            var tags = new List<string>();
            while (await reader.ReadAsync())
                tags.Add(reader.GetString(0));
            return tags.AsReadOnly();
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

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
