using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.NotebookMigration;

namespace MemoriaNote.Cli;

internal sealed class NotebookMigrationCommandHandler
{
    private readonly CliCommandExecutor _executor;
    private readonly INotebookMigrationService _migrationService;
    private readonly ICommandOutput _output;

    internal NotebookMigrationCommandHandler(
        CliCommandExecutor executor,
        INotebookMigrationService migrationService,
        ICommandOutput output)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _migrationService = migrationService ??
            throw new ArgumentNullException(nameof(migrationService));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    internal Task<int> ExecuteAsync(
        string source,
        string destination,
        string resolution,
        bool dryRun,
        string reportLevel,
        CancellationToken cancellationToken)
    {
        return _executor.ExecuteAsync(async token =>
        {
            if (!TryReportLevel(reportLevel, out var level))
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "The report level must be error, warning, or info.");
            }

            string sourcePath;
            string destinationPath;
            string resolutionPath;
            try
            {
                sourcePath = Path.GetFullPath(source);
                destinationPath = Path.GetFullPath(destination);
                resolutionPath = string.IsNullOrWhiteSpace(resolution)
                    ? null
                    : Path.GetFullPath(resolution);
            }
            catch (Exception exception) when (CliMessage.IsInvalidPathException(exception))
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "A migration path is invalid.",
                    exception);
            }

            var result = await _migrationService.MigrateAsync(
                    new NotebookMigrationRequest(
                        sourcePath,
                        destinationPath,
                        resolutionPath,
                        dryRun),
                    token)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
                return MapFailure(result.Error.Value);

            WriteReport(result.Report, level);
            return result.Report.HasPageErrors
                ? CliCommandResult.Failure(
                    CliErrorKind.Partial,
                    "Migration completed with page errors.",
                    alreadyReported: true)
                : CliCommandResult.Success();
        }, cancellationToken);
    }

    private void WriteReport(
        NotebookMigrationReport report,
        NotebookMigrationIssueSeverity level)
    {
        foreach (var issue in report.Issues.Where(issue => Included(issue.Severity, level)))
        {
            var detail = string.Empty;
            if (issue.PageId is not null)
                detail += " page-id=" + CliMessage.Quote(issue.PageId);
            if (issue.SourceRowId.HasValue)
                detail += " source-rowid=" + issue.SourceRowId.Value.ToString(CultureInfo.InvariantCulture);
            if (issue.SourceIndex.HasValue)
                detail += " source-index=" + issue.SourceIndex.Value.ToString(CultureInfo.InvariantCulture);
            if (issue.Field is not null)
                detail += " field=" + CliMessage.Quote(issue.Field);
            if (issue.FinalName is not null)
                detail += " final-name=" + CliMessage.Quote(issue.FinalName);
            if (issue.Count.HasValue)
                detail += " count=" + issue.Count.Value.ToString(CultureInfo.InvariantCulture);
            if (issue.Reason is not null)
                detail += " reason=" + CliMessage.Quote(issue.Reason);
            if (issue.Actual.HasValue)
                detail += " actual=" + issue.Actual.Value.ToString(CultureInfo.InvariantCulture);
            if (issue.Limit.HasValue)
                detail += " limit=" + issue.Limit.Value.ToString(CultureInfo.InvariantCulture);
            if (issue.MetadataKeys is not null)
                detail += " keys=" + JsonSerializer.Serialize(issue.MetadataKeys);
            _output.WriteLine($"{issue.Severity.ToString().ToUpperInvariant()} {issue.Code}{detail}");
        }

        var errors = report.Issues.Count(issue =>
            issue.Severity == NotebookMigrationIssueSeverity.Error);
        var warnings = report.Issues.Count(issue =>
            issue.Severity == NotebookMigrationIssueSeverity.Warning);
        var status = report.DryRun ? "validated" : "completed";
        if (report.HasPageErrors)
            status += " with page errors";
        _output.WriteLine(
            $"Migration {status}: " +
            $"source={CliMessage.Quote(report.SourcePath)}, " +
            $"destination={CliMessage.Quote(report.DestinationPath)}, " +
            $"source-pages={report.Counts.SourcePages.ToString(CultureInfo.InvariantCulture)}, " +
            $"migrated={report.Counts.MigratedPages.ToString(CultureInfo.InvariantCulture)}, " +
            $"skipped={report.Counts.SkippedPages.ToString(CultureInfo.InvariantCulture)}, " +
            $"renamed={report.Counts.RenamedPages.ToString(CultureInfo.InvariantCulture)}, " +
            $"errors={errors.ToString(CultureInfo.InvariantCulture)}, " +
            $"warnings={warnings.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static bool Included(
        NotebookMigrationIssueSeverity severity,
        NotebookMigrationIssueSeverity level) =>
        severity <= level;

    private static bool TryReportLevel(
        string value,
        out NotebookMigrationIssueSeverity level)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            level = NotebookMigrationIssueSeverity.Warning;
            return true;
        }

        if (string.Equals(value, "error", StringComparison.OrdinalIgnoreCase))
        {
            level = NotebookMigrationIssueSeverity.Error;
            return true;
        }

        if (string.Equals(value, "warning", StringComparison.OrdinalIgnoreCase))
        {
            level = NotebookMigrationIssueSeverity.Warning;
            return true;
        }

        if (string.Equals(value, "info", StringComparison.OrdinalIgnoreCase))
        {
            level = NotebookMigrationIssueSeverity.Info;
            return true;
        }

        level = default;
        return false;
    }

    private static CliCommandResult MapFailure(NotebookMigrationErrorCode error)
    {
        return error switch
        {
            NotebookMigrationErrorCode.InputNotFound => Failure(
                CliErrorKind.NotFound,
                "The migration source or resolution file does not exist."),
            NotebookMigrationErrorCode.UnsupportedSource => Failure(
                CliErrorKind.Validation,
                "The source is not a supported schema v1 notebook or CM1 archive."),
            NotebookMigrationErrorCode.InvalidSource => Failure(
                CliErrorKind.Validation,
                "The migration source cannot be read completely and safely."),
            NotebookMigrationErrorCode.InvalidResolution => Failure(
                CliErrorKind.Validation,
                "The migration resolution document is invalid or inapplicable."),
            NotebookMigrationErrorCode.DestinationConflict => Failure(
                CliErrorKind.Conflict,
                "The migration destination already exists or conflicts with an input."),
            NotebookMigrationErrorCode.SourceBusy => Failure(
                CliErrorKind.Conflict,
                "The migration source is busy and no consistent snapshot was available."),
            NotebookMigrationErrorCode.IoFailure => Failure(
                CliErrorKind.Storage,
                "The migration failed due to an I/O error."),
            NotebookMigrationErrorCode.IntegrityFailure => Failure(
                CliErrorKind.Storage,
                "The generated notebook failed integrity verification."),
            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };
    }

    private static CliCommandResult Failure(CliErrorKind kind, string message) =>
        CliCommandResult.Failure(kind, message);
}
