using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Models;
using MemoriaNote.Transfer;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Provides Schema V2 persistence for flat text import and export workflows.</summary>
public sealed class SchemaV2NotebookTransferRepository :
    INotebookTransferRepository,
    ITextPageImportRepository
{
    private readonly IClock _clock;

    /// <summary>Initializes the repository with the production clock.</summary>
    public SchemaV2NotebookTransferRepository()
        : this(SystemClock.Instance)
    {
    }

    /// <summary>Initializes the repository with an explicit clock.</summary>
    /// <param name="clock">The clock used for imported page timestamps.</param>
    public SchemaV2NotebookTransferRepository(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Page>> ListPagesAsync(
        NotebookId notebookId,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        token.ThrowIfCancellationRequested();
        await using var connection = await SqliteConnectionFactory.OpenAsync(
                notebookId.Locator,
                SqliteOpenMode.ReadOnly,
                token)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PageId, Name, CreateTime, UpdateTime, Text
            FROM Pages
            ORDER BY InternalId;
            """;
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var pages = new List<Page>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            pages.Add(new Page
            {
                Guid = PageId.FromUuid(reader.GetString(0)).Value,
                Name = reader.GetString(1),
                Index = 1,
                ContentType = "T",
                CreateTime = SchemaV2PageValueCodec.ParseTimestamp(reader.GetString(2)).UtcDateTime,
                UpdateTime = SchemaV2PageValueCodec.ParseTimestamp(reader.GetString(3)).UtcDateTime,
                IsErased = false,
                Text = reader.GetString(4)
            });
        }
        return pages.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task AddPagesAsync(
        NotebookId notebookId,
        IReadOnlyCollection<Page> pages,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        ArgumentNullException.ThrowIfNull(pages);
        var items = pages.Select(page => new TextPageImportItem(page.Name, page.Text ?? string.Empty))
            .ToArray();
        var result = await ImportAsync(
                notebookId,
                items,
                TextPageImportConflictPolicy.Fail,
                dryRun: false,
                token)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Schema V2 page transfer failed: {result.ErrorCode}.");
        }
    }

    /// <inheritdoc />
    public async Task<TextPageImportResult> ImportAsync(
        NotebookId notebookId,
        IReadOnlyCollection<TextPageImportItem> items,
        TextPageImportConflictPolicy conflictPolicy,
        bool dryRun,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        ArgumentNullException.ThrowIfNull(items);
        if (!Enum.IsDefined(conflictPolicy))
            throw new ArgumentOutOfRangeException(nameof(conflictPolicy));
        token.ThrowIfCancellationRequested();

        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            SchemaV2PageValueCodec.ValidateName(item.Name, nameof(items));
            ArgumentNullException.ThrowIfNull(item.Text);
        }

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                notebookId.Locator,
                SqliteOpenMode.ReadWrite,
                token)
            .ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, token).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(token)
            .ConfigureAwait(false);

        if (await IsReadOnlyAsync(connection, transaction, token).ConfigureAwait(false))
            return TextPageImportResult.Failed(TextPageImportErrorCode.ReadOnly);

        var existingNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (await NameExistsAsync(connection, transaction, item.Name, token)
                .ConfigureAwait(false))
            {
                existingNames.Add(item.Name);
            }
        }

        if (conflictPolicy == TextPageImportConflictPolicy.Fail && existingNames.Count > 0)
        {
            return TextPageImportResult.Failed(
                TextPageImportErrorCode.ExistingPageConflict);
        }

        var createdCount = 0;
        var replacedCount = 0;
        var skippedCount = 0;
        var timestamp = SchemaV2PageValueCodec.FormatTimestamp(_clock.UtcNow);
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            if (!existingNames.Contains(item.Name))
            {
                createdCount++;
                if (!dryRun)
                {
                    await InsertAsync(
                            connection,
                            transaction,
                            item,
                            timestamp,
                            token)
                        .ConfigureAwait(false);
                }
                continue;
            }

            if (conflictPolicy == TextPageImportConflictPolicy.Skip)
            {
                skippedCount++;
                continue;
            }

            replacedCount++;
            if (!dryRun)
            {
                await ReplaceAsync(
                        connection,
                        transaction,
                        item,
                        timestamp,
                        token)
                    .ConfigureAwait(false);
            }
        }

        if (!dryRun)
            await transaction.CommitAsync(token).ConfigureAwait(false);
        return TextPageImportResult.Succeeded(createdCount, replacedCount, skippedCount);
    }

    private static async Task EnableForeignKeysAsync(
        SqliteConnection connection,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task<bool> IsReadOnlyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Value FROM Metadata WHERE Key = 'ReadOnly';";
        return string.Equals(
            Convert.ToString(await command.ExecuteScalarAsync(token).ConfigureAwait(false)),
            bool.TrueString,
            StringComparison.Ordinal);
    }

    private static async Task<bool> NameExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM Pages WHERE Name = $name COLLATE BINARY LIMIT 1;";
        command.Parameters.AddWithValue("$name", name);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not null;
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TextPageImportItem item,
        string timestamp,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Pages(PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
            VALUES($pageId, $name, $created, $updated, 1, '[]', $text);
            """;
        command.Parameters.AddWithValue("$pageId", PageId.FromGuid(Guid.NewGuid()).ToUuid());
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$created", timestamp);
        command.Parameters.AddWithValue("$updated", timestamp);
        command.Parameters.AddWithValue("$text", item.Text);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task ReplaceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TextPageImportItem item,
        string timestamp,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Pages
            SET Text = $text,
                UpdateTime = $updated,
                Revision = Revision + 1
            WHERE Name = $name COLLATE BINARY;
            """;
        command.Parameters.AddWithValue("$text", item.Text);
        command.Parameters.AddWithValue("$updated", timestamp);
        command.Parameters.AddWithValue("$name", item.Name);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
