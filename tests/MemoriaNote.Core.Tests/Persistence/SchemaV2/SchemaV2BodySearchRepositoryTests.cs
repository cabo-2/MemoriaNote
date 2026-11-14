using MemoriaNote.Domain;
using MemoriaNote.Domain.Queries;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies the parameterized authoritative-body Scan repository.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2BodySearchRepositoryTests
{
    /// <summary>Verifies literal AND matching does not interpret SQL or search operators.</summary>
    [Test]
    public async Task StartAsync_LiteralKeywords_AreBoundAsParameters()
    {
        using var database = await TestDatabase.CreateAsync("literal-scan");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "both",
            "sqlite migration");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000002",
            "one",
            "sqlite only");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000003",
            "operators",
            "' OR 1=1 -- AND NEAR % _ *");
        var repository = new SchemaV2BodySearchRepository();

        var andResult = await SearchAsync(repository, database, "sqlite migration");
        var operatorResult = await SearchAsync(
            repository,
            database,
            "' OR 1=1 -- AND NEAR % _ *");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(andResult.Hits.Select(hit => hit.Name), Is.EqualTo(new[] { "both" }));
            Assert.That(
                operatorResult.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "operators" }));
            Assert.That(await database.CountPagesAsync(), Is.EqualTo(3));
        }

        await database.ExecuteAsync("DROP TABLE FtsIndexUnicode61;");
        var withoutIndex = await SearchAsync(repository, database, "sqlite migration");
        Assert.That(withoutIndex.Hits.Select(hit => hit.Name), Is.EqualTo(new[] { "both" }));
    }

    /// <summary>Verifies the default comparison folds ordinal case without normalizing text.</summary>
    [Test]
    public async Task StartAsync_DefaultCaseComparison_IsOrdinalIgnoreCase()
    {
        using var database = await TestDatabase.CreateAsync("case-insensitive-scan");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "ascii",
            "Microsoft AND literal % _ \"");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000002",
            "non-ascii",
            "ÅNGSTRÖM");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000003",
            "composed",
            "CAFÉ");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000004",
            "decomposed",
            "CAFE\u0301");
        var repository = new SchemaV2BodySearchRepository();

        var ascii = await SearchAsync(repository, database, "microsoft and % _ \"");
        var nonAscii = await SearchAsync(repository, database, "ångström");
        var normalization = await SearchAsync(repository, database, "café");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ascii.Hits.Select(hit => hit.Name), Is.EqualTo(new[] { "ascii" }));
            Assert.That(
                nonAscii.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "non-ascii" }));
            Assert.That(
                normalization.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "composed" }));
        }
    }

    /// <summary>Verifies explicit sensitive searches retain the existing ordinal literal behavior.</summary>
    [Test]
    public async Task StartAsync_SensitiveCaseComparison_UsesSqliteInstr()
    {
        using var database = await TestDatabase.CreateAsync("case-sensitive-scan");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "page",
            "Microsoft microsoft");
        var repository = new SchemaV2BodySearchRepository();

        var lower = await SearchAsync(
            repository,
            database,
            "microsoft",
            caseSensitivity: RepositoryBodySearchCaseSensitivity.Sensitive);
        var upper = await SearchAsync(
            repository,
            database,
            "MICROSOFT",
            caseSensitivity: RepositoryBodySearchCaseSensitivity.Sensitive);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lower.Hits.Select(hit => hit.Name), Is.EqualTo(new[] { "page" }));
            Assert.That(upper.Hits, Is.Empty);
            Assert.That(
                lower.Hits.Single().Snippet!.Matches.Select(
                    match => (match.Start, match.Length)),
                Is.EqualTo(new[] { (10, 9) }));
        }
    }

    /// <summary>Verifies insensitive snippets include every case variant selected by the predicate.</summary>
    [Test]
    public async Task StartAsync_InsensitiveSnippet_UsesPredicateComparison()
    {
        using var database = await TestDatabase.CreateAsync("case-snippet-scan");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "page",
            "before MICROSOFT and Microsoft after");
        var repository = new SchemaV2BodySearchRepository();

        var result = await SearchAsync(repository, database, "microsoft");

        var snippet = result.Hits.Single().Snippet!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet.Text, Is.EqualTo("before MICROSOFT and Microsoft after"));
            Assert.That(
                snippet.Matches.Select(match => (match.Start, match.Length)),
                Is.EqualTo(new[] { (7, 9), (21, 9) }));
        }
    }

    /// <summary>Verifies both sorts, Page ID ties, limit, and hasMore are deterministic.</summary>
    [Test]
    public async Task Cursor_ReadAsync_StreamsSortsLimitsAndReportsHasMore()
    {
        using var database = await TestDatabase.CreateAsync("bounded-scan");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000003",
            "zulu",
            "MARKER",
            updateTime: "2026-11-07 03:00:00.0000000");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "alpha",
            "Marker",
            updateTime: "2026-11-07 03:00:00.0000000");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000002",
            "bravo",
            "marker",
            updateTime: "2026-11-07 02:00:00.0000000");
        var repository = new SchemaV2BodySearchRepository();

        var byName = await SearchAsync(
            repository,
            database,
            "marker",
            RepositoryBodySearchSort.Name,
            limit: 2,
            includeSnippet: false,
            batchSize: 1);
        var byUpdated = await SearchAsync(
            repository,
            database,
            "marker",
            RepositoryBodySearchSort.Updated,
            limit: 3,
            includeSnippet: false,
            batchSize: 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(byName.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "alpha", "bravo" }));
            Assert.That(byName.HasMore, Is.True);
            Assert.That(byName.Hits, Has.All.Property("Snippet").Null);
            Assert.That(byName.BatchSizes, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(byUpdated.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "alpha", "zulu", "bravo" }));
            Assert.That(byUpdated.HasMore, Is.False);
            Assert.That(byUpdated.BatchSizes, Is.EqualTo(new[] { 2, 1 }));
        }
    }

    /// <summary>Verifies Scan snippets are sanitized and expose only structured UTF-16 spans.</summary>
    [Test]
    public async Task Cursor_ReadAsync_WithSnippet_ReturnsSanitizedTextAndMatches()
    {
        using var database = await TestDatabase.CreateAsync("scan-snippet");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "page",
            "before\nneedle\tand needle\u001bafter");
        var repository = new SchemaV2BodySearchRepository();

        var result = await SearchAsync(repository, database, "needle");

        var snippet = result.Hits.Single().Snippet!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet.Text, Is.EqualTo("before needle and needle\uFFFDafter"));
            Assert.That(
                snippet.Matches.Select(match => (match.Start, match.Length)),
                Is.EqualTo(new[] { (7, 6), (18, 6) }));
        }
    }

    /// <summary>Verifies pre-cancellation is propagated before database access.</summary>
    [Test]
    public async Task StartAsync_PreCanceled_ThrowsCancellation()
    {
        using var database = await TestDatabase.CreateAsync("canceled-scan");
        var repository = new SchemaV2BodySearchRepository();
        var query = Parse("marker", RepositoryBodySearchSort.Name, 1, true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await repository.StartAsync(
                database.NotebookId,
                query,
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static async Task<SearchResult> SearchAsync(
        SchemaV2BodySearchRepository repository,
        TestDatabase database,
        string source,
        RepositoryBodySearchSort sort = RepositoryBodySearchSort.Name,
        int limit = 20,
        bool includeSnippet = true,
        int batchSize = 20,
        RepositoryBodySearchCaseSensitivity caseSensitivity =
            RepositoryBodySearchCaseSensitivity.Insensitive)
    {
        await using var cursor = await repository.StartAsync(
            database.NotebookId,
            Parse(source, sort, limit, includeSnippet, caseSensitivity),
            CancellationToken.None);
        var hits = new List<RepositoryBodySearchHit>();
        var batchSizes = new List<int>();
        RepositoryBodySearchBatch batch;
        do
        {
            batch = await cursor.ReadAsync(batchSize, CancellationToken.None);
            hits.AddRange(batch.Hits);
            batchSizes.Add(batch.Hits.Count);
        }
        while (!batch.IsCompleted);

        return new SearchResult(hits, batchSizes, batch.HasMore);
    }

    private static RepositoryBodySearchQuery Parse(
        string source,
        RepositoryBodySearchSort sort,
        int limit,
        bool includeSnippet,
        RepositoryBodySearchCaseSensitivity caseSensitivity =
            RepositoryBodySearchCaseSensitivity.Insensitive)
    {
        Assert.That(
            BodySearchQueryParser.TryParse(source, out var query, out var error),
            Is.True,
            error.ToString());
        return new RepositoryBodySearchQuery(
            query!,
            sort,
            limit,
            includeSnippet,
            caseSensitivity);
    }

    private sealed record SearchResult(
        IReadOnlyList<RepositoryBodySearchHit> Hits,
        IReadOnlyList<int> BatchSizes,
        bool HasMore);

    private sealed class TestDatabase : IDisposable
    {
        private readonly TemporaryDirectory _directory;

        private TestDatabase(TemporaryDirectory directory, string databasePath)
        {
            _directory = directory;
            DatabasePath = databasePath;
            NotebookId = NotebookId.FromDatabasePath(databasePath);
        }

        internal string DatabasePath { get; }

        internal NotebookId NotebookId { get; }

        internal static async Task<TestDatabase> CreateAsync(string name)
        {
            var directory = new TemporaryDirectory();
            var databasePath = directory.GetPath(name + ".mnote");
            await SchemaV2DatabaseFactory.CreateAsync(databasePath, name);
            return new TestDatabase(directory, databasePath);
        }

        internal async Task InsertPageAsync(
            string pageId,
            string name,
            string text,
            string updateTime = "2026-11-07 00:00:00.0000000")
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
                    '2026-11-07 00:00:00.0000000',
                    $updateTime,
                    1, '[]', $text);
                """;
            command.Parameters.AddWithValue("$pageId", pageId);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$updateTime", updateTime);
            command.Parameters.AddWithValue("$text", text);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<long> CountPagesAsync()
        {
            await using var connection = await SqliteConnectionFactory.OpenAsync(
                DatabasePath,
                SqliteOpenMode.ReadOnly);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Pages;";
            return (long)(await command.ExecuteScalarAsync())!;
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

        public void Dispose() => _directory.Dispose();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MemoriaNote.BodySearch.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string GetPath(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
