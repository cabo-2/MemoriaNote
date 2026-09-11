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
        var noteId = CreateNoteId("note-scope");
        var first = CreateSummary(noteId, "Alpha");
        var second = CreateSummary(noteId, "Beta");
        var repository = new FakeSearchRepository(
            new Dictionary<NoteId, IReadOnlyList<PageSummary>>
            {
                [noteId] = new[] { first, second }
            });
        var request = SearchRequest.ForNote(
            "marker",
            SearchMethodType.FullText,
            noteId,
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
            new Dictionary<NoteId, IReadOnlyList<PageSummary>>());
        var request = SearchRequest.ForNote(
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
    /// Verifies workgroup order, cross-note paging, and the unpaged total count.
    /// </summary>
    [Test]
    public async Task SearchAsync_WorkgroupScope_PagesAcrossNotesInTargetOrder()
    {
        var firstNoteId = CreateNoteId("first-workgroup");
        var secondNoteId = CreateNoteId("second-workgroup");
        var thirdNoteId = CreateNoteId("third-workgroup");
        var firstAlpha = CreateSummary(firstNoteId, "Alpha");
        var firstBeta = CreateSummary(firstNoteId, "Beta");
        var secondAlpha = CreateSummary(secondNoteId, "Alpha");
        var thirdAlpha = CreateSummary(thirdNoteId, "Alpha");
        var repository = new FakeSearchRepository(
            new Dictionary<NoteId, IReadOnlyList<PageSummary>>
            {
                [firstNoteId] = new[] { firstAlpha, firstBeta },
                [secondNoteId] = new[] { secondAlpha },
                [thirdNoteId] = new[] { thirdAlpha }
            });
        var request = SearchRequest.ForWorkgroup(
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
                repository.CountCalls.Select(call => call.NoteId),
                Is.EqualTo(new[] { firstNoteId, secondNoteId, thirdNoteId }));
            Assert.That(
                repository.SearchCalls.Select(call => call.NoteId),
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
        var noteId = CreateNoteId($"boundary-{offset}-{limit}");
        var repository = new FakeSearchRepository(
            new Dictionary<NoteId, IReadOnlyList<PageSummary>>
            {
                [noteId] = new[]
                {
                    CreateSummary(noteId, "Alpha"),
                    CreateSummary(noteId, "Beta")
                }
            });
        var request = SearchRequest.ForWorkgroup(
            "*",
            SearchMethodType.Heading,
            new[] { noteId },
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
        var noteId = CreateNoteId("count-cancellation");
        using var cancellation = new CancellationTokenSource();
        var repository = new FakeSearchRepository(
            new Dictionary<NoteId, IReadOnlyList<PageSummary>>
            {
                [noteId] = new[] { CreateSummary(noteId, "Alpha") }
            })
        {
            BeforeCount = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
        };
        var request = SearchRequest.ForNote(
            "query",
            SearchMethodType.Heading,
            noteId,
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
        var noteId = CreateNoteId("query-cancellation");
        using var cancellation = new CancellationTokenSource();
        var repository = new FakeSearchRepository(
            new Dictionary<NoteId, IReadOnlyList<PageSummary>>
            {
                [noteId] = new[] { CreateSummary(noteId, "Alpha") }
            })
        {
            BeforeSearch = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
        };
        var request = SearchRequest.ForNote(
            "query",
            SearchMethodType.Heading,
            noteId,
            0,
            1);
        Func<Task> search = () => new SearchUseCase(repository)
            .SearchAsync(request, cancellation.Token);

        Assert.That(
            search,
            Throws.InstanceOf<OperationCanceledException>());
    }

    static NoteId CreateNoteId(string name)
    {
        return NoteId.FromDataSource(Path.Combine(Path.GetTempPath(), $"{name}.db"));
    }

    static PageSummary CreateSummary(NoteId noteId, string name)
    {
        return new PageSummary(
            noteId,
            PageId.FromGuid(Guid.NewGuid()),
            name,
            1,
            new Dictionary<string, string>(),
            nameof(Content),
            DateTime.UtcNow,
            DateTime.UtcNow,
            false);
    }

    sealed class FakeSearchRepository : INoteSearchRepository
    {
        readonly IReadOnlyDictionary<NoteId, IReadOnlyList<PageSummary>> _items;

        internal FakeSearchRepository(
            IReadOnlyDictionary<NoteId, IReadOnlyList<PageSummary>> items)
        {
            _items = items;
        }

        internal List<SearchCall> CountCalls { get; } = new();

        internal List<SearchCall> SearchCalls { get; } = new();

        internal Action<CancellationToken>? BeforeCount { get; init; }

        internal Action<CancellationToken>? BeforeSearch { get; init; }

        public Task<int> CountAsync(
            NoteId noteId,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            CountCalls.Add(new SearchCall(noteId, searchEntry, searchMethod, 0, 0));
            BeforeCount?.Invoke(token);
            token.ThrowIfCancellationRequested();
            return Task.FromResult(_items[noteId].Count);
        }

        public Task<IReadOnlyList<PageSummary>> SearchPageSummariesAsync(
            NoteId noteId,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            SearchCalls.Add(
                new SearchCall(noteId, searchEntry, searchMethod, skipCount, takeCount));
            BeforeSearch?.Invoke(token);
            token.ThrowIfCancellationRequested();
            IReadOnlyList<PageSummary> result = _items[noteId]
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

        public Task<int> CountAsync(
            string dataSource,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

    sealed record SearchCall(
        NoteId NoteId,
        string Query,
        SearchMethodType Method,
        int Offset,
        int Limit);
}
