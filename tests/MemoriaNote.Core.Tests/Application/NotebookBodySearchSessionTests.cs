using MemoriaNote.Application;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>Verifies body-search session deadlines, cancellation, and ownership.</summary>
[TestFixture]
public sealed class NotebookBodySearchSessionTests
{
    /// <summary>Verifies the deadline also bounds creation of the provider cursor.</summary>
    [Test]
    public async Task StartAsync_DeadlineExpires_ReturnsTimeout()
    {
        var lifetime = new NotebookSessionLifetime();

        var result = await NotebookBodySearchSession.StartAsync(
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new CompletedCursor();
            },
            lifetime,
            TimeSpan.FromMilliseconds(20),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure!.Category, Is.EqualTo(NotebookFailureCategory.Timeout));
            Assert.That(result.Failure.Code, Is.EqualTo("page.search.timeout"));
        }
        await lifetime.DisposeAsync();
    }

    /// <summary>Verifies one deadline covers cursor startup and all incremental reads.</summary>
    [Test]
    public async Task ReadAsync_DeadlineExpires_ReturnsTimeoutAndDisposesCursor()
    {
        var cursor = new WaitingCursor();
        var lifetime = new NotebookSessionLifetime();
        var started = await NotebookBodySearchSession.StartAsync(
            _ => Task.FromResult<INotebookBodySearchCursor>(cursor),
            lifetime,
            TimeSpan.FromMilliseconds(20),
            CancellationToken.None);

        var result = await started.Value!.ReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure!.Category, Is.EqualTo(NotebookFailureCategory.Timeout));
            Assert.That(result.Failure.Code, Is.EqualTo("page.search.timeout"));
            Assert.That(cursor.IsDisposed, Is.True);
        }
        await lifetime.DisposeAsync();
    }

    /// <summary>Verifies caller cancellation is propagated and ends the session.</summary>
    [Test]
    public async Task ReadAsync_CallerCanceled_ThrowsCancellationAndDisposesCursor()
    {
        var cursor = new WaitingCursor();
        var lifetime = new NotebookSessionLifetime();
        var started = await NotebookBodySearchSession.StartAsync(
            _ => Task.FromResult<INotebookBodySearchCursor>(cursor),
            lifetime,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await started.Value!.ReadAsync(cancellationToken: cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(cursor.IsDisposed, Is.True);
        await lifetime.DisposeAsync();
    }

    /// <summary>Verifies the owning notebook session closes an unfinished search.</summary>
    [Test]
    public async Task ParentDispose_ClosesRegisteredSearchSession()
    {
        var cursor = new CompletedCursor();
        var lifetime = new NotebookSessionLifetime();
        var started = await NotebookBodySearchSession.StartAsync(
            _ => Task.FromResult<INotebookBodySearchCursor>(cursor),
            lifetime,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        await lifetime.DisposeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cursor.IsDisposed, Is.True);
            Assert.That(
                async () => await started.Value!.ReadAsync(),
                Throws.InstanceOf<ObjectDisposedException>());
        }
    }

    /// <summary>Verifies provider failures during streaming are classified and close the cursor.</summary>
    [Test]
    public async Task ReadAsync_StorageFailure_ReturnsFailureAndDisposesCursor()
    {
        var cursor = new FailingCursor();
        var lifetime = new NotebookSessionLifetime();
        var started = await NotebookBodySearchSession.StartAsync(
            _ => Task.FromResult<INotebookBodySearchCursor>(cursor),
            lifetime,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        var result = await started.Value!.ReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure!.Category,
                Is.EqualTo(NotebookFailureCategory.AccessOrBusy));
            Assert.That(result.Failure.Code, Is.EqualTo("page.search.access_denied"));
            Assert.That(cursor.IsDisposed, Is.True);
        }
        await lifetime.DisposeAsync();
    }

    /// <summary>Verifies invalid batch sizes do not consume or close the cursor.</summary>
    [Test]
    public async Task ReadAsync_InvalidBatchSize_ReturnsValidationWithoutConsumingCursor()
    {
        var cursor = new CompletedCursor();
        var lifetime = new NotebookSessionLifetime();
        var started = await NotebookBodySearchSession.StartAsync(
            _ => Task.FromResult<INotebookBodySearchCursor>(cursor),
            lifetime,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);
        await using var session = started.Value!;

        var invalid = await session.ReadAsync(0);
        var completed = await session.ReadAsync(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(invalid.IsSuccess, Is.False);
            Assert.That(invalid.Failure!.Code, Is.EqualTo("page.search.invalid_batch_size"));
            Assert.That(completed.IsSuccess, Is.True);
            Assert.That(completed.Value!.IsCompleted, Is.True);
            Assert.That(cursor.ReadCount, Is.EqualTo(1));
        }
        await lifetime.DisposeAsync();
    }

    private sealed class WaitingCursor : INotebookBodySearchCursor
    {
        internal bool IsDisposed { get; private set; }

        public async ValueTask<RepositoryBodySearchBatch> ReadAsync(
            int maximumCount,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CompletedCursor : INotebookBodySearchCursor
    {
        internal bool IsDisposed { get; private set; }

        internal int ReadCount { get; private set; }

        public ValueTask<RepositoryBodySearchBatch> ReadAsync(
            int maximumCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return ValueTask.FromResult(new RepositoryBodySearchBatch([], true, false));
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingCursor : INotebookBodySearchCursor
    {
        internal bool IsDisposed { get; private set; }

        public ValueTask<RepositoryBodySearchBatch> ReadAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException();

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
