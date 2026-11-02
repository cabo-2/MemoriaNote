using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Loads current logical records into a validated new Schema V2 database.</summary>
internal static class SchemaV2NotebookSink
{
    /// <summary>Creates and publishes a new Schema V2 database without replacing a destination.</summary>
    internal static async Task<SchemaV2NotebookSinkResult> PublishAsync(
        string destinationPath,
        CurrentNotebookTransfer transfer,
        IReadOnlyCollection<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        return await PublishAsync(
                destinationPath,
                transfer,
                sourcePaths,
                SchemaV2DatabaseValidator.ValidateAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Creates and publishes using an explicit post-load validation operation.</summary>
    internal static async Task<SchemaV2NotebookSinkResult> PublishAsync(
        string destinationPath,
        CurrentNotebookTransfer transfer,
        IReadOnlyCollection<string> sourcePaths,
        Func<string, CancellationToken, Task<SchemaV2DatabaseValidation>> validateAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentNullException.ThrowIfNull(validateAsync);
        cancellationToken.ThrowIfCancellationRequested();

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        ValidatePathConflicts(fullDestinationPath, sourcePaths);
        using var output = PendingSqliteOutput.Create(fullDestinationPath);
        var expected = await LoadAsync(output.StagingPath, transfer, cancellationToken)
            .ConfigureAwait(false);

        var validation = await validateAsync(output.StagingPath, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                "The private Schema V2 database failed validation: " +
                string.Join(" ", validation.AllIssues));
        }

        var actual = await ReadFingerprintAsync(output.StagingPath, cancellationToken)
            .ConfigureAwait(false);
        if (!expected.Matches(actual))
            throw new InvalidDataException("The private Schema V2 database is not logically equivalent to its input.");

        cancellationToken.ThrowIfCancellationRequested();
        FlushToDisk(output.StagingPath);
        output.Commit();
        return new SchemaV2NotebookSinkResult(expected, validation);
    }

    private static async Task<NotebookLogicalFingerprint> LoadAsync(
        string databasePath,
        CurrentNotebookTransfer transfer,
        CancellationToken cancellationToken)
    {
        var fingerprint = new NotebookLogicalFingerprintBuilder();
        await using var connection = SqliteConnectionFactory.Create(
            databasePath,
            SqliteOpenMode.ReadWriteCreate);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = SchemaV2Resources.ReadCanonicalDdl();
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertMetadataAsync(
                connection,
                transaction,
                transfer.Metadata,
                fingerprint,
                cancellationToken)
            .ConfigureAwait(false);
        await InsertPagesAsync(
                connection,
                transaction,
                transfer.Pages,
                fingerprint,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return fingerprint.Build();
    }

    private static async Task InsertMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CurrentNotebookMetadataRecord> metadata,
        NotebookLogicalFingerprintBuilder fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Metadata(Key, Value) VALUES ($key, $value);";
        var key = command.Parameters.Add("$key", SqliteType.Text);
        var value = command.Parameters.Add("$value", SqliteType.Text);

        foreach (var row in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            key.Value = row.Key;
            value.Value = row.Value is null ? DBNull.Value : row.Value;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            fingerprint.Add(row);
        }
    }

    private static async Task InsertPagesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IAsyncEnumerable<CurrentNotebookPageRecord> pages,
        NotebookLogicalFingerprintBuilder fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Pages(PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
            VALUES ($pageId, $name, $createTime, $updateTime, $revision, $tags, $text);
            """;
        var pageId = command.Parameters.Add("$pageId", SqliteType.Text);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var createTime = command.Parameters.Add("$createTime", SqliteType.Text);
        var updateTime = command.Parameters.Add("$updateTime", SqliteType.Text);
        var revision = command.Parameters.Add("$revision", SqliteType.Integer);
        var tags = command.Parameters.Add("$tags", SqliteType.Text);
        var text = command.Parameters.Add("$text", SqliteType.Text);

        await foreach (var row in pages.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            pageId.Value = row.PageId;
            name.Value = row.Name;
            createTime.Value = row.CreateTime;
            updateTime.Value = row.UpdateTime;
            revision.Value = row.Revision;
            tags.Value = row.Tags;
            text.Value = row.Text;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            fingerprint.Add(row);
        }
    }

    internal static async Task<NotebookLogicalFingerprint> ReadFingerprintAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var fingerprint = new NotebookLogicalFingerprintBuilder();
        await using var connection = await SqliteConnectionFactory.OpenAsync(
                databasePath,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);

        await using (var metadata = connection.CreateCommand())
        {
            metadata.CommandText = "SELECT Key, Value FROM Metadata ORDER BY Key COLLATE BINARY;";
            await using var reader = await metadata.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                fingerprint.Add(new CurrentNotebookMetadataRecord(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
        }

        await using (var pages = connection.CreateCommand())
        {
            pages.CommandText = """
                SELECT PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text
                FROM Pages
                ORDER BY PageId COLLATE BINARY;
                """;
            await using var reader = await pages.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                fingerprint.Add(new CurrentNotebookPageRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.GetString(5),
                    reader.GetString(6)));
            }
        }

        return fingerprint.Build();
    }

    private static void ValidatePathConflicts(
        string destinationPath,
        IEnumerable<string> sourcePaths)
    {
        foreach (var sourcePath in sourcePaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            if (string.Equals(
                    destinationPath,
                    Path.GetFullPath(sourcePath),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                throw new IOException("The Schema V2 destination conflicts with an input path.");
            }
        }
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

/// <summary>Reports the validated logical result published by the Schema V2 sink.</summary>
internal sealed record SchemaV2NotebookSinkResult(
    NotebookLogicalFingerprint Fingerprint,
    SchemaV2DatabaseValidation Validation);
