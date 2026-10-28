using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Creates and safely publishes empty Schema V2 databases.</summary>
internal static class SchemaV2DatabaseFactory
{
    /// <summary>Creates a new Schema V2 database with its required metadata.</summary>
    internal static async Task CreateAsync(
        string databasePath,
        string name,
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        cancellationToken.ThrowIfCancellationRequested();

        var canonicalDdl = SchemaV2Resources.ReadCanonicalDdl();
        using var output = PendingSqliteOutput.Create(databasePath);

        await CreatePrivateDatabaseAsync(
                output.StagingPath,
                canonicalDdl,
                name,
                title,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        FlushToDisk(output.StagingPath);
        output.Commit();
    }

    private static async Task CreatePrivateDatabaseAsync(
        string databasePath,
        string canonicalDdl,
        string name,
        string title,
        CancellationToken cancellationToken)
    {
        await using var connection = SqliteConnectionFactory.Create(
            databasePath,
            SqliteOpenMode.ReadWriteCreate);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var schemaCommand = connection.CreateCommand())
        {
            schemaCommand.Transaction = (SqliteTransaction)transaction;
            schemaCommand.CommandText = canonicalDdl;
            await schemaCommand
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await InsertMetadataAsync(
                connection,
                (SqliteTransaction)transaction,
                name,
                title,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnableForeignKeysAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        string title,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Metadata(Key, Value) VALUES
                ('Name', $name),
                ('Title', $title),
                ('Version', $version);
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$version", SchemaV2Format.MetadataVersion);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void FlushToDisk(string databasePath)
    {
        using var stream = new FileStream(
            databasePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read);
        stream.Flush(flushToDisk: true);
    }
}
