using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Implements Schema V2 operational page storage with parameterized direct SQL.</summary>
internal sealed class SchemaV2PageRepository :
    INotebookPageReader,
    INotebookPageWriter
{
    private readonly IClock _clock;

    internal SchemaV2PageRepository(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<RepositoryPage> FindByIdAsync(
        NotebookId notebookId,
        PageId pageId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        ArgumentNullException.ThrowIfNull(pageId);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                notebookId.Locator,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);
        return await ReadByIdAsync(
                notebookId,
                connection,
                transaction: null,
                pageId.ToUuid(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RepositoryPage> FindByNameAsync(
        NotebookId notebookId,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        SchemaV2PageValueCodec.ValidateName(name, nameof(name));
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                notebookId.Locator,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreatePageSelectCommand(connection, transaction: null);
        command.CommandText += " WHERE Name = $name COLLATE BINARY LIMIT 1;";
        command.Parameters.AddWithValue("$name", name);
        return await ReadSinglePageAsync(notebookId, command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RepositoryPage>> ListByIdPrefixAsync(
        NotebookId notebookId,
        string normalizedPageIdPrefix,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));

        var lowerBound = SchemaV2PageValueCodec
            .ToStoragePageIdPrefix(normalizedPageIdPrefix);
        var upperBound = SchemaV2PageValueCodec.NextBinaryPrefix(lowerBound);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                notebookId.Locator,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreatePageSelectCommand(connection, transaction: null);
        command.CommandText += upperBound == null
            ? " WHERE PageId >= $lower COLLATE BINARY"
            : " WHERE PageId >= $lower COLLATE BINARY AND PageId < $upper COLLATE BINARY";
        command.CommandText += " ORDER BY PageId COLLATE BINARY LIMIT $limit;";
        command.Parameters.AddWithValue("$lower", lowerBound);
        if (upperBound != null)
            command.Parameters.AddWithValue("$upper", upperBound);
        command.Parameters.AddWithValue("$limit", maximumCount);

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var pages = new List<RepositoryPage>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            pages.Add(MapPage(notebookId, reader));
        return pages.AsReadOnly();
    }

    public async Task<int> CountAsync(
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
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Pages;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<RepositoryPageSummary>> ListSummariesAsync(
        NotebookId notebookId,
        int skipCount,
        int takeCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        if (skipCount < 0)
            throw new ArgumentOutOfRangeException(nameof(skipCount));
        if (takeCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(takeCount));
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                notebookId.Locator,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PageId, Name, CreateTime, UpdateTime, Revision
            FROM Pages
            ORDER BY Name COLLATE BINARY, PageId COLLATE BINARY
            LIMIT $take OFFSET $skip;
            """;
        command.Parameters.AddWithValue("$take", takeCount);
        command.Parameters.AddWithValue("$skip", skipCount);

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var summaries = new List<RepositoryPageSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            summaries.Add(new RepositoryPageSummary(
                notebookId,
                PageId.FromUuid(reader.GetString(0)),
                reader.GetString(1),
                SchemaV2PageValueCodec.ParseTimestamp(reader.GetString(2)),
                SchemaV2PageValueCodec.ParseTimestamp(reader.GetString(3)),
                reader.GetInt64(4)));
        }

        return summaries.AsReadOnly();
    }

    public async Task<RepositoryPageWriteResult> CreateAsync(
        NotebookId notebookId,
        RepositoryPageCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        ArgumentNullException.ThrowIfNull(request);
        var tags = ValidateAndEncode(request.Name, request.Text, request.Tags, nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        var pageId = PageId.FromGuid(Guid.NewGuid());
        var timestamp = SchemaV2PageValueCodec.FormatTimestamp(_clock.UtcNow);
        await using var connection = await OpenWriteConnectionAsync(
                notebookId,
                cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Pages(
                    PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
                VALUES (
                    $pageId, $name, $createTime, $updateTime, 1, $tags, $text);
                """;
            command.Parameters.AddWithValue("$pageId", pageId.ToUuid());
            command.Parameters.AddWithValue("$name", request.Name);
            command.Parameters.AddWithValue("$createTime", timestamp);
            command.Parameters.AddWithValue("$updateTime", timestamp);
            command.Parameters.AddWithValue("$tags", tags);
            command.Parameters.AddWithValue("$text", request.Text);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (IsConstraintFailure(exception))
        {
            if (await NameExistsAsync(
                    connection,
                    transaction,
                    request.Name,
                    excludedPageId: null,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return RepositoryPageWriteResult.Failed(
                    RepositoryPageWriteStatus.NameConflict);
            }

            throw;
        }

        var page = await ReadByIdAsync(
                notebookId,
                connection,
                transaction,
                pageId.ToUuid(),
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RepositoryPageWriteResult.Succeeded(page);
    }

    public async Task<RepositoryPageWriteResult> UpdateAsync(
        NotebookId notebookId,
        PageId pageId,
        long expectedRevision,
        RepositoryPageUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        ArgumentNullException.ThrowIfNull(pageId);
        ArgumentNullException.ThrowIfNull(request);
        if (expectedRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        var tags = ValidateAndEncode(request.Name, request.Text, request.Tags, nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        var pageIdText = pageId.ToUuid();
        var timestamp = SchemaV2PageValueCodec.FormatTimestamp(_clock.UtcNow);
        await using var connection = await OpenWriteConnectionAsync(
                notebookId,
                cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        int changed;
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Pages
                SET Name = $name,
                    UpdateTime = $updateTime,
                    Revision = Revision + 1,
                    Tags = $tags,
                    Text = $text
                WHERE PageId = $pageId COLLATE BINARY
                  AND Revision = $expectedRevision;
                """;
            command.Parameters.AddWithValue("$name", request.Name);
            command.Parameters.AddWithValue("$updateTime", timestamp);
            command.Parameters.AddWithValue("$tags", tags);
            command.Parameters.AddWithValue("$text", request.Text);
            command.Parameters.AddWithValue("$pageId", pageIdText);
            command.Parameters.AddWithValue("$expectedRevision", expectedRevision);
            changed = await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SqliteException exception) when (IsConstraintFailure(exception))
        {
            if (await NameExistsAsync(
                    connection,
                    transaction,
                    request.Name,
                    pageIdText,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return RepositoryPageWriteResult.Failed(
                    RepositoryPageWriteStatus.NameConflict);
            }

            throw;
        }

        if (changed == 0)
        {
            var exists = await PageExistsAsync(
                    connection,
                    transaction,
                    pageIdText,
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return RepositoryPageWriteResult.Failed(
                exists
                    ? RepositoryPageWriteStatus.RevisionConflict
                    : RepositoryPageWriteStatus.NotFound);
        }

        var page = await ReadByIdAsync(
                notebookId,
                connection,
                transaction,
                pageIdText,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RepositoryPageWriteResult.Succeeded(page);
    }

    public async Task<RepositoryPageWriteResult> DeleteAsync(
        NotebookId notebookId,
        PageId pageId,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        ArgumentNullException.ThrowIfNull(pageId);
        if (expectedRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        cancellationToken.ThrowIfCancellationRequested();

        var pageIdText = pageId.ToUuid();
        await using var connection = await OpenWriteConnectionAsync(
                notebookId,
                cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM Pages
            WHERE PageId = $pageId COLLATE BINARY
              AND Revision = $expectedRevision;
            """;
        command.Parameters.AddWithValue("$pageId", pageIdText);
        command.Parameters.AddWithValue("$expectedRevision", expectedRevision);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        if (changed == 0)
        {
            var exists = await PageExistsAsync(
                    connection,
                    transaction,
                    pageIdText,
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return RepositoryPageWriteResult.Failed(
                exists
                    ? RepositoryPageWriteStatus.RevisionConflict
                    : RepositoryPageWriteStatus.NotFound);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RepositoryPageWriteResult.Succeeded();
    }

    private static string ValidateAndEncode(
        string name,
        string text,
        IReadOnlyList<string> tags,
        string parameterName)
    {
        SchemaV2PageValueCodec.ValidateName(name, parameterName);
        if (text == null)
            throw new ArgumentException("Page text cannot be null.", parameterName);
        if (tags == null)
            throw new ArgumentException("Page tags cannot be null.", parameterName);
        return SchemaV2TagCodec.Encode(tags);
    }

    private static async Task<SqliteConnection> OpenWriteConnectionAsync(
        NotebookId notebookId,
        CancellationToken cancellationToken)
    {
        var connection = await SqliteConnectionFactory.OpenAsync(
                notebookId.Locator,
                SqliteOpenMode.ReadWrite,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static SqliteCommand CreatePageSelectCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text
            FROM Pages
            """;
        return command;
    }

    private static async Task<RepositoryPage> ReadByIdAsync(
        NotebookId notebookId,
        SqliteConnection connection,
        SqliteTransaction transaction,
        string pageId,
        CancellationToken cancellationToken)
    {
        await using var command = CreatePageSelectCommand(connection, transaction);
        command.CommandText += " WHERE PageId = $pageId COLLATE BINARY LIMIT 1;";
        command.Parameters.AddWithValue("$pageId", pageId);
        return await ReadSinglePageAsync(notebookId, command, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<RepositoryPage> ReadSinglePageAsync(
        NotebookId notebookId,
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? MapPage(notebookId, reader)
            : null;
    }

    private static RepositoryPage MapPage(
        NotebookId notebookId,
        SqliteDataReader reader)
    {
        return new RepositoryPage(
            notebookId,
            PageId.FromUuid(reader.GetString(0)),
            reader.GetString(1),
            SchemaV2PageValueCodec.ParseTimestamp(reader.GetString(2)),
            SchemaV2PageValueCodec.ParseTimestamp(reader.GetString(3)),
            reader.GetInt64(4),
            SchemaV2TagCodec.Decode(reader.GetString(5)),
            reader.GetString(6));
    }

    private static async Task<bool> PageExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string pageId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM Pages
            WHERE PageId = $pageId COLLATE BINARY
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$pageId", pageId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) != null;
    }

    private static async Task<bool> NameExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        string excludedPageId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = excludedPageId == null
            ? """
                SELECT 1 FROM Pages
                WHERE Name = $name COLLATE BINARY
                LIMIT 1;
                """
            : """
                SELECT 1 FROM Pages
                WHERE Name = $name COLLATE BINARY
                  AND PageId <> $pageId COLLATE BINARY
                LIMIT 1;
                """;
        command.Parameters.AddWithValue("$name", name);
        if (excludedPageId != null)
            command.Parameters.AddWithValue("$pageId", excludedPageId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) != null;
    }

    private static bool IsConstraintFailure(SqliteException exception)
    {
        return exception.SqliteErrorCode == 19;
    }
}
