using System.Runtime.CompilerServices;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Integration.Validation;
using MemoriaNote.Persistence;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Integration.Validation;

/// <summary>Verifies the revision 1 body-search conformance corpus through public validation APIs.</summary>
[TestFixture]
[Category("Functional")]
public sealed class NotebookSearchValidationTests
{
    private static readonly IReadOnlyList<ConformanceCase> Cases =
    [
        new(
            "literal-and",
            "sqlite migration",
            ["alpha", "bravo"],
            ["alpha", "bravo"],
            ["alpha", "bravo"]),
        new(
            "japanese-token-substring",
            "日本語",
            ["alpha", "bravo"],
            ["alpha"],
            ["alpha", "bravo"]),
        new(
            "english-substring",
            "lite",
            ["alpha", "bravo"],
            [],
            ["alpha", "bravo"]),
        new(
            "diacritic-folding",
            "cafe",
            [],
            ["alpha"],
            []),
        new(
            "operator-literals",
            "AND NEAR",
            ["operators"],
            ["operators"],
            ["operators"])
    ];

    /// <summary>Runs the same logical query matrix through all three fixed candidates.</summary>
    [Test]
    public async Task Candidates_UseSharedQuerySessionAndResultContract()
    {
        using var notebook = await CreateCorpusAsync();
        var beforeBytes = await File.ReadAllBytesAsync(notebook.DatabasePath);
        var beforeFiles = Directory.GetFileSystemEntries(notebook.DirectoryPath);
        var opened = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(notebook.DatabasePath));
        await using var application = opened.Value!;
        var validation = NotebookSearchValidation.CreateSession(application);

        foreach (var candidate in Enum.GetValues<BodySearchCandidate>())
        {
            foreach (var testCase in Cases)
            {
                var result = await SearchAsync(
                    validation,
                    new BodySearchValidationRequest(
                        candidate,
                        testCase.Query,
                        BodySearchValidationSort.Name,
                        limit: 10));

                Assert.That(
                    result.Hits.Select(hit => hit.Name),
                    Is.EqualTo(testCase.Expected(candidate)),
                    $"{candidate}: {testCase.Name}");
                Assert.That(result.HasMore, Is.False, $"{candidate}: {testCase.Name}");
                foreach (var hit in result.Hits)
                {
                    Assert.That(hit.Snippet, Is.Not.Null, $"{candidate}: {testCase.Name}");
                    AssertSnippetInvariant(hit.Snippet!);
                }
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CoreIntegrationContract.BodySearchConformanceCorpusRevision, Is.EqualTo(1));
            Assert.That(await File.ReadAllBytesAsync(notebook.DatabasePath), Is.EqualTo(beforeBytes));
            Assert.That(Directory.GetFileSystemEntries(notebook.DirectoryPath), Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies FTS relevance is explicit and Scan receives no synthetic ranking.</summary>
    [Test]
    public async Task Relevance_IsAvailableOnlyForFtsCandidates()
    {
        using var notebook = await CreateCorpusAsync();
        var opened = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(notebook.DatabasePath));
        await using var application = opened.Value!;
        var validation = NotebookSearchValidation.CreateSession(application);

        foreach (var candidate in new[]
                 {
                     BodySearchCandidate.Fts5Unicode61,
                     BodySearchCandidate.Fts5Trigram
                 })
        {
            var result = await SearchAsync(
                validation,
                new BodySearchValidationRequest(
                    candidate,
                    "marker",
                    BodySearchValidationSort.Relevance,
                    limit: 2));

            Assert.That(
                result.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "bravo", "alpha" }),
                candidate.ToString());
        }

        var scan = await validation.StartAsync(
            new BodySearchValidationRequest(
                BodySearchCandidate.Scan,
                "marker",
                BodySearchValidationSort.Relevance));

        AssertFailure(scan, "page.search.unsupported_sort");
    }

    /// <summary>Verifies short-query behavior remains candidate-specific without fallback.</summary>
    [Test]
    public async Task ShortQuery_UsesSelectedCandidateWithoutFallback()
    {
        using var notebook = await CreateCorpusAsync();
        var opened = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(notebook.DatabasePath));
        await using var application = opened.Value!;
        var validation = NotebookSearchValidation.CreateSession(application);

        var scan = await SearchAsync(
            validation,
            new BodySearchValidationRequest(
                BodySearchCandidate.Scan,
                "日",
                BodySearchValidationSort.Name));
        var unicode61 = await SearchAsync(
            validation,
            new BodySearchValidationRequest(
                BodySearchCandidate.Fts5Unicode61,
                "日",
                BodySearchValidationSort.Name));
        var trigram = await validation.StartAsync(
            new BodySearchValidationRequest(
                BodySearchCandidate.Fts5Trigram,
                "日",
                BodySearchValidationSort.Name));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                scan.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "alpha", "bravo", "short" }));
            Assert.That(
                unicode61.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "short" }));
        }
        AssertFailure(trigram, "page.search.keyword_too_short");
    }

    /// <summary>Verifies unknown closed-selector values are rejected before storage access.</summary>
    [Test]
    public async Task InvalidSelectorAndSort_ReturnClassifiedFailures()
    {
        using var notebook = await CreateCorpusAsync();
        var opened = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(notebook.DatabasePath));
        await using var application = opened.Value!;
        var validation = NotebookSearchValidation.CreateSession(application);

        var candidate = await validation.StartAsync(
            new BodySearchValidationRequest(
                (BodySearchCandidate)99,
                "marker",
                BodySearchValidationSort.Name));
        var sort = await validation.StartAsync(
            new BodySearchValidationRequest(
                BodySearchCandidate.Scan,
                "marker",
                (BodySearchValidationSort)99));

        AssertFailure(candidate, "page.search.invalid_candidate");
        AssertFailure(sort, "page.search.invalid_sort");
    }

    /// <summary>Verifies selected-index failures remain classified and never fall back.</summary>
    [Test]
    public async Task SelectedFtsIndexFailure_DoesNotFallBack()
    {
        using var notebook = await CreateCorpusAsync();
        var opened = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(notebook.DatabasePath));
        await using var application = opened.Value!;
        var validation = NotebookSearchValidation.CreateSession(application);
        await ExecuteAsync(notebook.DatabasePath, "DROP TABLE FtsIndexTrigram;");

        var result = await validation.StartAsync(
            new BodySearchValidationRequest(
                BodySearchCandidate.Fts5Trigram,
                "marker",
                BodySearchValidationSort.Name));

        AssertFailure(result, "page.search.index_invalid", NotebookFailureCategory.DerivedData);
    }

    /// <summary>Verifies the validation session shares the application-session lifetime.</summary>
    [Test]
    public async Task AfterApplicationDispose_RejectsSearch()
    {
        using var notebook = await CreateCorpusAsync();
        var opened = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(notebook.DatabasePath));
        var application = opened.Value!;
        var validation = NotebookSearchValidation.CreateSession(application);
        await application.DisposeAsync();

        Assert.That(
            async () => await validation.StartAsync(
                new BodySearchValidationRequest(
                    BodySearchCandidate.Scan,
                    "marker",
                    BodySearchValidationSort.Name)),
            Throws.TypeOf<ObjectDisposedException>());
    }

    private static async Task<SearchResult> SearchAsync(
        NotebookSearchValidationSession validation,
        BodySearchValidationRequest request)
    {
        var started = await validation.StartAsync(request);
        Assert.That(started.IsSuccess, Is.True, started.Failure?.Code);
        await using var search = started.Value!;
        var hits = new List<NotebookBodySearchHit>();
        NotebookBodySearchBatch batch;
        do
        {
            var read = await search.ReadAsync(maximumCount: 1);
            Assert.That(read.IsSuccess, Is.True, read.Failure?.Code);
            batch = read.Value!;
            hits.AddRange(batch.Hits);
        }
        while (!batch.IsCompleted);
        return new SearchResult(hits, batch.HasMore);
    }

    private static void AssertSnippetInvariant(NotebookBodySearchSnippet snippet)
    {
        var previousEnd = 0;
        foreach (var match in snippet.Matches)
        {
            Assert.That(match.Start, Is.GreaterThanOrEqualTo(previousEnd));
            Assert.That(match.Length, Is.GreaterThan(0));
            Assert.That(match.Start + match.Length, Is.LessThanOrEqualTo(snippet.Text.Length));
            previousEnd = match.Start + match.Length;
        }
    }

    private static void AssertFailure<T>(
        NotebookOperationResult<T> result,
        string code,
        NotebookFailureCategory category = NotebookFailureCategory.Validation)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure!.Category, Is.EqualTo(category));
            Assert.That(result.Failure.Code, Is.EqualTo(code));
        }
    }

    private static async Task<CorpusNotebook> CreateCorpusAsync()
    {
        var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("body-search-conformance-v1.mnote");
        var timestamp = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        var pages = new[]
        {
            Page("00000000-0000-4000-8000-000000000001", "alpha", timestamp,
                "sqlite migration café 日本語 marker"),
            Page("00000000-0000-4000-8000-000000000002", "bravo", timestamp,
                "prefix sqlite migration suffix 日本語です marker marker marker"),
            Page("00000000-0000-4000-8000-000000000003", "operators", timestamp,
                "AND OR NOT NEAR \"quoted\""),
            Page("00000000-0000-4000-8000-000000000004", "short", timestamp,
                "日 ab")
        };
        var metadata = new Dictionary<string, string?>
        {
            ["Author"] = string.Empty,
            ["CreateTime"] = null,
            ["Description"] = string.Empty,
            ["ReadOnly"] = bool.FalseString,
            ["Tag"] = string.Empty,
            ["Title"] = "Body search conformance v1"
        };
        var published = await DeterministicNotebookPublisher.PublishAsync(
            new DeterministicNotebookPublicationRequest(
                databasePath,
                metadata,
                ToAsync(pages)));
        if (!published.IsSuccess)
        {
            directory.Dispose();
            throw new InvalidOperationException(published.Failure!.Code);
        }
        return new CorpusNotebook(directory, databasePath);
    }

    private static DeterministicNotebookPage Page(
        string pageId,
        string name,
        DateTimeOffset timestamp,
        string text) => new(
            PageId.FromGuid(Guid.Parse(pageId)),
            name,
            timestamp,
            timestamp,
            revision: 1,
            [],
            text);

    private static async IAsyncEnumerable<DeterministicNotebookPage> ToAsync(
        IEnumerable<DeterministicNotebookPage> pages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return page;
            await Task.Yield();
        }
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

    private sealed record ConformanceCase(
        string Name,
        string Query,
        IReadOnlyList<string> Scan,
        IReadOnlyList<string> Unicode61,
        IReadOnlyList<string> Trigram)
    {
        internal IReadOnlyList<string> Expected(BodySearchCandidate candidate) =>
            candidate switch
            {
                BodySearchCandidate.Scan => Scan,
                BodySearchCandidate.Fts5Unicode61 => Unicode61,
                BodySearchCandidate.Fts5Trigram => Trigram,
                _ => throw new ArgumentOutOfRangeException(nameof(candidate))
            };
    }

    private sealed record SearchResult(
        IReadOnlyList<NotebookBodySearchHit> Hits,
        bool HasMore);

    private sealed class CorpusNotebook : IDisposable
    {
        private readonly TemporaryDirectory _directory;

        internal CorpusNotebook(TemporaryDirectory directory, string databasePath)
        {
            _directory = directory;
            DatabasePath = databasePath;
        }

        internal string DatabasePath { get; }

        internal string DirectoryPath => _directory.Path;

        public void Dispose() => _directory.Dispose();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MemoriaNote.SearchValidation.Tests",
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
