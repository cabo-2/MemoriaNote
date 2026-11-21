using MemoriaNote.Application;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>Verifies page-query session cancellation, failure, and ownership.</summary>
[TestFixture]
public sealed class NotebookPageQuerySessionTests
{
    /// <summary>Verifies a fixed bounded read reaches natural completion and closes the cursor.</summary>
    [Test]
    public async Task ReadAsync_CompletedBatch_DisposesCursor()
    {
        var cursor = new CompletedCursor();
        var lifetime = new NotebookSessionLifetime();
        var started = await NotebookPageQuerySession.StartAsync(
            _ => Task.FromResult<INotebookPageQueryCursor>(cursor),
            lifetime,
            CancellationToken.None);

        var result = await started.Value!.ReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.IsCompleted, Is.True);
            Assert.That(result.Value.Summaries, Is.Empty);
            Assert.That(cursor.MaximumCount, Is.EqualTo(128));
            Assert.That(cursor.IsDisposed, Is.True);
        }
        await lifetime.DisposeAsync();
    }

    /// <summary>Verifies caller cancellation propagates and ends the query session.</summary>
    [Test]
    public async Task ReadAsync_CallerCanceled_ThrowsCancellationAndDisposesCursor()
    {
        var cursor = new WaitingCursor();
        var lifetime = new NotebookSessionLifetime();
        var started = await NotebookPageQuerySession.StartAsync(
            _ => Task.FromResult<INotebookPageQueryCursor>(cursor),
            lifetime,
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await started.Value!.ReadAsync(cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(cursor.IsDisposed, Is.True);
        await lifetime.DisposeAsync();
    }

    /// <summary>Verifies the owning notebook session closes an unfinished query.</summary>
    [Test]
    public async Task ParentDispose_ClosesRegisteredQuerySession()
    {
        var cursor = new IncompleteCursor();
        var lifetime = new NotebookSessionLifetime();
        var started = await NotebookPageQuerySession.StartAsync(
            _ => Task.FromResult<INotebookPageQueryCursor>(cursor),
            lifetime,
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

    /// <summary>Verifies provider failures are classified and close the cursor.</summary>
    [Test]
    public async Task ReadAsync_StorageFailure_ReturnsFailureAndDisposesCursor()
    {
        var cursor = new FailingCursor();
        var lifetime = new NotebookSessionLifetime();
        var started = await NotebookPageQuerySession.StartAsync(
            _ => Task.FromResult<INotebookPageQueryCursor>(cursor),
            lifetime,
            CancellationToken.None);

        var result = await started.Value!.ReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure!.Category,
                Is.EqualTo(NotebookFailureCategory.AccessOrBusy));
            Assert.That(result.Failure.Code, Is.EqualTo("page.query.access_denied"));
            Assert.That(cursor.IsDisposed, Is.True);
        }
        await lifetime.DisposeAsync();
    }

    private sealed class CompletedCursor : INotebookPageQueryCursor
    {
        internal int MaximumCount { get; private set; }

        internal bool IsDisposed { get; private set; }

        public ValueTask<RepositoryPageQueryBatch> ReadAsync(
            int maximumCount,
            CancellationToken cancellationToken)
        {
            MaximumCount = maximumCount;
            return ValueTask.FromResult(new RepositoryPageQueryBatch([], true));
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class IncompleteCursor : INotebookPageQueryCursor
    {
        internal bool IsDisposed { get; private set; }

        public ValueTask<RepositoryPageQueryBatch> ReadAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new RepositoryPageQueryBatch([], false));

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WaitingCursor : INotebookPageQueryCursor
    {
        internal bool IsDisposed { get; private set; }

        public async ValueTask<RepositoryPageQueryBatch> ReadAsync(
            int maximumCount,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The canceled cursor unexpectedly resumed.");
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingCursor : INotebookPageQueryCursor
    {
        internal bool IsDisposed { get; private set; }

        public ValueTask<RepositoryPageQueryBatch> ReadAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("private path");

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
