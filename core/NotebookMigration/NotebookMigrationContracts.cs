using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.NotebookMigration;

/// <summary>Migrates legacy notebook data to a validated new current-format notebook.</summary>
public interface INotebookMigrationService
{
    /// <summary>Validates and migrates one legacy source without changing it.</summary>
    Task<NotebookMigrationResult> MigrateAsync(
        NotebookMigrationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Requests migration of a schema v1 notebook or CM1 archive.</summary>
public sealed class NotebookMigrationRequest
{
    /// <summary>Initializes a migration request.</summary>
    public NotebookMigrationRequest(
        string sourcePath,
        string destinationPath,
        string resolutionPath = null,
        bool dryRun = false)
    {
        SourcePath = RequirePath(sourcePath, nameof(sourcePath));
        DestinationPath = RequirePath(destinationPath, nameof(destinationPath));
        ResolutionPath = resolutionPath;
        DryRun = dryRun;
    }

    /// <summary>Gets the schema v1 notebook or CM1 archive path.</summary>
    public string SourcePath { get; }

    /// <summary>Gets the new schema v2 notebook path.</summary>
    public string DestinationPath { get; }

    /// <summary>Gets the optional versioned resolution document path.</summary>
    public string ResolutionPath { get; }

    /// <summary>Gets whether validation must run without publishing the destination.</summary>
    public bool DryRun { get; }

    private static string RequirePath(string value, string parameterName)
    {
        if (value == null)
            throw new ArgumentNullException(parameterName);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A file path is required.", parameterName);
        return value;
    }
}

/// <summary>Identifies the accepted legacy source representation.</summary>
public enum NotebookMigrationSourceKind
{
    /// <summary>A live schema v1 SQLite notebook.</summary>
    SchemaV1Notebook,

    /// <summary>An archive v1 content-model-1 ZIP file.</summary>
    ContentModel1Archive
}

/// <summary>Identifies report entry importance independently from mapping classification.</summary>
public enum NotebookMigrationIssueSeverity
{
    /// <summary>The page could not be migrated.</summary>
    Error,

    /// <summary>The page migrated with a user-visible or lossy change.</summary>
    Warning,

    /// <summary>The entry records an expected conversion or aggregate fact.</summary>
    Info
}

/// <summary>Identifies how a legacy value was handled.</summary>
public enum NotebookMigrationChangeKind
{
    /// <summary>The value was preserved.</summary>
    Preserved,

    /// <summary>The value was rewritten to its canonical representation.</summary>
    Canonicalized,

    /// <summary>The value was mapped to a different representation.</summary>
    Transformed,

    /// <summary>The legacy field was intentionally omitted.</summary>
    Dropped,

    /// <summary>The complete legacy page was omitted.</summary>
    Excluded
}

/// <summary>Defines stable migration report issue codes.</summary>
public static class NotebookMigrationIssueCode
{
    /// <summary>A page identifier could not be interpreted safely.</summary>
    public const string InvalidPageId = "invalid-page-id";

    /// <summary>A legacy row contains a value with an unreadable SQLite representation.</summary>
    public const string InvalidPageValue = "invalid-page-value";

    /// <summary>A page name violates the schema v2 value contract.</summary>
    public const string InvalidPageName = "invalid-page-name";

    /// <summary>A duplicate page name was changed automatically.</summary>
    public const string DuplicateNameRenamed = "duplicate-name-renamed";

    /// <summary>An explicit page rename was applied.</summary>
    public const string PageRenamed = "page-renamed";

    /// <summary>Legacy tags could not be converted to valid schema v2 labels.</summary>
    public const string InvalidTags = "invalid-tags";

    /// <summary>An explicit complete tag replacement was applied.</summary>
    public const string TagsOverridden = "tags-overridden";

    /// <summary>A page timestamp could not be parsed.</summary>
    public const string InvalidTimestamp = "invalid-timestamp";

    /// <summary>A page update timestamp precedes its creation timestamp.</summary>
    public const string UpdateBeforeCreate = "update-before-create";

    /// <summary>An erased legacy page was omitted without an explicit exclusion.</summary>
    public const string ErasedPageSkipped = "erased-page-skipped";

    /// <summary>An erased legacy page was omitted by an explicit resolution.</summary>
    public const string ErasedPageExcluded = "erased-page-excluded";

    /// <summary>A null legacy body was converted to empty text.</summary>
    public const string NullTextCanonicalized = "null-text-canonicalized";

    /// <summary>Legacy page ordinal fields were dropped.</summary>
    public const string IndexesDropped = "indexes-dropped";

    /// <summary>Legacy content type fields were classified and dropped.</summary>
    public const string ContentTypesDropped = "content-types-dropped";

    /// <summary>Page identifiers were written in canonical lowercase form.</summary>
    public const string PageIdsCanonicalized = "page-ids-canonicalized";

    /// <summary>Timestamp fractions were padded to seven digits.</summary>
    public const string TimestampsCanonicalized = "timestamps-canonicalized";
}

/// <summary>Defines stable reasons that further classify migration issues.</summary>
public static class NotebookMigrationIssueReason
{
    /// <summary>The page name is empty or consists only of whitespace.</summary>
    public const string EmptyOrWhitespace = "empty-or-whitespace";

    /// <summary>The page name has leading or trailing whitespace.</summary>
    public const string OuterWhitespace = "outer-whitespace";

    /// <summary>The page name is not valid Unicode text.</summary>
    public const string InvalidUnicode = "invalid-unicode";

    /// <summary>The page name contains a Unicode control character.</summary>
    public const string ControlCharacter = "control-character";

    /// <summary>The page name exceeds the Unicode scalar limit.</summary>
    public const string ScalarLimitExceeded = "scalar-limit-exceeded";
}

/// <summary>Describes one safe migration report entry without page body content.</summary>
public sealed record NotebookMigrationIssue(
    string Code,
    NotebookMigrationIssueSeverity Severity,
    NotebookMigrationChangeKind ChangeKind,
    string PageId = null,
    long? SourceRowId = null,
    long? SourceIndex = null,
    string Field = null,
    string FinalName = null,
    long? Count = null,
    string Reason = null,
    long? Actual = null,
    long? Limit = null);

/// <summary>Counts classified legacy ContentType values.</summary>
public sealed record NotebookMigrationContentTypeCounts(
    long Null,
    long Empty,
    long Text,
    long Other)
{
    /// <summary>Gets the number of dropped ContentType values.</summary>
    public long Total => Null + Empty + Text + Other;
}

/// <summary>Summarizes records and conversions performed by migration.</summary>
public sealed record NotebookMigrationCounts(
    long SourceMetadata,
    long MigratedMetadata,
    long SourcePages,
    long MigratedPages,
    long SkippedPages,
    long RenamedPages,
    long TagOverrides,
    long NullTexts,
    long CanonicalizedPageIds,
    long CanonicalizedTimestamps,
    long DroppedIndexes,
    NotebookMigrationContentTypeCounts DroppedContentTypes);

/// <summary>Contains the complete operation report used by all CLI presentations.</summary>
public sealed class NotebookMigrationReport
{
    internal NotebookMigrationReport(
        NotebookMigrationSourceKind sourceKind,
        bool dryRun,
        string sourcePath,
        string destinationPath,
        NotebookMigrationCounts counts,
        IEnumerable<NotebookMigrationIssue> issues,
        string logicalFingerprint)
    {
        SourceKind = sourceKind;
        DryRun = dryRun;
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        Counts = counts;
        Issues = new ReadOnlyCollection<NotebookMigrationIssue>([.. issues]);
        LogicalFingerprint = logicalFingerprint;
    }

    /// <summary>Gets the detected source representation.</summary>
    public NotebookMigrationSourceKind SourceKind { get; }

    /// <summary>Gets whether this report describes a dry-run.</summary>
    public bool DryRun { get; }

    /// <summary>Gets the normalized source path.</summary>
    public string SourcePath { get; }

    /// <summary>Gets the normalized destination path.</summary>
    public string DestinationPath { get; }

    /// <summary>Gets migration counts.</summary>
    public NotebookMigrationCounts Counts { get; }

    /// <summary>Gets stable report entries.</summary>
    public IReadOnlyList<NotebookMigrationIssue> Issues { get; }

    /// <summary>Gets the logical destination fingerprint without private values.</summary>
    public string LogicalFingerprint { get; }

    /// <summary>Gets whether one or more source pages were not migrated.</summary>
    public bool HasPageErrors => Issues.Any(issue =>
        issue.Severity == NotebookMigrationIssueSeverity.Error);
}

/// <summary>Identifies a fatal migration failure that prevents publication.</summary>
public enum NotebookMigrationErrorCode
{
    /// <summary>The source or resolution input does not exist.</summary>
    InputNotFound,

    /// <summary>The input is not a supported schema v1 notebook or CM1 archive.</summary>
    UnsupportedSource,

    /// <summary>The legacy source cannot be read completely and safely.</summary>
    InvalidSource,

    /// <summary>The versioned resolution document is invalid.</summary>
    InvalidResolution,

    /// <summary>The destination or another input path conflicts with publication.</summary>
    DestinationConflict,

    /// <summary>The source could not provide a consistent snapshot.</summary>
    SourceBusy,

    /// <summary>A file operation failed.</summary>
    IoFailure,

    /// <summary>The generated schema v2 database failed verification.</summary>
    IntegrityFailure
}

/// <summary>Contains a migration report or a fatal classified error.</summary>
public sealed class NotebookMigrationResult
{
    private NotebookMigrationResult(
        NotebookMigrationReport report,
        NotebookMigrationErrorCode? error)
    {
        Report = report;
        Error = error;
    }

    /// <summary>Gets whether migration reached a verified dry-run or publication result.</summary>
    public bool IsSuccess => Error == null;

    /// <summary>Gets the report after a successful or partial migration.</summary>
    public NotebookMigrationReport Report { get; }

    /// <summary>Gets a fatal error code when no valid result was produced.</summary>
    public NotebookMigrationErrorCode? Error { get; }

    internal static NotebookMigrationResult Completed(NotebookMigrationReport report) =>
        new(report ?? throw new ArgumentNullException(nameof(report)), null);

    internal static NotebookMigrationResult Failed(NotebookMigrationErrorCode error) =>
        new(null, error);
}
