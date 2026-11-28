#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence.SchemaV2;

namespace MemoriaNote.NotebookMaintenance;

/// <summary>Rebuilds safely readable Schema V2 authoritative data into a new notebook.</summary>
public interface INotebookRepairService
{
    /// <summary>Runs repairability preflight and publishes a validated new notebook.</summary>
    Task<NotebookRepairResult> RepairAsync(
        NotebookRepairRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Requests repair of one current-format notebook into a new output.</summary>
public sealed class NotebookRepairRequest
{
    /// <summary>Initializes a notebook repair request.</summary>
    public NotebookRepairRequest(string sourcePath, string outputPath)
    {
        SourcePath = RequirePath(sourcePath, nameof(sourcePath));
        OutputPath = RequirePath(outputPath, nameof(outputPath));
    }

    /// <summary>Gets the source schema v2 notebook path.</summary>
    public string SourcePath { get; }

    /// <summary>Gets the new schema v2 output path.</summary>
    public string OutputPath { get; }

    private static string RequirePath(string value, string parameterName)
    {
        if (value is null)
            throw new ArgumentNullException(parameterName);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A notebook file path is required.", parameterName);
        return value;
    }
}

/// <summary>Defines stable reasons that a recognized notebook cannot be repaired safely.</summary>
public static class NotebookRepairIssueCode
{
    /// <summary>The authoritative table or column contract cannot be read exactly.</summary>
    public const string AuthoritativeSchemaMismatch =
        "authoritative-schema-mismatch";

    /// <summary>SQLite integrity of authoritative storage cannot be established.</summary>
    public const string AuthoritativeIntegrityFailure =
        "authoritative-integrity-failure";

    /// <summary>One or more authoritative values violate the release contract.</summary>
    public const string AuthoritativeValueInvalid =
        "authoritative-value-invalid";
}

/// <summary>Describes one safe repair refusal without exposing source values.</summary>
public sealed class NotebookRepairIssue
{
    internal NotebookRepairIssue(
        string code,
        string location,
        int occurrenceCount)
    {
        Code = code;
        Location = location;
        OccurrenceCount = occurrenceCount;
    }

    /// <summary>Gets the stable refusal code.</summary>
    public string Code { get; }

    /// <summary>Gets the safe schema or value category.</summary>
    public string Location { get; }

    /// <summary>Gets the number of internal findings represented by this issue.</summary>
    public int OccurrenceCount { get; }
}

/// <summary>Identifies the result of repairability preflight.</summary>
public enum NotebookRepairPreflightStatus
{
    /// <summary>The authoritative source can be copied safely.</summary>
    Repairable,

    /// <summary>The authoritative source cannot be copied without unsafe recovery.</summary>
    Refused
}

/// <summary>Contains a completed repairability and publication report.</summary>
public sealed class NotebookRepairReport
{
    private NotebookRepairReport(
        string sourcePath,
        string outputPath,
        NotebookRepairPreflightStatus preflightStatus,
        IEnumerable<NotebookRepairIssue> issues,
        long? metadataCount,
        long? pageCount,
        string? logicalFingerprint,
        bool outputValidationPassed,
        bool outputPublished)
    {
        SourcePath = sourcePath;
        OutputPath = outputPath;
        PreflightStatus = preflightStatus;
        Issues = new ReadOnlyCollection<NotebookRepairIssue>([.. issues]);
        MetadataCount = metadataCount;
        PageCount = pageCount;
        LogicalFingerprint = logicalFingerprint;
        OutputValidationPassed = outputValidationPassed;
        OutputPublished = outputPublished;
        CopiedAuthoritativeData = outputPublished
            ? Array.AsReadOnly(["Metadata", "Pages"])
            : Array.Empty<string>();
        RebuiltDerivedData = outputPublished
            ? Array.AsReadOnly(["PageTags"])
            : Array.Empty<string>();
    }

    /// <summary>Gets the normalized source path.</summary>
    public string SourcePath { get; }

    /// <summary>Gets the normalized requested output path.</summary>
    public string OutputPath { get; }

    /// <summary>Gets the recognized notebook format version.</summary>
    public int FormatVersion => SchemaV2Format.UserVersion;

    /// <summary>Gets the repairability decision.</summary>
    public NotebookRepairPreflightStatus PreflightStatus { get; }

    /// <summary>Gets safe issues that explain a refusal.</summary>
    public IReadOnlyList<NotebookRepairIssue> Issues { get; }

    /// <summary>Gets the copied metadata count after successful preflight.</summary>
    public long? MetadataCount { get; }

    /// <summary>Gets the copied page count after successful preflight.</summary>
    public long? PageCount { get; }

    /// <summary>Gets the logical fingerprint after successful preflight.</summary>
    public string? LogicalFingerprint { get; }

    /// <summary>Gets whether the generated output passed complete validation.</summary>
    public bool OutputValidationPassed { get; }

    /// <summary>Gets whether the new output was atomically published.</summary>
    public bool OutputPublished { get; }

    /// <summary>Gets whether the source was modified by repair.</summary>
    public bool SourceWasModified => false;

    /// <summary>Gets authoritative categories copied without transformation.</summary>
    public IReadOnlyList<string> CopiedAuthoritativeData { get; }

    /// <summary>Gets derived categories rebuilt from authoritative data.</summary>
    public IReadOnlyList<string> RebuiltDerivedData { get; }

    internal static NotebookRepairReport Refused(
        string sourcePath,
        string outputPath,
        NotebookRepairIssue issue) =>
        new(
            sourcePath,
            outputPath,
            NotebookRepairPreflightStatus.Refused,
            [issue],
            null,
            null,
            null,
            outputValidationPassed: false,
            outputPublished: false);

    internal static NotebookRepairReport Published(
        string sourcePath,
        string outputPath,
        long metadataCount,
        long pageCount,
        string logicalFingerprint) =>
        new(
            sourcePath,
            outputPath,
            NotebookRepairPreflightStatus.Repairable,
            Array.Empty<NotebookRepairIssue>(),
            metadataCount,
            pageCount,
            logicalFingerprint,
            outputValidationPassed: true,
            outputPublished: true);
}

/// <summary>Identifies a fatal target or storage failure that prevented a repair report.</summary>
public enum NotebookRepairErrorCode
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

    /// <summary>The output already exists or conflicts with the source.</summary>
    OutputConflict,

    /// <summary>The repair failed because of an access, storage, or publication failure.</summary>
    StorageFailure
}

/// <summary>Contains a completed repair report or a fatal classified error.</summary>
public sealed class NotebookRepairResult
{
    private NotebookRepairResult(
        NotebookRepairReport? report,
        NotebookRepairErrorCode? error,
        string? detectedVersion)
    {
        Report = report;
        Error = error;
        DetectedVersion = detectedVersion;
    }

    /// <summary>Gets whether preflight completed and produced a report.</summary>
    public bool IsCompleted => Error is null;

    /// <summary>Gets whether a validated output was published.</summary>
    public bool IsSuccess => Report?.OutputPublished == true;

    /// <summary>Gets the completed repairability and publication report.</summary>
    public NotebookRepairReport? Report { get; }

    /// <summary>Gets the fatal error that prevented a report.</summary>
    public NotebookRepairErrorCode? Error { get; }

    /// <summary>Gets a positively detected unsupported format version.</summary>
    public string? DetectedVersion { get; }

    internal static NotebookRepairResult Completed(NotebookRepairReport report) =>
        new(report ?? throw new ArgumentNullException(nameof(report)), null, null);

    internal static NotebookRepairResult Failed(
        NotebookRepairErrorCode error,
        string? detectedVersion = null) =>
        new(null, error, detectedVersion);
}
