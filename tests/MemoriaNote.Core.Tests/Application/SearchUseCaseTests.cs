using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>
/// Verifies search coordination without a database.
/// </summary>
[TestFixture]
public sealed class SearchUseCaseTests
{
    /// <summary>
    /// Verifies note-scoped search and its owner-qualified result.
    /// </summary>
    [Test]
    public async Task SearchAsync_NoteScope_ReturnsTheRequestedSlice()
    {
        var notebookId = CreateNotebookId("note-scope");
        var first = CreateSummary(notebookId, "Alpha");
        var second = CreateSummary(notebookId, "Beta");
        var repository = new FakeSearchRepository(
            new Dictionary<NotebookId, IReadOnlyList<PageSummary>>
            {
                [notebookId] = new[] { first, second }
            });
        var request = SearchRequest.ForNotebook(
            "marker",
            SearchMethodType.FullText,
            notebookId,
            1,
            1);

        var result = await new SearchUseCase(repository)
            .SearchAsync(request, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Is.EqualTo(new[] { second }));
            Assert.That(result.TotalCount, Is.EqualTo(2));
            Assert.That(result.Offset, Is.EqualTo(1));
            Assert.That(result.Limit, Is.EqualTo(1));
            Assert.That(repository.CountCalls.Single().Method, Is.EqualTo(SearchMethodType.FullText));
            Assert.That(repository.SearchCalls.Single().Query, Is.EqualTo("marker"));
        }
    }

    /// <summary>
    /// Verifies that a missing note target produces an empty result without repository access.
    /// </summary>
    [Test]
    public async Task SearchAsync_NoteScopeWithoutTarget_ReturnsEmptyPage()
    {
        var repository = new FakeSearchRepository(
            new Dictionary<NotebookId, IReadOnlyList<PageSummary>>());
        var request = SearchRequest.ForNotebook(
            "query",
            SearchMethodType.Heading,
            null!,
            4,
            2);

        var result = await new SearchUseCase(repository)
            .SearchAsync(request, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.TotalCount, Is.Zero);
            Assert.That(result.Offset, Is.EqualTo(4));
            Assert.That(result.Limit, Is.EqualTo(2));
            Assert.That(repository.CountCalls, Is.Empty);
            Assert.That(repository.SearchCalls, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies workspace order, cross-note paging, and the unpaged total count.
    /// </summary>
    [Test]
    public async Task SearchAsync_WorkspaceScope_PagesAcrossNotesInTargetOrder()
    {
        var firstNoteId = CreateNotebookId("first-workspace");
        var secondNoteId = CreateNotebookId("second-workspace");
        var thirdNoteId = CreateNotebookId("third-workspace");
        var firstAlpha = CreateSummary(firstNoteId, "Alpha");
        var firstBeta = CreateSummary(firstNoteId, "Beta");
        var secondAlpha = CreateSummary(secondNoteId, "Alpha");
        var thirdAlpha = CreateSummary(thirdNoteId, "Alpha");
        var repository = new FakeSearchRepository(
            new Dictionary<NotebookId, IReadOnlyList<PageSummary>>
            {
                [firstNoteId] = new[] { firstAlpha, firstBeta },
                [secondNoteId] = new[] { secondAlpha },
                [thirdNoteId] = new[] { thirdAlpha }
            });
        var request = SearchRequest.ForWorkspace(
            "*",
            SearchMethodType.Heading,
            new[] { firstNoteId, secondNoteId, thirdNoteId },
            1,
            2);

        var result = await new SearchUseCase(repository)
            .SearchAsync(request, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Is.EqualTo(new[] { firstBeta, secondAlpha }));
            Assert.That(result.TotalCount, Is.EqualTo(4));
            Assert.That(
                repository.CountCalls.Select(call => call.NotebookId),
                Is.EqualTo(new[] { firstNoteId, secondNoteId, thirdNoteId }));
            Assert.That(
                repository.SearchCalls.Select(call => call.NotebookId),
                Is.EqualTo(new[] { firstNoteId, secondNoteId }));
            Assert.That(
                repository.SearchCalls.Select(call => (call.Offset, call.Limit)),
                Is.EqualTo(new[] { (1, 1), (0, 1) }));
        }
    }

    /// <summary>
    /// Verifies paging boundaries return no items while retaining the total count.
    /// </summary>
    /// <param name="offset">The offset at or beyond the final result.</param>
    /// <param name="limit">The requested result limit.</param>
    [TestCase(2, 10)]
    [TestCase(3, 10)]
    [TestCase(0, 0)]
    public async Task SearchAsync_EmptySlice_RetainsTotalCount(int offset, int limit)
    {
        var notebookId = CreateNotebookId($"boundary-{offset}-{limit}");
        var repository = new FakeSearchRepository(
            new Dictionary<NotebookId, IReadOnlyList<PageSummary>>
            {
                [notebookId] = new[]
                {
                    CreateSummary(notebookId, "Alpha"),
                    CreateSummary(notebookId, "Beta")
                }
            });
        var request = SearchRequest.ForWorkspace(
            "*",
            SearchMethodType.Heading,
            new[] { notebookId },
            offset,
            limit);

        var result = await new SearchUseCase(repository)
            .SearchAsync(request, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.TotalCount, Is.EqualTo(2));
            Assert.That(repository.CountCalls, Has.Count.EqualTo(1));
            Assert.That(repository.SearchCalls, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies cancellation is propagated through a count operation.
    /// </summary>
    [Test]
    public void SearchAsync_CountCancellation_IsPropagated()
    {
        var notebookId = CreateNotebookId("count-cancellation");
        using var cancellation = new CancellationTokenSource();
        var repository = new FakeSearchRepository(
            new Dictionary<NotebookId, IReadOnlyList<PageSummary>>
            {
                [notebookId] = new[] { CreateSummary(notebookId, "Alpha") }
            })
        {
            BeforeCount = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
        };
        var request = SearchRequest.ForNotebook(
            "query",
            SearchMethodType.Heading,
            notebookId,
            0,
            1);
        Func<Task> search = () => new SearchUseCase(repository)
            .SearchAsync(request, cancellation.Token);

        Assert.That(
            search,
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>
    /// Verifies cancellation is propagated through a result query.
    /// </summary>
    [Test]
    public void SearchAsync_QueryCancellation_IsPropagated()
    {
        var notebookId = CreateNotebookId("query-cancellation");
        using var cancellation = new CancellationTokenSource();
        var repository = new FakeSearchRepository(
            new Dictionary<NotebookId, IReadOnlyList<PageSummary>>
            {
                [notebookId] = new[] { CreateSummary(notebookId, "Alpha") }
            })
        {
            BeforeSearch = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
        };
        var request = SearchRequest.ForNotebook(
            "query",
            SearchMethodType.Heading,
            notebookId,
            0,
            1);
        Func<Task> search = () => new SearchUseCase(repository)
            .SearchAsync(request, cancellation.Token);

        Assert.That(
            search,
            Throws.InstanceOf<OperationCanceledException>());
    }

    static NotebookId CreateNotebookId(string name)
    {
        return NotebookId.FromDatabasePath(Path.Combine(Path.GetTempPath(), $"{name}.db"));
    }

    static PageSummary CreateSummary(NotebookId notebookId, string name)
    {
        return new PageSummary(
            notebookId,
            PageId.FromGuid(Guid.NewGuid()),
            name,
            1,
            new Dictionary<string, string>(),
            nameof(Content),
            DateTime.UtcNow,
            DateTime.UtcNow,
            false);
    }

    sealed class FakeSearchRepository : IPageSearchRepository
    {
        readonly IReadOnlyDictionary<NotebookId, IReadOnlyList<PageSummary>> _items;

        internal FakeSearchRepository(
            IReadOnlyDictionary<NotebookId, IReadOnlyList<PageSummary>> items)
        {
            _items = items;
        }

        internal List<SearchCall> CountCalls { get; } = new();

        internal List<SearchCall> SearchCalls { get; } = new();

        internal Action<CancellationToken>? BeforeCount { get; init; }

        internal Action<CancellationToken>? BeforeSearch { get; init; }

        public Task<int> CountMatchesAsync(
            NotebookId notebookId,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            CountCalls.Add(new SearchCall(notebookId, searchEntry, searchMethod, 0, 0));
            BeforeCount?.Invoke(token);
            token.ThrowIfCancellationRequested();
            return Task.FromResult(_items[notebookId].Count);
        }

        public Task<IReadOnlyList<PageSummary>> SearchPageSummariesAsync(
            NotebookId notebookId,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            SearchCalls.Add(
                new SearchCall(notebookId, searchEntry, searchMethod, skipCount, takeCount));
            BeforeSearch?.Invoke(token);
            token.ThrowIfCancellationRequested();
            IReadOnlyList<PageSummary> result = _items[notebookId]
                .Skip(skipCount)
                .Take(takeCount)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<SearchResult> SearchAsync(
            string dataSource,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<int> CountMatchesAsync(
            string dataSource,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

    sealed record SearchCall(
        NotebookId NotebookId,
        string Query,
        SearchMethodType Method,
        int Offset,
        int Limit);
}
