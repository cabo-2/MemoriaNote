using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Validates Schema V2 derived data without committing database changes.</summary>
internal static class SchemaV2DerivedDataValidator
{
    /// <summary>Validates selected derived indexes using a rollback-only transaction.</summary>
    internal static async Task<SchemaV2DerivedDataValidation> ValidateAsync(
        string databasePath,
        SchemaV2DerivedIndexSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ValidateSelection(selection);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                databasePath,
                SqliteOpenMode.ReadWrite,
                cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        try
        {
            return await ValidateAsync(
                    connection,
                    transaction,
                    selection,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Validates selected derived indexes in an existing transaction.</summary>
    internal static async Task<SchemaV2DerivedDataValidation> ValidateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SchemaV2DerivedIndexSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ValidateSelection(selection);
        cancellationToken.ThrowIfCancellationRequested();

        var issues = new List<SchemaV2DerivedDataIssue>();
        if (selection.HasFlag(SchemaV2DerivedIndexSelection.PageTags))
        {
            await ValidatePageTagsAsync(connection, transaction, issues, cancellationToken)
                .ConfigureAwait(false);
        }

        if (selection.HasFlag(SchemaV2DerivedIndexSelection.FtsUnicode61))
        {
            await ValidateFtsAsync(
                    connection,
                    transaction,
                    SchemaV2DerivedIndexKind.FtsUnicode61,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (selection.HasFlag(SchemaV2DerivedIndexSelection.FtsTrigram))
        {
            await ValidateFtsAsync(
                    connection,
                    transaction,
                    SchemaV2DerivedIndexKind.FtsTrigram,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return SchemaV2DerivedDataValidation.Evaluated(issues);

        async Task ValidateFtsAsync(
            SqliteConnection activeConnection,
            SqliteTransaction activeTransaction,
            SchemaV2DerivedIndexKind index,
            CancellationToken token)
        {
            await using var command = activeConnection.CreateCommand();
            command.Transaction = activeTransaction;
            command.CommandText = index switch
            {
                SchemaV2DerivedIndexKind.FtsUnicode61 =>
                    "INSERT INTO FtsIndexUnicode61(FtsIndexUnicode61, rank) " +
                    "VALUES('integrity-check', 1);",
                SchemaV2DerivedIndexKind.FtsTrigram =>
                    "INSERT INTO FtsIndexTrigram(FtsIndexTrigram, rank) " +
                    "VALUES('integrity-check', 1);",
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

            try
            {
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (
                exception.SqliteErrorCode == 11 ||
                exception.SqliteExtendedErrorCode == 267)
            {
                issues.Add(new SchemaV2DerivedDataIssue(
                    index,
                    SchemaV2DerivedDataIssueKind.ContentMismatch,
                    RowCount: null,
                    $"Derived index '{index}' does not match Pages."));
            }
        }
    }

    private static async Task ValidatePageTagsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ICollection<SchemaV2DerivedDataIssue> issues,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH Expected(PageInternalId, Label) AS (
                SELECT Pages.InternalId, json_each.value
                FROM Pages, json_each(Pages.Tags)
            )
            SELECT
                (
                    SELECT COUNT(*)
                    FROM Expected
                    LEFT JOIN PageTags
                      ON PageTags.PageInternalId = Expected.PageInternalId
                     AND PageTags.Label = Expected.Label COLLATE BINARY
                    WHERE PageTags.PageInternalId IS NULL
                ),
                (
                    SELECT COUNT(*)
                    FROM PageTags
                    LEFT JOIN Expected
                      ON Expected.PageInternalId = PageTags.PageInternalId
                     AND Expected.Label = PageTags.Label COLLATE BINARY
                    WHERE Expected.PageInternalId IS NULL
                );
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The PageTags integrity query returned no row.");

        var missingCount = reader.GetInt64(0);
        var unexpectedCount = reader.GetInt64(1);
        if (missingCount > 0)
        {
            issues.Add(new SchemaV2DerivedDataIssue(
                SchemaV2DerivedIndexKind.PageTags,
                SchemaV2DerivedDataIssueKind.MissingRows,
                missingCount,
                $"PageTags is missing {missingCount} expected row(s)."));
        }

        if (unexpectedCount > 0)
        {
            issues.Add(new SchemaV2DerivedDataIssue(
                SchemaV2DerivedIndexKind.PageTags,
                SchemaV2DerivedDataIssueKind.UnexpectedRows,
                unexpectedCount,
                $"PageTags contains {unexpectedCount} unexpected row(s)."));
        }
    }

    internal static void ValidateSelection(SchemaV2DerivedIndexSelection selection)
    {
        if (selection == SchemaV2DerivedIndexSelection.None ||
            (selection & ~SchemaV2DerivedIndexSelection.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }
    }
}
