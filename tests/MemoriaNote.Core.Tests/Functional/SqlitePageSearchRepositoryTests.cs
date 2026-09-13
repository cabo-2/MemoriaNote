using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies the parameterized SQLite search repository contract.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class SqlitePageSearchRepositoryTests
{
    readonly IPageSearchRepository _repository =
        new SqlitePageSearchRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));

    /// <summary>
    /// Verifies that SQL-like text is treated as a search value rather than query structure.
    /// </summary>
    [Test]
    public async Task SearchAsync_SqlLikeHeading_ReturnsOnlyTheLiteralMatch()
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("test-note", "Test Note");
        var searchEntry = "Alpha' OR 1 = 1 --";
        var expected = note.CreatePage(searchEntry, "Literal query text.");
        note.CreatePage("Unrelated", "Another page.");

        var result = await _repository.SearchAsync(
            NotebookId.FromDatabasePath(database.DatabasePath),
            searchEntry,
            SearchMethodType.Heading,
            0,
            10,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(
                result.Select(summary => summary.PageId.Value),
                Is.EqualTo(new[] { expected.Guid }));
            Assert.That(
                result.Single().NotebookId,
                Is.EqualTo(NotebookId.FromDatabasePath(database.DatabasePath)));
        }
    }

    /// <summary>
    /// Captures existing heading results for punctuation and Unicode input.
    /// </summary>
    /// <param name="searchEntry">The heading and search value.</param>
    /// <param name="expectedCount">The existing number of matches.</param>
    [TestCase("%", 0)]
    [TestCase("_", 0)]
    [TestCase("\"Quoted\"", 0)]
    [TestCase("O'Brien", 1)]
    [TestCase("日本語", 1)]
    public async Task SearchAsync_SpecialHeading_PreservesExistingResult(
        string searchEntry,
        int expectedCount)
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("test-note", "Test Note");
        var page = note.CreatePage(searchEntry, "Special heading body.");

        var result = await _repository.SearchAsync(
            NotebookId.FromDatabasePath(database.DatabasePath),
            searchEntry,
            SearchMethodType.Heading,
            0,
            10,
            CancellationToken.None);

        Assert.That(result, Has.Count.EqualTo(expectedCount));
        if (expectedCount == 1)
            Assert.That(result.Single().PageId.Value, Is.EqualTo(page.Guid));
    }

    /// <summary>
    /// Captures existing full-text results for punctuation and Unicode input.
    /// </summary>
    /// <param name="searchEntry">The full-text search value.</param>
    /// <param name="expectedPageName">The matching page name, or null when no match is expected.</param>
    [TestCase("O'Brien", "Apostrophe")]
    [TestCase("\"quoted\"", "Quotation")]
    [TestCase("日本語", "Unicode")]
    [TestCase("%", null)]
    [TestCase("_", null)]
    public async Task SearchAsync_SpecialFullText_PreservesExistingResult(
        string searchEntry,
        string? expectedPageName)
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("test-note", "Test Note");
        note.CreatePage("Apostrophe", "O'Brien wrote this text.");
        note.CreatePage("Quotation", "A \"quoted\" word appears here.");
        note.CreatePage("Unicode", "日本語");
        note.CreatePage("Symbols", "100% under_score.");

        var result = await _repository.SearchAsync(
            NotebookId.FromDatabasePath(database.DatabasePath),
            searchEntry,
            SearchMethodType.FullText,
            0,
            10,
            CancellationToken.None);

        if (expectedPageName == null)
        {
            Assert.That(result, Is.Empty);
        }
        else
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result.Single().Name, Is.EqualTo(expectedPageName));
        }
    }

    /// <summary>
    /// Verifies that count and result queries use the same matching conditions.
    /// </summary>
    /// <param name="searchMethod">The search method to exercise.</param>
    /// <param name="searchEntry">The search value.</param>
    [TestCase(SearchMethodType.Heading, "%")]
    [TestCase(SearchMethodType.Heading, "Alpha*")]
    [TestCase(SearchMethodType.FullText, "marker")]
    public async Task CountAsync_UsesTheSameConditionsAsSearchAsync(
        SearchMethodType searchMethod,
        string searchEntry)
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("test-note", "Test Note");
        note.CreatePage("%", "A marker appears here.");
        note.CreatePage("Alpha", "Another marker appears here.");
        note.CreatePage("Alphabet", "No matching body token.");

        var count = await _repository.CountMatchesAsync(
            NotebookId.FromDatabasePath(database.DatabasePath),
            searchEntry,
            searchMethod,
            CancellationToken.None);
        var result = await _repository.SearchAsync(
            NotebookId.FromDatabasePath(database.DatabasePath),
            searchEntry,
            searchMethod,
            0,
            1,
            CancellationToken.None);

        Assert.That(result, Has.Count.EqualTo(Math.Min(count, 1)));
    }

    /// <summary>
    /// Verifies that a pre-cancelled token prevents repository database access.
    /// </summary>
    [Test]
    public async Task Operations_PreCancelledToken_ThrowOperationCancelledException()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("test-note", "Test Note");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        OperationCanceledException? searchException = null;
        OperationCanceledException? countException = null;

        try
        {
            await _repository.SearchAsync(
                NotebookId.FromDatabasePath(database.DatabasePath),
                "marker",
                SearchMethodType.FullText,
                0,
                10,
                cancellation.Token);
        }
        catch (OperationCanceledException caught)
        {
            searchException = caught;
        }

        try
        {
            await _repository.CountMatchesAsync(
                NotebookId.FromDatabasePath(database.DatabasePath),
                "marker",
                SearchMethodType.FullText,
                cancellation.Token);
        }
        catch (OperationCanceledException caught)
        {
            countException = caught;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(searchException, Is.Not.Null);
            Assert.That(countException, Is.Not.Null);
        }
    }

    /// <summary>
    /// Verifies that SQLite failures remain observable at the repository boundary.
    /// </summary>
    [Test]
    public async Task SearchAsync_MissingDatabaseSchema_ThrowsSqliteException()
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("test-note", "Test Note");
        note.CreatePage("Alpha", "Search marker.");
        SqliteConnection.ClearAllPools();
        File.Delete(database.DatabasePath);
        SqliteException? exception = null;

        try
        {
            await _repository.SearchAsync(
                NotebookId.FromDatabasePath(database.DatabasePath),
                "marker",
                SearchMethodType.FullText,
                0,
                10,
                CancellationToken.None);
        }
        catch (SqliteException caught)
        {
            exception = caught;
        }

        Assert.That(exception, Is.Not.Null);
    }

    /// <summary>
    /// Verifies that workspace totals follow the same exact-heading query as result retrieval.
    /// </summary>
    [Test]
    public async Task WorkspaceSearch_SpecialHeading_KeepsCountAndContentsConsistent()
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("test-note", "Test Note");
        note.CreatePage("%", "Special heading.");
        var workspace = new Workspace(null, new[] { note }, note);

        var request = SearchRequest.ForWorkspace(
            "%",
            SearchMethodType.Heading,
            new[] { NotebookId.FromDatabasePath(note.DatabasePath) },
            0,
            10);
        var result = await ApplicationComposition.Compose(workspace)
            .ApplicationService
            .SearchAsync(request, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalCount, Is.Zero);
            Assert.That(result.Items, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies that Notebook and Workspace delegate searches through an injected repository.
    /// </summary>
    [Test]
    public async Task WorkspaceSearch_InjectedRepository_PreservesOwnerInformation()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebookId = NotebookId.FromDatabasePath(database.DatabasePath);
        var summary = new PageSummary(
            notebookId,
            PageId.FromGuid(Guid.NewGuid()),
            "Injected",
            1,
            new Dictionary<string, string>(),
            nameof(Page),
            DateTime.UtcNow,
            DateTime.UtcNow,
            false);
        var repository = new RecordingSearchRepository(summary);
        var note = new Notebook(database.DatabasePath, repository);
        var result = await new SearchUseCase(repository).SearchAsync(
            SearchRequest.ForWorkspace(
                "Injected",
                SearchMethodType.Heading,
                new[] { notebookId },
                0,
                10),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            var returnedSummary = result.Items.Single();
            Assert.That(repository.CountCallCount, Is.EqualTo(1));
            Assert.That(repository.SearchCallCount, Is.EqualTo(1));
            Assert.That(result.TotalCount, Is.EqualTo(1));
            Assert.That(returnedSummary.PageId, Is.EqualTo(summary.PageId));
            Assert.That(returnedSummary.NotebookId, Is.EqualTo(notebookId));
        }
    }

    sealed class RecordingSearchRepository : IPageSearchRepository
    {
        readonly PageSummary _summary;

        internal RecordingSearchRepository(PageSummary summary)
        {
            _summary = summary;
        }

        internal int CountCallCount { get; private set; }

        internal int SearchCallCount { get; private set; }

        public Task<IReadOnlyList<PageSummary>> SearchAsync(
            NotebookId notebookId,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            SearchCallCount++;
            return Task.FromResult<IReadOnlyList<PageSummary>>(new[] { _summary });
        }

        public Task<int> CountMatchesAsync(
            NotebookId notebookId,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            CountCallCount++;
            return Task.FromResult(1);
        }
    }
}
