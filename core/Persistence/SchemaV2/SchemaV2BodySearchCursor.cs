#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Queries;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Owns one active SQLite statement for an authoritative-body Scan.</summary>
internal sealed class SchemaV2BodySearchCursor : INotebookBodySearchCursor
{
    private readonly NotebookId _notebookId;
    private readonly RepositoryBodySearchQuery _query;
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _command;
    private readonly SqliteDataReader _reader;
    private RepositoryBodySearchHit? _bufferedHit;
    private int _deliveredCount;
    private bool _isCompleted;
    private bool _isDisposed;

    internal SchemaV2BodySearchCursor(
        NotebookId notebookId,
        RepositoryBodySearchQuery query,
        SqliteConnection connection,
        SqliteCommand command,
        SqliteDataReader reader)
    {
        _notebookId = notebookId ?? throw new ArgumentNullException(nameof(notebookId));
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _command = command ?? throw new ArgumentNullException(nameof(command));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async ValueTask<RepositoryBodySearchBatch> ReadAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isCompleted)
            throw new InvalidOperationException("The body-search cursor has completed.");
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        cancellationToken.ThrowIfCancellationRequested();

        var hits = new List<RepositoryBodySearchHit>(
            Math.Min(maximumCount, _query.Limit - _deliveredCount));
        var reachedEnd = false;
        while (hits.Count < maximumCount && _deliveredCount < _query.Limit)
        {
            var hit = await TakeNextAsync(cancellationToken).ConfigureAwait(false);
            if (hit is null)
            {
                reachedEnd = true;
                break;
            }

            hits.Add(hit);
            _deliveredCount++;
        }

        var hasMore = false;
        if (_deliveredCount == _query.Limit)
        {
            hasMore = await TakeNextAsync(cancellationToken).ConfigureAwait(false) is not null;
            _isCompleted = true;
        }
        else if (reachedEnd)
        {
            _isCompleted = true;
        }
        else if (hits.Count == maximumCount)
        {
            _bufferedHit = await ReadNextAsync(cancellationToken).ConfigureAwait(false);
            _isCompleted = _bufferedHit is null;
        }

        return new RepositoryBodySearchBatch(hits, _isCompleted, hasMore);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await _reader.DisposeAsync().ConfigureAwait(false);
        await _command.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<RepositoryBodySearchHit?> TakeNextAsync(
        CancellationToken cancellationToken)
    {
        if (_bufferedHit is not null)
        {
            var hit = _bufferedHit;
            _bufferedHit = null;
            return hit;
        }

        return await ReadNextAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RepositoryBodySearchHit?> ReadNextAsync(
        CancellationToken cancellationToken)
    {
        if (!await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new RepositoryBodySearchHit(
            _notebookId,
            PageId.FromUuid(_reader.GetString(0)),
            _reader.GetString(1),
            SchemaV2PageValueCodec.ParseTimestamp(_reader.GetString(2)),
            _query.IncludeSnippet
                ? BodySearchSnippetBuilder.Build(
                    _reader.GetString(3),
                    _query.Query.Keywords)
                : null);
    }
}
