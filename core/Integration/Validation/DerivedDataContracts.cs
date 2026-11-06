#nullable enable

using System;
using System.Collections.Generic;

namespace MemoriaNote.Integration.Validation;

/// <summary>Identifies one revision-specific derived-data candidate.</summary>
public enum DerivedDataKind
{
    /// <summary>The tag index derived from authoritative page tags.</summary>
    PageTags,

    /// <summary>The comparison FTS5 index using the unicode61 tokenizer.</summary>
    Fts5Unicode61,

    /// <summary>The comparison FTS5 index using the trigram tokenizer.</summary>
    Fts5Trigram
}

/// <summary>Classifies one derived-data content inconsistency.</summary>
public enum DerivedDataIssueKind
{
    /// <summary>Expected derived rows are absent.</summary>
    MissingRows,

    /// <summary>Rows without corresponding authoritative values are present.</summary>
    UnexpectedRows,

    /// <summary>The derived content does not match authoritative page content.</summary>
    ContentMismatch
}

/// <summary>Describes one selected derived-data inconsistency.</summary>
public sealed class DerivedDataIssue
{
    internal DerivedDataIssue(
        DerivedDataKind derivedData,
        DerivedDataIssueKind kind,
        long? rowCount,
        string message)
    {
        DerivedData = derivedData;
        Kind = kind;
        RowCount = rowCount;
        Message = message;
    }

    /// <summary>Gets the derived-data candidate containing the issue.</summary>
    public DerivedDataKind DerivedData { get; }

    /// <summary>Gets the classified issue kind.</summary>
    public DerivedDataIssueKind Kind { get; }

    /// <summary>Gets the affected row count when the validator can determine it.</summary>
    public long? RowCount { get; }

    /// <summary>Gets a diagnostic message safe for a first-party validation report.</summary>
    public string Message { get; }
}

/// <summary>Reports the expanded selector set and its derived-data issues.</summary>
public sealed class DerivedDataCheckReport
{
    internal DerivedDataCheckReport(
        IReadOnlyList<DerivedDataKind> selected,
        IReadOnlyList<DerivedDataIssue> issues)
    {
        Selected = Array.AsReadOnly([.. selected]);
        Issues = Array.AsReadOnly([.. issues]);
    }

    /// <summary>Gets whether every selected derived-data candidate is valid.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>Gets the explicit selector set in stable enum order.</summary>
    public IReadOnlyList<DerivedDataKind> Selected { get; }

    /// <summary>Gets all independently classified issues.</summary>
    public IReadOnlyList<DerivedDataIssue> Issues { get; }
}

/// <summary>Reports whether a rebuild committed and its post-rebuild validation.</summary>
public sealed class DerivedDataRebuildReport
{
    internal DerivedDataRebuildReport(
        bool wasCommitted,
        DerivedDataCheckReport validation)
    {
        WasCommitted = wasCommitted;
        Validation = validation;
    }

    /// <summary>Gets whether the rebuilt derived data was committed.</summary>
    public bool WasCommitted { get; }

    /// <summary>Gets the post-rebuild validation report.</summary>
    public DerivedDataCheckReport Validation { get; }
}
