#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Owns one active SQLite statement for a streaming page-name query.</summary>
internal sealed class SchemaV2PageQueryCursor : INotebookPageQueryCursor
{
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _command;
    private readonly SqliteDataReader _reader;
    private readonly Func<SqliteDataReader, RepositoryPageSummary> _readSummary;
    private RepositoryPageSummary? _bufferedSummary;
    private bool _isCompleted;
    private bool _isDisposed;

    internal SchemaV2PageQueryCursor(
        SqliteConnection connection,
        SqliteCommand command,
        SqliteDataReader reader,
        Func<SqliteDataReader, RepositoryPageSummary> readSummary)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _command = command ?? throw new ArgumentNullException(nameof(command));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _readSummary = readSummary ?? throw new ArgumentNullException(nameof(readSummary));
    }

    public async ValueTask<RepositoryPageQueryBatch> ReadAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isCompleted)
            throw new InvalidOperationException("The page-query cursor has completed.");
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        cancellationToken.ThrowIfCancellationRequested();

        var summaries = new List<RepositoryPageSummary>(maximumCount);
        while (summaries.Count < maximumCount)
        {
            var summary = await TakeNextAsync(cancellationToken).ConfigureAwait(false);
            if (summary is null)
            {
                _isCompleted = true;
                break;
            }

            summaries.Add(summary);
        }

        if (!_isCompleted && summaries.Count == maximumCount)
        {
            _bufferedSummary = await ReadNextAsync(cancellationToken).ConfigureAwait(false);
            _isCompleted = _bufferedSummary is null;
        }

        return new RepositoryPageQueryBatch(summaries, _isCompleted);
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

    private async ValueTask<RepositoryPageSummary?> TakeNextAsync(
        CancellationToken cancellationToken)
    {
        if (_bufferedSummary is not null)
        {
            var summary = _bufferedSummary;
            _bufferedSummary = null;
            return summary;
        }

        return await ReadNextAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RepositoryPageSummary?> ReadNextAsync(
        CancellationToken cancellationToken)
    {
        if (!await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return _readSummary(_reader);
    }
}
