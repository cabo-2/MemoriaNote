using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Creates and safely publishes empty Schema V2 databases.</summary>
internal static class SchemaV2DatabaseFactory
{
    /// <summary>Creates a new Schema V2 database with its required metadata.</summary>
    internal static async Task CreateAsync(
        string databasePath,
        string title,
        CancellationToken cancellationToken = default)
    {
        await CreateAsync(
                databasePath,
                title,
                SystemClock.Instance,
                SchemaV2DatabaseValidator.ValidateAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Creates a new Schema V2 database using the supplied application clock.</summary>
    internal static async Task CreateAsync(
        string databasePath,
        string title,
        IClock clock,
        CancellationToken cancellationToken = default)
    {
        await CreateAsync(
                databasePath,
                title,
                clock,
                SchemaV2DatabaseValidator.ValidateAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Creates a database using an explicit publication validation operation.</summary>
    internal static async Task CreateAsync(
        string databasePath,
        string title,
        IClock clock,
        Func<string, CancellationToken, Task<SchemaV2DatabaseValidation>> validateAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(validateAsync);
        cancellationToken.ThrowIfCancellationRequested();

        var canonicalDdl = SchemaV2Resources.ReadCanonicalDdl();
        using var output = PendingSqliteOutput.Create(databasePath);

        await CreatePrivateDatabaseAsync(
                output.StagingPath,
                canonicalDdl,
                title,
                SchemaV2MetadataContract.FormatCreateTime(clock.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validateAsync(output.StagingPath, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                "The private Schema V2 database failed validation: " +
                string.Join(" ", validation.AllIssues));
        }

        cancellationToken.ThrowIfCancellationRequested();
        FlushToDisk(output.StagingPath);
        output.Commit();
    }

    private static async Task CreatePrivateDatabaseAsync(
        string databasePath,
        string canonicalDdl,
        string title,
        string createTime,
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
                title,
                createTime,
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
        string title,
        string createTime,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Metadata(Key, Value) VALUES
                ('Author', ''),
                ('CreateTime', $createTime),
                ('Description', ''),
                ('ReadOnly', 'False'),
                ('Tag', ''),
                ('Title', $title);
            """;
        command.Parameters.AddWithValue("$createTime", createTime);
        command.Parameters.AddWithValue("$title", title);
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
