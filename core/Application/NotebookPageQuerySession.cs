#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;

namespace MemoriaNote.Application;

/// <summary>Reads ordered page summaries from one provider query.</summary>
public sealed class NotebookPageQuerySession : IAsyncDisposable
{
    private const int BatchSize = 128;

    private readonly INotebookPageQueryCursor _cursor;
    private readonly NotebookSessionLifetime _parentLifetime;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private bool _disposeRequested;
    private bool _resourcesDisposed;

    private NotebookPageQuerySession(
        INotebookPageQueryCursor cursor,
        NotebookSessionLifetime parentLifetime)
    {
        _cursor = cursor;
        _parentLifetime = parentLifetime;
    }

    /// <summary>Reads the next bounded batch without re-executing the provider query.</summary>
    /// <param name="cancellationToken">Cancels this read and ends the query session.</param>
    public async Task<NotebookOperationResult<NotebookPageQueryBatch>> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfTerminal();
        if (!_operationGate.Wait(0))
        {
            throw new InvalidOperationException(
                "Concurrent reads from one page-query session are not supported.");
        }

        try
        {
            ThrowIfTerminal();
            using var effectiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposeCancellation.Token);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var repositoryBatch = await _cursor.ReadAsync(
                        BatchSize,
                        effectiveCancellation.Token)
                    .ConfigureAwait(false);
                var batch = Map(repositoryBatch);
                if (batch.IsCompleted)
                    await DisposeResourcesAsync().ConfigureAwait(false);
                return NotebookOperationResult<NotebookPageQueryBatch>.Succeeded(batch);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await DisposeResourcesAsync().ConfigureAwait(false);
                throw;
            }
            catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
            {
                await DisposeResourcesAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (NotebookFailureFactory.IsClassifiable(exception))
            {
                await DisposeResourcesAsync().ConfigureAwait(false);
                return NotebookOperationResult<NotebookPageQueryBatch>.Failed(
                    NotebookFailureFactory.FromException(exception, "page.query"));
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

    /// <summary>Ends the query before or after its natural completion.</summary>
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

    internal static async Task<NotebookOperationResult<NotebookPageQuerySession>> StartAsync(
        Func<CancellationToken, Task<INotebookPageQueryCursor>> startCursor,
        NotebookSessionLifetime parentLifetime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startCursor);
        ArgumentNullException.ThrowIfNull(parentLifetime);
        cancellationToken.ThrowIfCancellationRequested();

        INotebookPageQueryCursor cursor;
        try
        {
            cursor = await startCursor(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (NotebookFailureFactory.IsClassifiable(exception))
        {
            return NotebookOperationResult<NotebookPageQuerySession>.Failed(
                NotebookFailureFactory.FromException(exception, "page.query"));
        }

        var session = new NotebookPageQuerySession(cursor, parentLifetime);
        try
        {
            parentLifetime.Register(session);
            return NotebookOperationResult<NotebookPageQuerySession>.Succeeded(session);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static NotebookPageQueryBatch Map(RepositoryPageQueryBatch batch) => new(
        batch.Summaries.Select(summary => new NotebookPageSummary(
            summary.NotebookId,
            summary.PageId,
            summary.Name,
            summary.CreateTimeUtc,
            summary.UpdateTimeUtc,
            summary.Revision)).ToArray(),
        batch.IsCompleted);

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
            _disposeCancellation.Dispose();
        }
    }
}
