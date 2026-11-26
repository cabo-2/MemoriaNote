#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.NotebookMaintenance;

/// <summary>Checks a current-format notebook without changing its source files.</summary>
public interface INotebookCheckService
{
    /// <summary>Runs every safe validation layer for one notebook source.</summary>
    Task<NotebookCheckResult> CheckAsync(
        NotebookCheckRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Requests a complete read-only check of one notebook file.</summary>
public sealed class NotebookCheckRequest
{
    /// <summary>Initializes a notebook check request.</summary>
    public NotebookCheckRequest(string sourcePath)
    {
        if (sourcePath is null)
            throw new ArgumentNullException(nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("A notebook source path is required.", nameof(sourcePath));

        SourcePath = sourcePath;
    }

    /// <summary>Gets the notebook file to check.</summary>
    public string SourcePath { get; }
}

/// <summary>Identifies one independently reported notebook check layer.</summary>
public enum NotebookCheckLayerKind
{
    /// <summary>The persisted Memoria Note format identity.</summary>
    Identity,

    /// <summary>The release table, column, constraint, and index contract.</summary>
    Schema,

    /// <summary>The release trigger contract.</summary>
    Triggers,

    /// <summary>SQLite and foreign-key integrity.</summary>
    Integrity,

    /// <summary>Metadata and page values owned by the application.</summary>
    AuthoritativeValues,

    /// <summary>Values rebuilt from authoritative data.</summary>
    DerivedData
}

/// <summary>Identifies whether one notebook check layer completed successfully.</summary>
public enum NotebookCheckLayerStatus
{
    /// <summary>The layer was evaluated and no issues were found.</summary>
    Passed,

    /// <summary>The layer was evaluated and one or more issues were found.</summary>
    Failed,

    /// <summary>An earlier unsafe layer prevented this layer from being evaluated.</summary>
    NotEvaluated
}

/// <summary>Defines stable notebook check issue codes.</summary>
public static class NotebookCheckIssueCode
{
    /// <summary>The SQLite application ID differs from the release identity.</summary>
    public const string IdentityApplicationIdMismatch =
        "identity-application-id-mismatch";

    /// <summary>The SQLite user version differs from the release identity.</summary>
    public const string IdentityUserVersionMismatch =
        "identity-user-version-mismatch";

    /// <summary>A non-trigger schema object differs from the release contract.</summary>
    public const string SchemaObjectMismatch = "schema-object-mismatch";

    /// <summary>A required non-trigger schema object is absent.</summary>
    public const string SchemaObjectMissing = "schema-object-missing";

    /// <summary>An unexpected non-trigger schema object is present.</summary>
    public const string SchemaObjectUnexpected = "schema-object-unexpected";

    /// <summary>A required trigger is absent.</summary>
    public const string TriggerMissing = "trigger-missing";

    /// <summary>A trigger differs from the release contract.</summary>
    public const string TriggerMismatch = "trigger-mismatch";

    /// <summary>SQLite reported a low-level integrity problem.</summary>
    public const string SqliteIntegrityError = "sqlite-integrity-error";

    /// <summary>SQLite reported a foreign-key violation.</summary>
    public const string ForeignKeyViolation = "foreign-key-violation";

    /// <summary>A required metadata key is absent.</summary>
    public const string MetadataMissing = "metadata-missing";

    /// <summary>A metadata key occurs more than once.</summary>
    public const string MetadataDuplicate = "metadata-duplicate";

    /// <summary>Stored metadata differs from the release value contract.</summary>
    public const string MetadataInvalid = "metadata-invalid";

    /// <summary>A retired metadata key is present.</summary>
    public const string MetadataRetired = "metadata-retired";

    /// <summary>A Page ID violates the release value contract.</summary>
    public const string PageIdInvalid = "page-id-invalid";

    /// <summary>Stable Page IDs are not unique.</summary>
    public const string PageIdDuplicate = "page-id-duplicate";

    /// <summary>Page names are not unique.</summary>
    public const string PageNameDuplicate = "page-name-duplicate";

    /// <summary>A page name violates the release value contract.</summary>
    public const string PageNameInvalid = "page-name-invalid";

    /// <summary>A page timestamp violates the release value contract.</summary>
    public const string PageTimestampInvalid = "page-timestamp-invalid";

    /// <summary>A page revision violates the release value contract.</summary>
    public const string PageRevisionInvalid = "page-revision-invalid";

    /// <summary>Authoritative page tags violate the release value contract.</summary>
    public const string PageTagsInvalid = "page-tags-invalid";

    /// <summary>PageTags is missing rows required by authoritative page tags.</summary>
    public const string DerivedPageTagsMissing = "derived-page-tags-missing";

    /// <summary>PageTags contains rows not present in authoritative page tags.</summary>
    public const string DerivedPageTagsUnexpected = "derived-page-tags-unexpected";
}

/// <summary>Describes one safe notebook check issue.</summary>
public sealed class NotebookCheckIssue
{
    internal NotebookCheckIssue(
        string code,
        string location,
        string? expected = null,
        string? actual = null)
    {
        Code = code;
        Location = location;
        Expected = expected;
        Actual = actual;
    }

    /// <summary>Gets the stable issue code.</summary>
    public string Code { get; }

    /// <summary>Gets the safe schema or record location.</summary>
    public string Location { get; }

    /// <summary>Gets a safe expected-value summary.</summary>
    public string? Expected { get; }

    /// <summary>Gets a safe actual-value summary.</summary>
    public string? Actual { get; }
}

/// <summary>Reports one independently evaluated notebook check layer.</summary>
public sealed class NotebookCheckLayerReport
{
    internal NotebookCheckLayerReport(
        NotebookCheckLayerKind layer,
        NotebookCheckLayerStatus status,
        IEnumerable<NotebookCheckIssue> issues)
    {
        Layer = layer;
        Status = status;
        Issues = new ReadOnlyCollection<NotebookCheckIssue>([.. issues]);
    }

    /// <summary>Gets the validation layer.</summary>
    public NotebookCheckLayerKind Layer { get; }

    /// <summary>Gets the layer result.</summary>
    public NotebookCheckLayerStatus Status { get; }

    /// <summary>Gets stable issues reported by this layer.</summary>
    public IReadOnlyList<NotebookCheckIssue> Issues { get; }
}

/// <summary>Contains the complete human-presentation-neutral notebook check report.</summary>
public sealed class NotebookCheckReport
{
    internal NotebookCheckReport(
        string sourcePath,
        int formatVersion,
        IEnumerable<NotebookCheckLayerReport> layers)
    {
        SourcePath = sourcePath;
        FormatVersion = formatVersion;
        Layers = new ReadOnlyCollection<NotebookCheckLayerReport>([.. layers]);
    }

    /// <summary>Gets the normalized checked source path.</summary>
    public string SourcePath { get; }

    /// <summary>Gets the recognized notebook format version.</summary>
    public int FormatVersion { get; }

    /// <summary>Gets each check layer in evaluation order.</summary>
    public IReadOnlyList<NotebookCheckLayerReport> Layers { get; }

    /// <summary>Gets every reported issue in layer order.</summary>
    public IReadOnlyList<NotebookCheckIssue> Issues =>
        Array.AsReadOnly(Layers.SelectMany(layer => layer.Issues).ToArray());

    /// <summary>Gets whether every layer passed.</summary>
    public bool IsValid => Layers.All(layer =>
        layer.Status == NotebookCheckLayerStatus.Passed);

    /// <summary>Gets whether the operation changed the notebook source.</summary>
    public bool SourceWasModified => false;
}

/// <summary>Identifies a fatal target or storage result that prevented a check report.</summary>
public enum NotebookCheckErrorCode
{
    /// <summary>The source path does not exist.</summary>
    SourceNotFound,

    /// <summary>The source path is invalid or does not identify a regular file.</summary>
    InvalidTarget,

    /// <summary>The source is a schema v1 notebook.</summary>
    LegacyFormat,

    /// <summary>The source uses a newer unsupported Memoria Note format.</summary>
    UnsupportedFormat,

    /// <summary>The source is not a safely readable current-format notebook.</summary>
    UnreadableSource,

    /// <summary>A consistent read snapshot was unavailable.</summary>
    SourceBusy,

    /// <summary>The source could not be read because of an access or storage failure.</summary>
    StorageFailure
}

/// <summary>Contains a complete check report or a fatal classified error.</summary>
public sealed class NotebookCheckResult
{
    private NotebookCheckResult(
        NotebookCheckReport? report,
        NotebookCheckErrorCode? error,
        string? detectedVersion)
    {
        Report = report;
        Error = error;
        DetectedVersion = detectedVersion;
    }

    /// <summary>Gets whether the check completed and produced a report.</summary>
    public bool IsCompleted => Error is null;

    /// <summary>Gets the completed report.</summary>
    public NotebookCheckReport? Report { get; }

    /// <summary>Gets the fatal error that prevented a report.</summary>
    public NotebookCheckErrorCode? Error { get; }

    /// <summary>Gets a positively detected unsupported format version.</summary>
    public string? DetectedVersion { get; }

    internal static NotebookCheckResult Completed(NotebookCheckReport report) =>
        new(report ?? throw new ArgumentNullException(nameof(report)), null, null);

    internal static NotebookCheckResult Failed(
        NotebookCheckErrorCode error,
        string? detectedVersion = null) =>
        new(null, error, detectedVersion);
}
