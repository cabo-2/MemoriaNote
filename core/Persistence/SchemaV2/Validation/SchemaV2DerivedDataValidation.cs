using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Identifies a fixed Schema V2 derived index.</summary>
internal enum SchemaV2DerivedIndexKind
{
    PageTags,
    FtsUnicode61,
    FtsTrigram
}

/// <summary>Identifies a kind of derived-index content inconsistency.</summary>
internal enum SchemaV2DerivedDataIssueKind
{
    MissingRows,
    UnexpectedRows,
    ContentMismatch
}

/// <summary>Describes one classified derived-index content inconsistency.</summary>
internal sealed record SchemaV2DerivedDataIssue(
    SchemaV2DerivedIndexKind Index,
    SchemaV2DerivedDataIssueKind Kind,
    long? RowCount,
    string Message);

/// <summary>Reports independently classified Schema V2 derived-data issues.</summary>
internal sealed record SchemaV2DerivedDataValidation(
    bool WasEvaluated,
    IReadOnlyList<SchemaV2DerivedDataIssue> Issues)
{
    internal bool IsValid => WasEvaluated && Issues.Count == 0;

    internal static SchemaV2DerivedDataValidation Evaluated(
        IEnumerable<SchemaV2DerivedDataIssue> issues) =>
        new(true, issues.ToArray());

    internal static SchemaV2DerivedDataValidation NotEvaluated { get; } =
        new(false, new SchemaV2DerivedDataIssue[0]);
}
