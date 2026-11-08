#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Owns one active SQLite statement for a streaming body search.</summary>
internal sealed class SchemaV2BodySearchCursor : INotebookBodySearchCursor
{
    private readonly int _limit;
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _command;
    private readonly SqliteDataReader _reader;
    private readonly Func<SqliteDataReader, RepositoryBodySearchHit> _readHit;
    private readonly Func<Exception, Exception?>? _translateFailure;
    private RepositoryBodySearchHit? _bufferedHit;
    private int _deliveredCount;
    private bool _isCompleted;
    private bool _isDisposed;

    internal SchemaV2BodySearchCursor(
        int limit,
        SqliteConnection connection,
        SqliteCommand command,
        SqliteDataReader reader,
        Func<SqliteDataReader, RepositoryBodySearchHit> readHit,
        Func<Exception, Exception?>? translateFailure = null)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _command = command ?? throw new ArgumentNullException(nameof(command));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _readHit = readHit ?? throw new ArgumentNullException(nameof(readHit));
        _translateFailure = translateFailure;
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
            Math.Min(maximumCount, _limit - _deliveredCount));
        var reachedEnd = false;
        while (hits.Count < maximumCount && _deliveredCount < _limit)
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
        if (_deliveredCount == _limit)
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
        try
        {
            if (!await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            return _readHit(_reader);
        }
        catch (Exception exception) when (_translateFailure is not null)
        {
            var translated = _translateFailure(exception);
            if (translated is null)
                throw;
            throw translated;
        }
    }
}
