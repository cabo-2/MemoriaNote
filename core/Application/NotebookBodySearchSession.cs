#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;

namespace MemoriaNote.Application;

/// <summary>Reads ordered body-search hits from one provider query.</summary>
public sealed class NotebookBodySearchSession : IAsyncDisposable
{
    /// <summary>The default maximum number of hits returned by one read.</summary>
    public const int DefaultBatchSize = 20;

    /// <summary>The largest accepted number of hits returned by one read.</summary>
    public const int MaximumBatchSize = 1000;

    private readonly INotebookBodySearchCursor _cursor;
    private readonly NotebookSessionLifetime _parentLifetime;
    private readonly CancellationTokenSource _timeoutCancellation;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private bool _disposeRequested;
    private bool _resourcesDisposed;

    private NotebookBodySearchSession(
        INotebookBodySearchCursor cursor,
        NotebookSessionLifetime parentLifetime,
        CancellationTokenSource timeoutCancellation)
    {
        _cursor = cursor;
        _parentLifetime = parentLifetime;
        _timeoutCancellation = timeoutCancellation;
    }

    /// <summary>Reads the next ordered batch without re-executing the provider query.</summary>
    /// <param name="maximumCount">The positive maximum number of hits to return.</param>
    /// <param name="cancellationToken">Cancels this read and ends the search session.</param>
    public async Task<NotebookOperationResult<NotebookBodySearchBatch>> ReadAsync(
        int maximumCount = DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        ThrowIfTerminal();
        if (maximumCount is <= 0 or > MaximumBatchSize)
        {
            return NotebookOperationResult<NotebookBodySearchBatch>.Failed(
                NotebookFailureFactory.Validation(
                    "page.search.invalid_batch_size",
                    $"A body-search batch size must be between 1 and {MaximumBatchSize}."));
        }
        if (!_operationGate.Wait(0))
        {
            throw new InvalidOperationException(
                "Concurrent reads from one body-search session are not supported.");
        }

        try
        {
            ThrowIfTerminal();
            using var effectiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _timeoutCancellation.Token,
                _disposeCancellation.Token);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var repositoryBatch = await _cursor.ReadAsync(
                        maximumCount,
                        effectiveCancellation.Token)
                    .ConfigureAwait(false);
                var batch = Map(repositoryBatch);
                if (batch.IsCompleted)
                    await DisposeResourcesAsync().ConfigureAwait(false);
                return NotebookOperationResult<NotebookBodySearchBatch>.Succeeded(batch);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await DisposeResourcesAsync().ConfigureAwait(false);
                throw;
            }
            catch (OperationCanceledException) when (_timeoutCancellation.IsCancellationRequested)
            {
                await DisposeResourcesAsync().ConfigureAwait(false);
                return NotebookOperationResult<NotebookBodySearchBatch>.Failed(
                    NotebookFailureFactory.Timeout(
                        "page.search.timeout",
                        "The operation exceeded its execution time limit."));
            }
            catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
            {
                await DisposeResourcesAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (NotebookFailureFactory.IsClassifiable(exception))
            {
                await DisposeResourcesAsync().ConfigureAwait(false);
                return NotebookOperationResult<NotebookBodySearchBatch>.Failed(
                    NotebookFailureFactory.FromException(exception, "page.search"));
            }
            catch
            {
                await DisposeResourcesAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>Ends the search before or after its natural completion.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            if (_resourcesDisposed)
                return;
            _disposeRequested = true;
        }
        _disposeCancellation.Cancel();

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeResourcesAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal static async Task<NotebookOperationResult<NotebookBodySearchSession>> StartAsync(
        Func<CancellationToken, Task<INotebookBodySearchCursor>> startCursor,
        NotebookSessionLifetime parentLifetime,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startCursor);
        ArgumentNullException.ThrowIfNull(parentLifetime);
        cancellationToken.ThrowIfCancellationRequested();

        var timeoutCancellation = new CancellationTokenSource();
        timeoutCancellation.CancelAfter(timeout);
        using var effectiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        INotebookBodySearchCursor cursor;
        try
        {
            cursor = await startCursor(effectiveCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            timeoutCancellation.Dispose();
            throw;
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            timeoutCancellation.Dispose();
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                NotebookFailureFactory.Timeout(
                    "page.search.timeout",
                    "The operation exceeded its execution time limit."));
        }
        catch (Exception exception) when (NotebookFailureFactory.IsClassifiable(exception))
        {
            timeoutCancellation.Dispose();
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                NotebookFailureFactory.FromException(exception, "page.search"));
        }
        catch
        {
            timeoutCancellation.Dispose();
            throw;
        }

        var session = new NotebookBodySearchSession(
            cursor,
            parentLifetime,
            timeoutCancellation);
        try
        {
            parentLifetime.Register(session);
            return NotebookOperationResult<NotebookBodySearchSession>.Succeeded(session);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static NotebookBodySearchBatch Map(RepositoryBodySearchBatch batch) => new(
        batch.Hits.Select(hit => new NotebookBodySearchHit(
            hit.NotebookId,
            hit.PageId,
            hit.Name,
            hit.UpdateTimeUtc,
            hit.Snippet is null
                ? null
                : new NotebookBodySearchSnippet(
                    hit.Snippet.Text,
                    hit.Snippet.Matches.Select(match => new NotebookBodySearchMatch(
                        match.Start,
                        match.Length)).ToArray()))).ToArray(),
        batch.IsCompleted,
        batch.HasMore);

    private void ThrowIfTerminal()
    {
        lock (_stateGate)
            ObjectDisposedException.ThrowIf(_disposeRequested || _resourcesDisposed, this);
        _parentLifetime.ThrowIfDisposed();
    }

    private async ValueTask DisposeResourcesAsync()
    {
        lock (_stateGate)
        {
            if (_resourcesDisposed)
                return;
            _disposeRequested = true;
            _resourcesDisposed = true;
        }

        _disposeCancellation.Cancel();
        try
        {
            await _cursor.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _parentLifetime.Unregister(this);
            _timeoutCancellation.Dispose();
            _disposeCancellation.Dispose();
        }
    }
}
