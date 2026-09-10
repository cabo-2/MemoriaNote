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
public sealed class SqliteNoteSearchRepositoryTests
{
    readonly INoteSearchRepository _repository =
        new SqliteNoteSearchRepository(
            new SqliteNoteDatabaseFactory(NullLoggerFactory.Instance));

    /// <summary>
    /// Verifies that SQL-like text is treated as a search value rather than query structure.
    /// </summary>
    [Test]
    public async Task SearchAsync_SqlLikeHeading_ReturnsOnlyTheLiteralMatch()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var searchEntry = "Alpha' OR 1 = 1 --";
        var expected = note.CreatePage(searchEntry, "Literal query text.");
        note.CreatePage("Unrelated", "Another page.");

        var result = await _repository.SearchAsync(
            database.DatabasePath,
            searchEntry,
            SearchMethodType.Heading,
            0,
            10,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(
                result.Contents.Select(content => content.Guid),
                Is.EqualTo(new[] { expected.Guid }));
            Assert.That(
                result.Contents.Single().OwnerDataSource,
                Is.EqualTo(Path.GetFullPath(database.DatabasePath)));
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
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var page = note.CreatePage(searchEntry, "Special heading body.");

        var result = await _repository.SearchAsync(
            database.DatabasePath,
            searchEntry,
            SearchMethodType.Heading,
            0,
            10,
            CancellationToken.None);

        Assert.That(result.Count, Is.EqualTo(expectedCount));
        Assert.That(result.Contents, Has.Count.EqualTo(expectedCount));
        if (expectedCount == 1)
            Assert.That(result.Contents.Single().Guid, Is.EqualTo(page.Guid));
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
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        note.CreatePage("Apostrophe", "O'Brien wrote this text.");
        note.CreatePage("Quotation", "A \"quoted\" word appears here.");
        note.CreatePage("Unicode", "日本語");
        note.CreatePage("Symbols", "100% under_score.");

        var result = await _repository.SearchAsync(
            database.DatabasePath,
            searchEntry,
            SearchMethodType.FullText,
            0,
            10,
            CancellationToken.None);

        if (expectedPageName == null)
        {
            Assert.That(result.Count, Is.Zero);
            Assert.That(result.Contents, Is.Empty);
        }
        else
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.Contents.Single().Name, Is.EqualTo(expectedPageName));
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
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        note.CreatePage("%", "A marker appears here.");
        note.CreatePage("Alpha", "Another marker appears here.");
        note.CreatePage("Alphabet", "No matching body token.");

        var count = await _repository.CountAsync(
            database.DatabasePath,
            searchEntry,
            searchMethod,
            CancellationToken.None);
        var result = await _repository.SearchAsync(
            database.DatabasePath,
            searchEntry,
            searchMethod,
            0,
            1,
            CancellationToken.None);

        Assert.That(count, Is.EqualTo(result.Count));
    }

    /// <summary>
    /// Verifies that a pre-cancelled token prevents repository database access.
    /// </summary>
    [Test]
    public async Task Operations_PreCancelledToken_ThrowOperationCancelledException()
    {
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("test-note", "Test Note");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        OperationCanceledException? searchException = null;
        OperationCanceledException? countException = null;

        try
        {
            await _repository.SearchAsync(
                database.DatabasePath,
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
            await _repository.CountAsync(
                database.DatabasePath,
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
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        note.CreatePage("Alpha", "Search marker.");
        SqliteConnection.ClearAllPools();
        File.Delete(database.DatabasePath);
        SqliteException? exception = null;

        try
        {
            await _repository.SearchAsync(
                database.DatabasePath,
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
    /// Verifies that workgroup totals follow the same exact-heading query as result retrieval.
    /// </summary>
    [Test]
    public async Task WorkgroupSearch_SpecialHeading_KeepsCountAndContentsConsistent()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        note.CreatePage("%", "Special heading.");
        var workgroup = new Workgroup();
        workgroup.Notes.Add(note);
        workgroup.SelectedNote = note;

        var result = await workgroup.SearchAsync(
            "%",
            SearchRangeType.Workgroup,
            SearchMethodType.Heading,
            0,
            10,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.Zero);
            Assert.That(result.Contents, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies that Note and Workgroup delegate searches through an injected repository.
    /// </summary>
    [Test]
    public async Task WorkgroupSearch_InjectedRepository_PreservesOwnerInformation()
    {
        using var database = new TemporaryNoteDatabase();
        var content = Content.Create<Content>("Injected");
        var repository = new RecordingSearchRepository(content);
        var note = new Note(database.DatabasePath, repository);
        var workgroup = new Workgroup();
        workgroup.Notes.Add(note);
        workgroup.SelectedNote = note;

        var result = await workgroup.SearchAsync(
            "Injected",
            SearchRangeType.Workgroup,
            SearchMethodType.Heading,
            0,
            10,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            var returnedContent = result.Contents.Single();
            Assert.That(repository.CountCallCount, Is.EqualTo(1));
            Assert.That(repository.SearchCallCount, Is.EqualTo(1));
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(returnedContent.Guid, Is.EqualTo(content.Guid));
            Assert.That(returnedContent.Parent, Is.SameAs(note));
            Assert.That(
                returnedContent.OwnerDataSource,
                Is.EqualTo(Path.GetFullPath(database.DatabasePath)));
        }
    }

    sealed class RecordingSearchRepository : INoteSearchRepository
    {
        readonly Content _content;

        internal RecordingSearchRepository(Content content)
        {
            _content = content;
        }

        internal int CountCallCount { get; private set; }

        internal int SearchCallCount { get; private set; }

        public Task<SearchResult> SearchAsync(
            string dataSource,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            SearchCallCount++;
            return Task.FromResult(new SearchResult()
            {
                Contents = new List<Content>() { _content },
                Count = 1,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow
            });
        }

        public Task<int> CountAsync(
            string dataSource,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            CountCallCount++;
            return Task.FromResult(1);
        }
    }
}
