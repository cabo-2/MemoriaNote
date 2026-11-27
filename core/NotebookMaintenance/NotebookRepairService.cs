#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.NotebookMaintenance;

/// <summary>Repairs current notebooks by losslessly rebuilding a validated new database.</summary>
public sealed class NotebookRepairService : INotebookRepairService
{
    /// <inheritdoc />
    public async Task<NotebookRepairResult> RepairAsync(
        NotebookRepairRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizePath(request.SourcePath, out var sourcePath) ||
            !TryNormalizePath(request.OutputPath, out var outputPath))
        {
            return NotebookRepairResult.Failed(NotebookRepairErrorCode.InvalidTarget);
        }

        if (Directory.Exists(sourcePath))
            return NotebookRepairResult.Failed(NotebookRepairErrorCode.InvalidTarget);
        if (!File.Exists(sourcePath))
            return NotebookRepairResult.Failed(NotebookRepairErrorCode.SourceNotFound);
        if (PathsEqual(sourcePath, outputPath) || DestinationExists(outputPath))
            return NotebookRepairResult.Failed(NotebookRepairErrorCode.OutputConflict);

        var publishing = false;
        try
        {
            var recognition = await SqliteNotebookFormatRecognizer
                .RecognizeAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);
            var recognitionFailure = MapRecognitionFailure(recognition);
            if (recognitionFailure is not null)
                return recognitionFailure;

            var preflight = await SchemaV2RepairSnapshot
                .OpenAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);
            if (!preflight.IsRepairable)
            {
                return NotebookRepairResult.Completed(
                    NotebookRepairReport.Refused(
                        sourcePath,
                        outputPath,
                        MapIssue(preflight)));
            }

            await using var snapshot = preflight.Snapshot!;
            var logicalFingerprint = FormatFingerprint(snapshot.Fingerprint);
            publishing = true;
            var published = await SchemaV2NotebookSink.PublishAsync(
                    outputPath,
                    snapshot.CreateTransfer(cancellationToken),
                    [sourcePath],
                    snapshot.Fingerprint,
                    cancellationToken)
                .ConfigureAwait(false);
            return NotebookRepairResult.Completed(
                NotebookRepairReport.Published(
                    sourcePath,
                    outputPath,
                    published.Fingerprint.MetadataCount,
                    published.Fingerprint.PageCount,
                    logicalFingerprint));
        }
        catch (NotebookRecognitionConcurrentChangeException)
        {
            return NotebookRepairResult.Failed(NotebookRepairErrorCode.SourceBusy);
        }
        catch (FileNotFoundException)
        {
            return NotebookRepairResult.Failed(NotebookRepairErrorCode.SourceNotFound);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            return NotebookRepairResult.Failed(NotebookRepairErrorCode.SourceBusy);
        }
        catch (SqliteException exception) when (
            exception.SqliteErrorCode is 3 or 8 or 10 or 13 or 14 or 23)
        {
            return NotebookRepairResult.Failed(NotebookRepairErrorCode.StorageFailure);
        }
        catch (SqliteException)
        {
            return NotebookRepairResult.Failed(
                publishing
                    ? NotebookRepairErrorCode.StorageFailure
                    : NotebookRepairErrorCode.UnreadableSource);
        }
        catch (InvalidDataException)
        {
            return NotebookRepairResult.Failed(
                publishing
                    ? NotebookRepairErrorCode.StorageFailure
                    : NotebookRepairErrorCode.UnreadableSource);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotebookRepairResult.Failed(
                DestinationExists(outputPath)
                    ? NotebookRepairErrorCode.OutputConflict
                    : NotebookRepairErrorCode.StorageFailure);
        }
    }

    private static NotebookRepairResult? MapRecognitionFailure(
        NotebookFormatRecognition recognition) => recognition.Classification switch
        {
            NotebookFormatClassification.Current => null,
            NotebookFormatClassification.Missing => NotebookRepairResult.Failed(
                NotebookRepairErrorCode.SourceNotFound),
            NotebookFormatClassification.Legacy => NotebookRepairResult.Failed(
                NotebookRepairErrorCode.LegacyFormat,
                recognition.DetectedVersion),
            NotebookFormatClassification.Unsupported => NotebookRepairResult.Failed(
                NotebookRepairErrorCode.UnsupportedFormat,
                recognition.DetectedVersion),
            NotebookFormatClassification.Corrupt => NotebookRepairResult.Failed(
                NotebookRepairErrorCode.UnreadableSource),
            _ => throw new ArgumentOutOfRangeException(nameof(recognition))
        };

    private static NotebookRepairIssue MapIssue(
        SchemaV2RepairSnapshotResult preflight) => preflight.IssueKind switch
        {
            SchemaV2RepairabilityIssueKind.AuthoritativeSchema => new NotebookRepairIssue(
                NotebookRepairIssueCode.AuthoritativeSchemaMismatch,
                "database.authoritative-schema",
                preflight.IssueCount),
            SchemaV2RepairabilityIssueKind.AuthoritativeIntegrity => new NotebookRepairIssue(
                NotebookRepairIssueCode.AuthoritativeIntegrityFailure,
                "database.authoritative-integrity",
                preflight.IssueCount),
            SchemaV2RepairabilityIssueKind.AuthoritativeValues => new NotebookRepairIssue(
                NotebookRepairIssueCode.AuthoritativeValueInvalid,
                "database.authoritative-values",
                preflight.IssueCount),
            _ => throw new ArgumentOutOfRangeException(nameof(preflight))
        };

    private static string FormatFingerprint(NotebookLogicalFingerprint fingerprint) =>
        $"{Convert.ToHexString(fingerprint.MetadataDigest).ToLowerInvariant()}:" +
        Convert.ToHexString(fingerprint.PageDigest).ToLowerInvariant();

    private static bool TryNormalizePath(string path, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private static bool DestinationExists(string path) =>
        SqliteDatabaseFiles.AnyExists(path) ||
        Directory.Exists(path) ||
        SqliteDatabaseFiles.Enumerate(path).Any(candidate =>
            new FileInfo(candidate).LinkTarget is not null);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
