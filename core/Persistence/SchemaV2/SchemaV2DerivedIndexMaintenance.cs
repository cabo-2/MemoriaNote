using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Checks and rebuilds the fixed Schema V2 derived indexes.</summary>
internal static class SchemaV2DerivedIndexMaintenance
{
    /// <summary>Checks selected derived indexes without committing database changes.</summary>
    internal static Task<SchemaV2DerivedDataValidation> CheckAsync(
        string databasePath,
        SchemaV2DerivedIndexSelection selection,
        CancellationToken cancellationToken = default) =>
        SchemaV2DerivedDataValidator.ValidateAsync(
            databasePath,
            selection,
            cancellationToken);

    /// <summary>Rebuilds selected derived indexes and commits only a valid result.</summary>
    internal static async Task<SchemaV2DerivedIndexRebuildResult> RebuildAsync(
        string databasePath,
        SchemaV2DerivedIndexSelection selection,
        CancellationToken cancellationToken = default)
    {
        return await RebuildAsync(
                databasePath,
                selection,
                SchemaV2DerivedDataValidator.ValidateAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Rebuilds selected indexes using an explicit post-validation operation.</summary>
    internal static async Task<SchemaV2DerivedIndexRebuildResult> RebuildAsync(
        string databasePath,
        SchemaV2DerivedIndexSelection selection,
        Func<
            SqliteConnection,
            SqliteTransaction,
            SchemaV2DerivedIndexSelection,
            CancellationToken,
            Task<SchemaV2DerivedDataValidation>> postValidateAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(postValidateAsync);
        SchemaV2DerivedDataValidator.ValidateSelection(selection);
        cancellationToken.ThrowIfCancellationRequested();

        var validation = await SchemaV2DatabaseValidator
            .ValidateAsync(databasePath, cancellationToken)
            .ConfigureAwait(false);
        EnsureRebuildPrerequisites(validation);

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                databasePath,
                SqliteOpenMode.ReadWrite,
                cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);

        await RebuildSelectedAsync(
                connection,
                transaction,
                selection,
                cancellationToken)
            .ConfigureAwait(false);
        var postValidation = await postValidateAsync(
                connection,
                transaction,
                selection,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!postValidation.IsValid)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return new SchemaV2DerivedIndexRebuildResult(
                WasCommitted: false,
                postValidation);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new SchemaV2DerivedIndexRebuildResult(
            WasCommitted: true,
            postValidation);
    }

    private static async Task RebuildSelectedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SchemaV2DerivedIndexSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection.HasFlag(SchemaV2DerivedIndexSelection.PageTags))
        {
            await ExecuteAsync(
                    connection,
                    transaction,
                    SchemaV2Resources.ReadRebuildPageTags(),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (selection.HasFlag(SchemaV2DerivedIndexSelection.FtsUnicode61))
        {
            await ExecuteAsync(
                    connection,
                    transaction,
                    SchemaV2Resources.ReadRebuildFtsUnicode61(),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (selection.HasFlag(SchemaV2DerivedIndexSelection.FtsTrigram))
        {
            await ExecuteAsync(
                    connection,
                    transaction,
                    SchemaV2Resources.ReadRebuildFtsTrigram(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureRebuildPrerequisites(SchemaV2DatabaseValidation validation)
    {
        if (validation.Identity.IsValid &&
            validation.Schema.IsValid &&
            validation.Integrity.IsValid &&
            validation.AuthoritativeValues.IsValid)
        {
            return;
        }

        var issues = validation.Identity.Issues
            .Concat(validation.Schema.Issues)
            .Concat(validation.Integrity.Issues)
            .Concat(validation.AuthoritativeValues.Issues);
        throw new InvalidDataException(
            "Schema V2 derived indexes cannot be rebuilt because prerequisite " +
            "validation failed: " + string.Join(" ", issues));
    }
}

/// <summary>Reports whether a selected derived-index rebuild was committed.</summary>
internal sealed record SchemaV2DerivedIndexRebuildResult(
    bool WasCommitted,
    SchemaV2DerivedDataValidation Validation);
