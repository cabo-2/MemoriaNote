using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Queries;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies the unicode61 and trigram streaming body-search candidates.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2FtsBodySearchRepositoryTests
{
    /// <summary>Verifies candidate queries search only body text and quote FTS operators.</summary>
    [TestCase("unicode61")]
    [TestCase("trigram")]
    public async Task ApplicationSearch_LiteralAndBodyOnly_ReturnsExpectedPages(
        string candidateName)
    {
        var candidate = Candidate(candidateName);
        using var database = await TestDatabase.CreateAsync("literal-fts");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "body-hit",
            "sqlite migration");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000002",
            "sqlite migration",
            "unrelated body");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000003",
            "operators",
            "AND OR NOT NEAR \"quoted\"");
        await using var session = database.OpenSession(candidate);

        var andResult = await SearchApplicationAsync(session, "sqlite migration");
        var operatorResult = await SearchApplicationAsync(session, "AND NEAR");
        var quoteResult = await SearchApplicationAsync(session, "\"quoted\"");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(andResult.Hits.Select(hit => hit.Name), Is.EqualTo(new[] { "body-hit" }));
            Assert.That(
                operatorResult.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "operators" }));
            Assert.That(
                quoteResult.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "operators" }));
        }
    }

    /// <summary>Verifies token and substring candidates retain their distinct Unicode semantics.</summary>
    [Test]
    public async Task ApplicationSearch_CandidateSemantics_AreIndependentlyObservable()
    {
        using var database = await TestDatabase.CreateAsync("candidate-semantics");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "japanese-contained",
            "これは日本語です");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000002",
            "japanese-token",
            "日本語");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000003",
            "english",
            "sqlite");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000004",
            "diacritic",
            "café");
        await using var unicode61 = database.OpenSession(
            SchemaV2BodySearchCandidate.FtsUnicode61);
        await using var trigram = database.OpenSession(
            SchemaV2BodySearchCandidate.FtsTrigram);

        var unicodeJapanese = await SearchApplicationAsync(unicode61, "日本語");
        var trigramJapanese = await SearchApplicationAsync(trigram, "日本語");
        var unicodeSubstring = await SearchApplicationAsync(unicode61, "lite");
        var trigramSubstring = await SearchApplicationAsync(trigram, "lite");
        var unicodeDiacritic = await SearchApplicationAsync(unicode61, "cafe");
        var trigramDiacritic = await SearchApplicationAsync(trigram, "cafe");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                unicodeJapanese.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "japanese-token" }));
            Assert.That(
                trigramJapanese.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "japanese-contained", "japanese-token" }));
            Assert.That(unicodeSubstring.Hits, Is.Empty);
            Assert.That(
                trigramSubstring.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "english" }));
            Assert.That(
                unicodeDiacritic.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "diacritic" }));
            Assert.That(trigramDiacritic.Hits, Is.Empty);
        }
    }

    /// <summary>Verifies unicode61 accepts a one-scalar token without fallback.</summary>
    [Test]
    public async Task Unicode61_OneScalarToken_SearchesSelectedIndex()
    {
        using var database = await TestDatabase.CreateAsync("unicode-short");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "short",
            "日");
        await using var session = database.OpenSession(
            SchemaV2BodySearchCandidate.FtsUnicode61);

        var result = await SearchApplicationAsync(session, "日");

        Assert.That(result.Hits.Select(hit => hit.Name), Is.EqualTo(new[] { "short" }));
    }

    /// <summary>Verifies punctuation-only input is literal and never becomes FTS syntax.</summary>
    [TestCase("*")]
    [TestCase("\"")]
    [TestCase("()")]
    public async Task Unicode61_PunctuationOnly_ReturnsEmptySuccess(string query)
    {
        using var database = await TestDatabase.CreateAsync("unicode-punctuation");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "punctuation",
            "* \" ()");
        await using var session = database.OpenSession(
            SchemaV2BodySearchCandidate.FtsUnicode61);

        var result = await SearchApplicationAsync(session, query);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.HasMore, Is.False);
        }
    }

    /// <summary>Verifies trigram rejects any keyword shorter than three scalars.</summary>
    [Test]
    public async Task Trigram_ShortKeyword_ReturnsValidationFailure()
    {
        using var database = await TestDatabase.CreateAsync("trigram-short");
        await using var session = database.OpenSession(
            SchemaV2BodySearchCandidate.FtsTrigram);

        var result = await session.Pages.StartBodySearchAsync(
            new NotebookBodySearchRequest("ab", NotebookBodySearchSort.Name));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure!.Category, Is.EqualTo(NotebookFailureCategory.Validation));
            Assert.That(result.Failure.Code, Is.EqualTo("page.search.keyword_too_short"));
        }
    }

    /// <summary>Verifies BM25 ordering, Page ID ties, streaming batches, limit, and hasMore.</summary>
    [TestCase("unicode61")]
    [TestCase("trigram")]
    public async Task RelevanceCursor_StreamsStableOrderAndReportsHasMore(
        string candidateName)
    {
        var kind = Kind(candidateName);
        using var database = await TestDatabase.CreateAsync("relevance-fts");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000003",
            "page-c",
            "marker");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "page-a",
            "marker");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000002",
            "page-b",
            "marker marker marker");
        var repository = new SchemaV2FtsBodySearchRepository(kind);

        var result = await SearchRepositoryAsync(
            repository,
            database,
            "marker",
            RepositoryBodySearchSort.Relevance,
            limit: 2,
            batchSize: 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Hits.Select(hit => hit.PageId.ToString()),
                Is.EqualTo(new[]
                {
                    "00000000-0000-0000-0000-000000000002",
                    "00000000-0000-0000-0000-000000000001"
                }));
            Assert.That(result.BatchSizes, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(result.HasMore, Is.True);
        }
    }

    /// <summary>Verifies both explicit attribute sorts retain their common deterministic contract.</summary>
    [TestCase("unicode61")]
    [TestCase("trigram")]
    public async Task Cursor_ExplicitSorts_UseCommonStableOrder(
        string candidateName)
    {
        var kind = Kind(candidateName);
        using var database = await TestDatabase.CreateAsync("sorted-fts");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000003",
            "zulu",
            "marker",
            "2026-11-07 03:00:00.0000000");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "alpha",
            "marker",
            "2026-11-07 03:00:00.0000000");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000002",
            "bravo",
            "marker",
            "2026-11-07 02:00:00.0000000");
        var repository = new SchemaV2FtsBodySearchRepository(kind);

        var byName = await SearchRepositoryAsync(
            repository,
            database,
            "marker",
            RepositoryBodySearchSort.Name);
        var byUpdated = await SearchRepositoryAsync(
            repository,
            database,
            "marker",
            RepositoryBodySearchSort.Updated);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                byName.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "alpha", "bravo", "zulu" }));
            Assert.That(
                byUpdated.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "alpha", "zulu", "bravo" }));
        }
    }

    /// <summary>Verifies FTS snippets share sanitized text and UTF-16 match spans.</summary>
    [TestCase("unicode61")]
    [TestCase("trigram")]
    public async Task ApplicationSearch_WithSnippet_ReturnsCommonStructure(
        string candidateName)
    {
        var candidate = Candidate(candidateName);
        using var database = await TestDatabase.CreateAsync("snippet-fts");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "page",
            "before\nSQLITE\tand sqlite\u001bafter");
        await using var session = database.OpenSession(candidate);

        var result = await SearchApplicationAsync(session, "sqlite");

        var snippet = result.Hits.Single().Snippet!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet.Text, Is.EqualTo("before SQLITE and sqlite\uFFFDafter"));
            Assert.That(
                snippet.Matches.Select(match => (match.Start, match.Length)),
                Is.EqualTo(new[] { (7, 6), (18, 6) }));
        }
    }

    /// <summary>Verifies omitted snippets do not project a structured snippet.</summary>
    [TestCase("unicode61")]
    [TestCase("trigram")]
    public async Task ApplicationSearch_WithoutSnippet_ReturnsNull(
        string candidateName)
    {
        var candidate = Candidate(candidateName);
        using var database = await TestDatabase.CreateAsync("no-snippet-fts");
        await database.InsertPageAsync(
            "00000000-0000-0000-0000-000000000001",
            "page",
            "sqlite body");
        await using var session = database.OpenSession(candidate);

        var result = await SearchApplicationAsync(
            session,
            "sqlite",
            includeSnippet: false);

        Assert.That(result.Hits.Single().Snippet, Is.Null);
    }

    /// <summary>Verifies a required FTS table missing before open is a format error.</summary>
    [TestCase("FtsIndexUnicode61")]
    [TestCase("FtsIndexTrigram")]
    public async Task ApplicationOpen_MissingRequiredIndex_ReturnsFormatFailure(string tableName)
    {
        using var database = await TestDatabase.CreateAsync("missing-open-fts");
        await database.ExecuteAsync($"DROP TABLE \"{tableName}\";");

        var result = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(database.DatabasePath));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure!.Category, Is.EqualTo(NotebookFailureCategory.Format));
            Assert.That(result.Failure.Code, Is.EqualTo("format.schema_invalid"));
        }
    }

    /// <summary>Verifies an index removed after session creation is a derived-data failure.</summary>
    [TestCase("unicode61", "FtsIndexUnicode61")]
    [TestCase("trigram", "FtsIndexTrigram")]
    public async Task ApplicationSearch_IndexRemovedAfterOpen_ReturnsIndexFailure(
        string candidateName,
        string tableName)
    {
        var candidate = Candidate(candidateName);
        using var database = await TestDatabase.CreateAsync("removed-fts");
        await using var session = database.OpenSession(candidate);
        await database.ExecuteAsync($"DROP TABLE \"{tableName}\";");

        var result = await session.Pages.StartBodySearchAsync(
            new NotebookBodySearchRequest("marker", NotebookBodySearchSort.Name));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure!.Category, Is.EqualTo(NotebookFailureCategory.DerivedData));
            Assert.That(result.Failure.Code, Is.EqualTo("page.search.index_invalid"));
        }
    }

    private static async Task<ApplicationSearchResult> SearchApplicationAsync(
        NotebookApplicationSession session,
        string source,
        bool includeSnippet = true,
        int batchSize = 20)
    {
        var started = await session.Pages.StartBodySearchAsync(
            new NotebookBodySearchRequest(
                source,
                NotebookBodySearchSort.Name,
                includeSnippet: includeSnippet));
        Assert.That(started.IsSuccess, Is.True, started.Failure?.Code);
        await using var search = started.Value!;
        var hits = new List<NotebookBodySearchHit>();
        NotebookBodySearchBatch batch;
        do
        {
            var read = await search.ReadAsync(batchSize);
            Assert.That(read.IsSuccess, Is.True, read.Failure?.Code);
            batch = read.Value!;
            hits.AddRange(batch.Hits);
        }
        while (!batch.IsCompleted);
        return new ApplicationSearchResult(hits, batch.HasMore);
    }

    private static async Task<RepositorySearchResult> SearchRepositoryAsync(
        SchemaV2FtsBodySearchRepository repository,
        TestDatabase database,
        string source,
        RepositoryBodySearchSort sort,
        int limit = 20,
        int batchSize = 20)
    {
        await using var cursor = await repository.StartAsync(
            database.NotebookId,
            Parse(source, sort, limit),
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
        return new RepositorySearchResult(hits, batchSizes, batch.HasMore);
    }

    private static RepositoryBodySearchQuery Parse(
        string source,
        RepositoryBodySearchSort sort,
        int limit)
    {
        Assert.That(
            BodySearchQueryParser.TryParse(source, out var query, out var error),
            Is.True,
            error.ToString());
        return new RepositoryBodySearchQuery(query!, sort, limit, includeSnippet: true);
    }

    private static SchemaV2BodySearchCandidate Candidate(string name) => name switch
    {
        "unicode61" => SchemaV2BodySearchCandidate.FtsUnicode61,
        "trigram" => SchemaV2BodySearchCandidate.FtsTrigram,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static SchemaV2FtsBodySearchKind Kind(string name) => name switch
    {
        "unicode61" => SchemaV2FtsBodySearchKind.Unicode61,
        "trigram" => SchemaV2FtsBodySearchKind.Trigram,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private sealed record ApplicationSearchResult(
        IReadOnlyList<NotebookBodySearchHit> Hits,
        bool HasMore);

    private sealed record RepositorySearchResult(
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

        internal NotebookApplicationSession OpenSession(SchemaV2BodySearchCandidate candidate)
        {
            var provider = new SchemaV2RepositoryProvider(
                new FixedClock(DateTimeOffset.Parse("2026-11-08T00:00:00Z")),
                candidate);
            return new NotebookApplicationSession(
                new NotebookDescriptor(NotebookId, SchemaV2Format.UserVersion),
                provider);
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
                "MemoriaNote.FtsBodySearch.Tests",
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
