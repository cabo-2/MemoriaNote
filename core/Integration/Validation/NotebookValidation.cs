#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;

namespace MemoriaNote.Integration.Validation;

/// <summary>Creates revisioned first-party validation operations for an application session.</summary>
public static class NotebookValidation
{
    /// <summary>Creates a validation session bound to a validated application session.</summary>
    /// <param name="applicationSession">The current-format application session.</param>
    /// <returns>The validation integration session.</returns>
    public static NotebookValidationSession CreateSession(
        NotebookApplicationSession applicationSession)
    {
        ArgumentNullException.ThrowIfNull(applicationSession);
        applicationSession.Lifetime.ThrowIfDisposed();
        return new NotebookValidationSession(applicationSession);
    }
}

/// <summary>
/// Provides revisioned derived-data validation operations for first-party verification tools.
/// </summary>
/// <remarks>
/// This surface may change or be removed in a later integration contract revision. It is not an
/// end-user search-mode contract.
/// </remarks>
public sealed class NotebookValidationSession
{
    private readonly NotebookApplicationSession _applicationSession;

    internal NotebookValidationSession(NotebookApplicationSession applicationSession)
    {
        _applicationSession = applicationSession;
    }

    /// <summary>Checks the selected derived data without committing database changes.</summary>
    /// <param name="selection">The explicit candidates to check.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The integrity report or a classified integration failure.</returns>
    public async Task<NotebookOperationResult<DerivedDataCheckReport>> CheckAsync(
        IReadOnlyCollection<DerivedDataKind> selection,
        CancellationToken cancellationToken = default)
    {
        _applicationSession.Lifetime.ThrowIfDisposed();
        if (!TryMapSelection(selection, out var internalSelection, out var expanded))
        {
            return NotebookOperationResult<DerivedDataCheckReport>.Failed(
                DerivedFailure(
                    "derived.unsupported_selection",
                    "The derived-data selection is empty or contains an unsupported value."));
        }

        var prerequisiteFailure = await ValidatePrerequisitesAsync(cancellationToken)
            .ConfigureAwait(false);
        if (prerequisiteFailure is not null)
            return NotebookOperationResult<DerivedDataCheckReport>.Failed(prerequisiteFailure);

        var result = await NotebookOperationExecutor.ExecuteAsync(
                () => SchemaV2DerivedIndexMaintenance.CheckAsync(
                    _applicationSession.Notebook.NotebookId.ToString(),
                    internalSelection,
                    cancellationToken),
                "derived.check",
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? NotebookOperationResult<DerivedDataCheckReport>.Succeeded(
                Map(result.Value!, expanded))
            : NotebookOperationResult<DerivedDataCheckReport>.Failed(result.Failure!);
    }

    /// <summary>Rebuilds selected derived data transactionally from authoritative pages.</summary>
    /// <param name="selection">The explicit candidates to rebuild.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The commit and post-validation report or a classified integration failure.</returns>
    public async Task<NotebookOperationResult<DerivedDataRebuildReport>> RebuildAsync(
        IReadOnlyCollection<DerivedDataKind> selection,
        CancellationToken cancellationToken = default)
    {
        _applicationSession.Lifetime.ThrowIfDisposed();
        if (!TryMapSelection(selection, out var internalSelection, out var expanded))
        {
            return NotebookOperationResult<DerivedDataRebuildReport>.Failed(
                DerivedFailure(
                    "derived.unsupported_selection",
                    "The derived-data selection is empty or contains an unsupported value."));
        }

        var prerequisiteFailure = await ValidatePrerequisitesAsync(cancellationToken)
            .ConfigureAwait(false);
        if (prerequisiteFailure is not null)
            return NotebookOperationResult<DerivedDataRebuildReport>.Failed(prerequisiteFailure);

        var result = await NotebookOperationExecutor.ExecuteAsync(
                () => SchemaV2DerivedIndexMaintenance.RebuildAsync(
                    _applicationSession.Notebook.NotebookId.ToString(),
                    internalSelection,
                    cancellationToken),
                "derived.rebuild",
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? NotebookOperationResult<DerivedDataRebuildReport>.Succeeded(
                new DerivedDataRebuildReport(
                    result.Value!.WasCommitted,
                    Map(result.Value.Validation, expanded)))
            : NotebookOperationResult<DerivedDataRebuildReport>.Failed(result.Failure!);
    }

    private async Task<NotebookFailure?> ValidatePrerequisitesAsync(
        CancellationToken cancellationToken)
    {
        var result = await NotebookOperationExecutor.ExecuteAsync(
                () => SchemaV2DatabaseValidator.ValidateAuthoritativeAsync(
                    _applicationSession.Notebook.NotebookId.ToString(),
                    cancellationToken),
                "derived.prerequisite",
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
            return result.Failure;

        var validation = result.Value!;
        return validation.Identity.IsValid &&
               validation.Schema.IsValid &&
               validation.Integrity.IsValid &&
               validation.AuthoritativeValues.IsValid
            ? null
            : DerivedFailure(
                "derived.prerequisite_invalid",
                "The notebook schema or authoritative data cannot support derived-data maintenance.");
    }

    private static bool TryMapSelection(
        IReadOnlyCollection<DerivedDataKind>? selection,
        out SchemaV2DerivedIndexSelection internalSelection,
        out IReadOnlyList<DerivedDataKind> expanded)
    {
        internalSelection = SchemaV2DerivedIndexSelection.None;
        expanded = Array.Empty<DerivedDataKind>();
        if (selection is null || selection.Count == 0)
            return false;

        var unique = new SortedSet<DerivedDataKind>();
        foreach (var candidate in selection)
        {
            if (!Enum.IsDefined(candidate))
                return false;

            unique.Add(candidate);
            internalSelection |= candidate switch
            {
                DerivedDataKind.PageTags => SchemaV2DerivedIndexSelection.PageTags,
                DerivedDataKind.Fts5Unicode61 => SchemaV2DerivedIndexSelection.FtsUnicode61,
                DerivedDataKind.Fts5Trigram => SchemaV2DerivedIndexSelection.FtsTrigram,
                _ => throw new ArgumentOutOfRangeException(nameof(selection))
            };
        }

        expanded = unique.ToArray();
        return true;
    }

    private static DerivedDataCheckReport Map(
        SchemaV2DerivedDataValidation validation,
        IReadOnlyList<DerivedDataKind> selected)
    {
        return new DerivedDataCheckReport(
            selected,
            validation.Issues.Select(issue => new DerivedDataIssue(
                issue.Index switch
                {
                    SchemaV2DerivedIndexKind.PageTags => DerivedDataKind.PageTags,
                    SchemaV2DerivedIndexKind.FtsUnicode61 => DerivedDataKind.Fts5Unicode61,
                    SchemaV2DerivedIndexKind.FtsTrigram => DerivedDataKind.Fts5Trigram,
                    _ => throw new ArgumentOutOfRangeException(nameof(validation))
                },
                issue.Kind switch
                {
                    SchemaV2DerivedDataIssueKind.MissingRows => DerivedDataIssueKind.MissingRows,
                    SchemaV2DerivedDataIssueKind.UnexpectedRows => DerivedDataIssueKind.UnexpectedRows,
                    SchemaV2DerivedDataIssueKind.ContentMismatch => DerivedDataIssueKind.ContentMismatch,
                    _ => throw new ArgumentOutOfRangeException(nameof(validation))
                },
                issue.RowCount,
                issue.Message)).ToArray());
    }

    private static NotebookFailure DerivedFailure(string code, string message) =>
        new(NotebookFailureCategory.DerivedData, code, message);
}
