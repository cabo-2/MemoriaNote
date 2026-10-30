using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Reads and updates Schema V2 metadata with parameterized direct SQL.</summary>
internal sealed class SchemaV2MetadataRepository :
    INotebookMetadataReader,
    INotebookMetadataWriter
{
    internal const string FormatVersionKey = "Version";

    public async Task<NotebookMetadataSnapshot> LoadAsync(
        NotebookId notebookId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                notebookId.Locator,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);
        return await ReadSnapshotAsync(
                notebookId,
                connection,
                transaction: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NotebookMetadataSnapshot> UpdateAsync(
        NotebookId notebookId,
        NotebookMetadataChanges changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        ArgumentNullException.ThrowIfNull(changes);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateChanges(changes);

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                notebookId.Locator,
                SqliteOpenMode.ReadWrite,
                cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var change in changes.Values)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Metadata(Key, Value)
                VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            command.Parameters.AddWithValue("$key", change.Key);
            command.Parameters.AddWithValue(
                "$value",
                change.Value == null ? DBNull.Value : change.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var snapshot = await ReadSnapshotAsync(
                notebookId,
                connection,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    private static void ValidateChanges(NotebookMetadataChanges changes)
    {
        foreach (var change in changes.Values)
        {
            if (string.IsNullOrEmpty(change.Key))
                throw new ArgumentException("A metadata key cannot be empty.", nameof(changes));
            if (string.Equals(change.Key, FormatVersionKey, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The notebook format version cannot be changed through metadata updates.",
                    nameof(changes));
            }
        }
    }

    private static async Task<NotebookMetadataSnapshot> ReadSnapshotAsync(
        NotebookId notebookId,
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Key, Value
            FROM Metadata
            ORDER BY Key COLLATE BINARY;
            """;
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1));
        }

        return new NotebookMetadataSnapshot(notebookId, values);
    }
}
