using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies task completion, cancellation, and latest-result behavior for searches.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class SearchCoordinationTests
{
    /// <summary>
    /// Verifies that a newer request cancels and supersedes an older request.
    /// </summary>
    [Test]
    public async Task ConsecutiveSearches_ApplyOnlyTheLatestResult()
    {
        var firstCompletion = CreateCompletionSource();
        var secondCompletion = CreateCompletionSource();
        var invocations = new List<SearchInvocation>();
        var service = new ControlledSearchService((invocation, token) =>
        {
            invocations.Add(invocation with { Token = token });
            return invocations.Count == 1
                ? firstCompletion.Task
                : secondCompletion.Task;
        });

        service.SearchEntry = "first";
        service.MaxViewResultCount = 25;
        var firstTask = service.SearchHandler();

        Assert.That(firstTask.IsCompleted, Is.False);

        service.SearchEntry = "second";
        service.SearchMethod = SearchMethodType.FullText;
        service.SearchRange = SearchRangeType.Workgroup;
        service.MaxViewResultCount = 50;
        var secondTask = service.SearchHandler();

        secondCompletion.SetResult(CreateResult(2));
        var secondResult = await secondTask;
        firstCompletion.SetResult(CreateResult(1));
        var firstResult = await firstTask;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(invocations, Has.Count.EqualTo(2));
            Assert.That(invocations[0].SearchEntry, Is.EqualTo("first"));
            Assert.That(invocations[0].SearchMethod, Is.EqualTo(SearchMethodType.Heading));
            Assert.That(invocations[0].SearchRange, Is.EqualTo(SearchRangeType.Note));
            Assert.That(invocations[0].SkipCount, Is.Zero);
            Assert.That(invocations[0].TakeCount, Is.EqualTo(25));
            Assert.That(invocations[0].Token.IsCancellationRequested, Is.True);
            Assert.That(invocations[1].SearchEntry, Is.EqualTo("second"));
            Assert.That(invocations[1].SearchMethod, Is.EqualTo(SearchMethodType.FullText));
            Assert.That(invocations[1].SearchRange, Is.EqualTo(SearchRangeType.Workgroup));
            Assert.That(invocations[1].SkipCount, Is.Zero);
            Assert.That(invocations[1].TakeCount, Is.EqualTo(50));
            Assert.That(firstResult, Is.Null);
            Assert.That(secondResult, Is.Not.Null);
            Assert.That(service.ContentsCount, Is.EqualTo(2));
        }
    }

    /// <summary>
    /// Verifies that cancellation caused by a replacement request completes normally.
    /// </summary>
    [Test]
    public async Task SupersededSearch_CancellationCompletesNormally()
    {
        var secondCompletion = CreateCompletionSource();
        var invocationCount = 0;
        var service = new ControlledSearchService((_, token) =>
        {
            invocationCount++;
            return invocationCount == 1
                ? WaitForCancellationAsync(token)
                : secondCompletion.Task;
        });

        var firstTask = service.SearchHandler();
        service.SearchEntry = "replacement";
        var secondTask = service.SearchHandler();

        var firstResult = await firstTask;
        secondCompletion.SetResult(CreateResult(3));
        var secondResult = await secondTask;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResult, Is.Null);
            Assert.That(secondResult, Is.Not.Null);
            Assert.That(service.ContentsCount, Is.EqualTo(3));
        }
    }

    /// <summary>
    /// Verifies that an infrastructure failure is handled without replacing the current result.
    /// </summary>
    [Test]
    public async Task SearchInfrastructureFailure_PreservesTheCurrentResult()
    {
        var invocationCount = 0;
        var service = new ControlledSearchService((_, _) =>
        {
            invocationCount++;
            return invocationCount == 1
                ? Task.FromResult(CreateResult(4))
                : Task.FromException<SearchResult>(new IOException("Database unavailable."));
        });

        var successfulResult = await service.SearchHandler();
        var previousNotice = service.SearchNotice;
        service.SearchEntry = "failure";
        var failedResult = await service.SearchHandler();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(successfulResult, Is.Not.Null);
            Assert.That(failedResult, Is.Null);
            Assert.That(service.ContentsCount, Is.EqualTo(4));
            Assert.That(service.SearchNotice, Is.EqualTo(previousNotice));
        }
    }

    /// <summary>
    /// Verifies that unexpected failures remain observable by callers.
    /// </summary>
    [Test]
    public async Task UnexpectedSearchFailure_FaultsTheReturnedTask()
    {
        var service = new ControlledSearchService((_, _) =>
            Task.FromException<SearchResult>(new InvalidOperationException("Search failed.")));

        InvalidOperationException? exception = null;
        try
        {
            await service.SearchHandler();
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        Assert.That(exception?.Message, Is.EqualTo("Search failed."));
    }

    private static TaskCompletionSource<SearchResult> CreateCompletionSource()
    {
        return new TaskCompletionSource<SearchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static SearchResult CreateResult(int count)
    {
        return new SearchResult()
        {
            Count = count,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow
        };
    }

    private static async Task<SearchResult> WaitForCancellationAsync(CancellationToken token)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return SearchResult.Empty;
    }

    private sealed class ControlledSearchService : MemoriaNoteService
    {
        private readonly Func<SearchInvocation, CancellationToken, Task<SearchResult>> _search;

        internal ControlledSearchService(
            Func<SearchInvocation, CancellationToken, Task<SearchResult>> search)
            : base(new Workgroup())
        {
            _search = search;
        }

        protected override Task<SearchResult> SearchAsync(
            string searchEntry,
            SearchRangeType searchRange,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            var invocation = new SearchInvocation(
                searchEntry,
                searchRange,
                searchMethod,
                skipCount,
                takeCount,
                token);
            return _search(invocation, token);
        }
    }

    private sealed record SearchInvocation(
        string SearchEntry,
        SearchRangeType SearchRange,
        SearchMethodType SearchMethod,
        int SkipCount,
        int TakeCount,
        CancellationToken Token);
}
