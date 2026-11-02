using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Archive;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Reads Schema V2 authoritative values from one consistent SQLite snapshot.</summary>
internal sealed class SchemaV2ArchiveSnapshot : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;

    private SchemaV2ArchiveSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ArchiveV1MetadataRow> metadata,
        int pageCount)
    {
        _connection = connection;
        _transaction = transaction;
        Metadata = metadata;
        PageCount = pageCount;
    }

    internal IReadOnlyList<ArchiveV1MetadataRow> Metadata { get; }

    internal int PageCount { get; }

    internal static async Task<SchemaV2ArchiveSnapshot> OpenAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The source notebook does not exist.", fullPath);

        var validation = await SchemaV2DatabaseValidator.ValidateAsync(fullPath, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.Identity.IsValid ||
            !validation.Schema.IsValid ||
            !validation.Integrity.IsValid ||
            !validation.AuthoritativeValues.IsValid)
        {
            throw new InvalidDataException(
                "The Schema V2 source is not a valid authoritative notebook: " +
                string.Join(" ", validation.AllIssues));
        }

        var connection = await SqliteConnectionFactory.OpenAsync(
                fullPath,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
            try
            {
                var metadata = await ReadMetadataAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
                var pageCount = await ReadPageCountAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
                return new SchemaV2ArchiveSnapshot(connection, transaction, metadata, pageCount);
            }
            catch
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async IAsyncEnumerable<ArchiveV1ContentModel2PageRow> ReadPagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = """
            SELECT InternalId, PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text
            FROM Pages
            ORDER BY InternalId;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new ArchiveV1ContentModel2PageRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetString(6),
                reader.GetString(7));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ArchiveV1MetadataRow>> ReadMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Key, Value FROM Metadata ORDER BY Key COLLATE BINARY;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var metadata = new List<ArchiveV1MetadataRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            metadata.Add(new ArchiveV1MetadataRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1)));
        }
        return metadata.AsReadOnly();
    }

    private static async Task<int> ReadPageCountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Pages;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }
}
