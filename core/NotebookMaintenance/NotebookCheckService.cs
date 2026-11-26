#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.NotebookMaintenance;

/// <summary>Checks current schema v2 notebooks through the release validation contract.</summary>
public sealed class NotebookCheckService : INotebookCheckService
{
    /// <inheritdoc />
    public async Task<NotebookCheckResult> CheckAsync(
        NotebookCheckRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string sourcePath;
        try
        {
            sourcePath = Path.GetFullPath(request.SourcePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return NotebookCheckResult.Failed(NotebookCheckErrorCode.InvalidTarget);
        }

        if (Directory.Exists(sourcePath))
            return NotebookCheckResult.Failed(NotebookCheckErrorCode.InvalidTarget);
        if (!File.Exists(sourcePath))
            return NotebookCheckResult.Failed(NotebookCheckErrorCode.SourceNotFound);

        try
        {
            var recognition = await SqliteNotebookFormatRecognizer
                .RecognizeAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);
            var recognitionFailure = MapRecognitionFailure(recognition);
            if (recognitionFailure is not null)
                return recognitionFailure;

            var validation = await SchemaV2DatabaseValidator
                .ValidateAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);
            return NotebookCheckResult.Completed(MapReport(sourcePath, validation));
        }
        catch (NotebookRecognitionConcurrentChangeException)
        {
            return NotebookCheckResult.Failed(NotebookCheckErrorCode.SourceBusy);
        }
        catch (FileNotFoundException)
        {
            return NotebookCheckResult.Failed(NotebookCheckErrorCode.SourceNotFound);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            return NotebookCheckResult.Failed(NotebookCheckErrorCode.SourceBusy);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 3 or 8 or 10 or 13 or 14 or 23)
        {
            return NotebookCheckResult.Failed(NotebookCheckErrorCode.StorageFailure);
        }
        catch (SqliteException)
        {
            return NotebookCheckResult.Failed(NotebookCheckErrorCode.UnreadableSource);
        }
        catch (InvalidDataException)
        {
            return NotebookCheckResult.Failed(NotebookCheckErrorCode.UnreadableSource);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotebookCheckResult.Failed(NotebookCheckErrorCode.StorageFailure);
        }
    }

    private static NotebookCheckResult? MapRecognitionFailure(
        NotebookFormatRecognition recognition)
    {
        return recognition.Classification switch
        {
            NotebookFormatClassification.Current => null,
            NotebookFormatClassification.Missing => NotebookCheckResult.Failed(
                NotebookCheckErrorCode.SourceNotFound),
            NotebookFormatClassification.Legacy => NotebookCheckResult.Failed(
                NotebookCheckErrorCode.LegacyFormat,
                recognition.DetectedVersion),
            NotebookFormatClassification.Unsupported => NotebookCheckResult.Failed(
                NotebookCheckErrorCode.UnsupportedFormat,
                recognition.DetectedVersion),
            NotebookFormatClassification.Corrupt => NotebookCheckResult.Failed(
                NotebookCheckErrorCode.UnreadableSource),
            _ => throw new ArgumentOutOfRangeException(nameof(recognition))
        };
    }

    private static NotebookCheckReport MapReport(
        string sourcePath,
        SchemaV2DatabaseValidation validation)
    {
        var schemaIssues = validation.Schema.Issues
            .Where(issue => !IsTriggerIssue(issue))
            .Select(MapSchemaIssue)
            .ToArray();
        var triggerSourceIssues = validation.Schema.Issues
            .Where(IsTriggerIssue)
            .ToArray();
        var hasSpecificTriggerIssue = triggerSourceIssues.Any(issue =>
            !issue.StartsWith("trigger schema objects differ", StringComparison.Ordinal));
        var triggerIssues = triggerSourceIssues
            .Where(issue =>
                !hasSpecificTriggerIssue ||
                !issue.StartsWith("trigger schema objects differ", StringComparison.Ordinal))
            .Select(MapTriggerIssue)
            .ToArray();

        return new NotebookCheckReport(
            sourcePath,
            SchemaV2Format.UserVersion,
            [
                Layer(
                    NotebookCheckLayerKind.Identity,
                    validation.Identity.WasEvaluated,
                    validation.Identity.Issues.Select(MapIdentityIssue)),
                Layer(
                    NotebookCheckLayerKind.Schema,
                    validation.Schema.WasEvaluated,
                    schemaIssues),
                Layer(
                    NotebookCheckLayerKind.Triggers,
                    validation.Schema.WasEvaluated,
                    triggerIssues),
                Layer(
                    NotebookCheckLayerKind.Integrity,
                    validation.Integrity.WasEvaluated,
                    validation.Integrity.Issues.Select(MapIntegrityIssue)),
                Layer(
                    NotebookCheckLayerKind.AuthoritativeValues,
                    validation.AuthoritativeValues.WasEvaluated,
                    validation.AuthoritativeValues.Issues.Select(MapAuthoritativeIssue)),
                Layer(
                    NotebookCheckLayerKind.DerivedData,
                    validation.DerivedData.WasEvaluated,
                    validation.DerivedData.Issues.Select(MapDerivedIssue))
            ]);
    }

    private static NotebookCheckLayerReport Layer(
        NotebookCheckLayerKind kind,
        bool wasEvaluated,
        IEnumerable<NotebookCheckIssue> issues)
    {
        var materialized = issues.ToArray();
        var status = !wasEvaluated
            ? NotebookCheckLayerStatus.NotEvaluated
            : materialized.Length == 0
                ? NotebookCheckLayerStatus.Passed
                : NotebookCheckLayerStatus.Failed;
        return new NotebookCheckLayerReport(kind, status, materialized);
    }

    private static bool IsTriggerIssue(string issue) =>
        issue.StartsWith("trigger schema objects differ", StringComparison.Ordinal) ||
        issue.StartsWith("Required SQL object 'trigger ", StringComparison.Ordinal) ||
        issue.StartsWith("SQL object 'Pages_", StringComparison.Ordinal);

    private static NotebookCheckIssue MapIdentityIssue(string issue)
    {
        var applicationId = issue.StartsWith("application_id", StringComparison.Ordinal);
        return new NotebookCheckIssue(
            applicationId
                ? NotebookCheckIssueCode.IdentityApplicationIdMismatch
                : NotebookCheckIssueCode.IdentityUserVersionMismatch,
            applicationId ? "pragma.application_id" : "pragma.user_version",
            applicationId
                ? SchemaV2Format.ApplicationId.ToString(CultureInfo.InvariantCulture)
                : SchemaV2Format.UserVersion.ToString(CultureInfo.InvariantCulture),
            "different value");
    }

    private static NotebookCheckIssue MapSchemaIssue(string issue) =>
        new(
            NotebookCheckIssueCode.SchemaObjectMismatch,
            SchemaLocation(issue),
            "release schema v2 contract",
            "different schema definition");

    private static NotebookCheckIssue MapTriggerIssue(string issue)
    {
        var trigger = QuotedValue(issue) ?? "release triggers";
        return new NotebookCheckIssue(
            issue.Contains("is missing", StringComparison.Ordinal)
                ? NotebookCheckIssueCode.TriggerMissing
                : NotebookCheckIssueCode.TriggerMismatch,
            "trigger." + trigger.Replace("trigger ", string.Empty, StringComparison.Ordinal),
            "release trigger contract",
            issue.Contains("is missing", StringComparison.Ordinal)
                ? "missing"
                : "different trigger definition");
    }

    private static NotebookCheckIssue MapIntegrityIssue(string issue)
    {
        if (issue.StartsWith("Foreign key violation", StringComparison.Ordinal))
        {
            return new NotebookCheckIssue(
                NotebookCheckIssueCode.ForeignKeyViolation,
                "database.foreign-keys",
                "no foreign-key violations",
                "violation reported");
        }

        return new NotebookCheckIssue(
            NotebookCheckIssueCode.SqliteIntegrityError,
            "database.integrity",
            "ok",
            SafeIntegritySummary(issue));
    }

    private static NotebookCheckIssue MapAuthoritativeIssue(string issue)
    {
        if (issue.StartsWith("Metadata", StringComparison.Ordinal) ||
            issue.StartsWith("Required metadata", StringComparison.Ordinal) ||
            issue.StartsWith("Retired metadata", StringComparison.Ordinal) ||
            issue.StartsWith("Unknown metadata", StringComparison.Ordinal))
        {
            return new NotebookCheckIssue(
                MetadataIssueCode(issue),
                "Metadata" + QuotedLocationSuffix(issue),
                "release metadata value contract",
                "invalid or missing value");
        }

        if (issue.StartsWith("Duplicate PageId", StringComparison.Ordinal))
        {
            return new NotebookCheckIssue(
                NotebookCheckIssueCode.PageIdDuplicate,
                "Pages.PageId",
                "unique Page IDs",
                "duplicate value");
        }

        if (issue.StartsWith("Duplicate page Name", StringComparison.Ordinal))
        {
            return new NotebookCheckIssue(
                NotebookCheckIssueCode.PageNameDuplicate,
                "Pages.Name",
                "unique page names",
                "duplicate value");
        }

        return new NotebookCheckIssue(
            PageIssueCode(issue),
            PageLocation(issue),
            "release page value contract",
            PageActualSummary(issue));
    }

    private static NotebookCheckIssue MapDerivedIssue(SchemaV2DerivedDataIssue issue) =>
        new(
            issue.Kind == SchemaV2DerivedDataIssueKind.MissingRows
                ? NotebookCheckIssueCode.DerivedPageTagsMissing
                : NotebookCheckIssueCode.DerivedPageTagsUnexpected,
            "PageTags",
            "content derived from Pages.Tags",
            issue.RowCount.HasValue
                ? issue.RowCount.Value.ToString(CultureInfo.InvariantCulture) + " row(s)"
                : "different content");

    private static string SchemaLocation(string issue)
    {
        var marker = " differ";
        var end = issue.IndexOf(marker, StringComparison.Ordinal);
        return end > 0
            ? "schema." + issue[..end].Replace(' ', '-')
            : "schema";
    }

    private static string SafeIntegritySummary(string issue)
    {
        const string prefix = "SQLite integrity_check: ";
        var summary = issue.StartsWith(prefix, StringComparison.Ordinal)
            ? issue[prefix.Length..]
            : "integrity problem reported";
        return summary.Length <= 200 ? summary : summary[..200];
    }

    private static string QuotedLocationSuffix(string issue)
    {
        var value = QuotedValue(issue);
        return value is null ? string.Empty : "[key=" + SafeToken(value) + "]";
    }

    private static string PageLocation(string issue)
    {
        const string prefix = "Pages row '";
        if (!issue.StartsWith(prefix, StringComparison.Ordinal))
            return "Pages";
        var end = issue.IndexOf('\'', prefix.Length);
        return end < 0
            ? "Pages"
            : "Pages[InternalId=" + SafeToken(issue[prefix.Length..end]) + "]";
    }

    private static string PageActualSummary(string issue)
    {
        if (issue.Contains("PageId", StringComparison.Ordinal))
            return "invalid PageId";
        if (issue.Contains("Name", StringComparison.Ordinal))
            return "invalid Name";
        if (issue.Contains("CreateTime", StringComparison.Ordinal))
            return "invalid CreateTime";
        if (issue.Contains("UpdateTime", StringComparison.Ordinal))
            return "invalid UpdateTime";
        if (issue.Contains("Revision", StringComparison.Ordinal))
            return "invalid Revision";
        if (issue.Contains("Tags", StringComparison.Ordinal))
            return "invalid Tags";
        return "invalid page value";
    }

    private static string MetadataIssueCode(string issue)
    {
        if (issue.StartsWith("Required metadata", StringComparison.Ordinal) &&
            issue.Contains("is missing", StringComparison.Ordinal))
        {
            return NotebookCheckIssueCode.MetadataMissing;
        }
        if (issue.Contains("more than once", StringComparison.Ordinal))
            return NotebookCheckIssueCode.MetadataDuplicate;
        if (issue.StartsWith("Retired metadata", StringComparison.Ordinal))
            return NotebookCheckIssueCode.MetadataRetired;
        return NotebookCheckIssueCode.MetadataInvalid;
    }

    private static string PageIssueCode(string issue)
    {
        if (issue.Contains("PageId", StringComparison.Ordinal))
            return NotebookCheckIssueCode.PageIdInvalid;
        if (issue.Contains("Name", StringComparison.Ordinal))
            return NotebookCheckIssueCode.PageNameInvalid;
        if (issue.Contains("CreateTime", StringComparison.Ordinal) ||
            issue.Contains("UpdateTime", StringComparison.Ordinal))
        {
            return NotebookCheckIssueCode.PageTimestampInvalid;
        }
        if (issue.Contains("Revision", StringComparison.Ordinal))
            return NotebookCheckIssueCode.PageRevisionInvalid;
        if (issue.Contains("Tags", StringComparison.Ordinal))
            return NotebookCheckIssueCode.PageTagsInvalid;
        return NotebookCheckIssueCode.SchemaObjectMismatch;
    }

    private static string? QuotedValue(string value)
    {
        var start = value.IndexOf('\'');
        if (start < 0)
            return null;
        var end = value.IndexOf('\'', start + 1);
        return end < 0 ? null : value[(start + 1)..end];
    }

    private static string SafeToken(string value) => value
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
