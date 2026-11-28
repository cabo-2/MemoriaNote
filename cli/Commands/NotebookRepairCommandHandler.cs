using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.NotebookMaintenance;

namespace MemoriaNote.Cli;

internal sealed class NotebookRepairCommandHandler
{
    private readonly CliCommandExecutor _executor;
    private readonly INotebookRepairService _repairService;
    private readonly ICommandOutput _output;

    internal NotebookRepairCommandHandler(
        CliCommandExecutor executor,
        INotebookRepairService repairService,
        ICommandOutput output)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _repairService = repairService ?? throw new ArgumentNullException(nameof(repairService));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    internal Task<int> ExecuteAsync(
        string source,
        string output,
        CancellationToken cancellationToken)
    {
        return _executor.ExecuteAsync(async token =>
        {
            var result = await _repairService.RepairAsync(
                    new NotebookRepairRequest(source, output),
                    token)
                .ConfigureAwait(false);
            if (!result.IsCompleted)
                return MapFailure(result);

            WriteReport(result.Report!);
            if (result.IsSuccess)
                return CliCommandResult.Success();

            _output.WriteLine(
                "Repair was refused because authoritative data could not be copied " +
                "completely and safely. Restore from a CM2 archive, backup, or another " +
                "known-good notebook.");
            return CliCommandResult.Failure(
                CliErrorKind.Validation,
                "Notebook repair was refused.",
                alreadyReported: true);
        }, cancellationToken);
    }

    private void WriteReport(NotebookRepairReport report)
    {
        _output.WriteLine(report.OutputPublished
            ? "Notebook repair: completed"
            : "Notebook repair: refused");
        _output.WriteLine($"Source: {CliMessage.Quote(report.SourcePath)}");
        _output.WriteLine($"Output: {CliMessage.Quote(report.OutputPath)}");
        _output.WriteLine(
            "Format: schema-v2 " +
            $"(version {report.FormatVersion.ToString(CultureInfo.InvariantCulture)})");
        _output.WriteLine(
            "Preflight: " +
            (report.PreflightStatus == NotebookRepairPreflightStatus.Repairable
                ? "repairable"
                : "refused"));

        foreach (var issue in report.Issues)
        {
            _output.WriteLine(
                $"  REFUSAL {issue.Code} " +
                $"location={CliMessage.Quote(issue.Location)} " +
                $"findings={issue.OccurrenceCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (report.MetadataCount.HasValue)
        {
            _output.WriteLine(
                "Copied: " +
                $"metadata={report.MetadataCount.Value.ToString(CultureInfo.InvariantCulture)}, " +
                $"pages={report.PageCount!.Value.ToString(CultureInfo.InvariantCulture)}");
            _output.WriteLine(
                "Authoritative data copied: " +
                string.Join(", ", report.CopiedAuthoritativeData));
            _output.WriteLine(
                "Derived data rebuilt: " +
                string.Join(", ", report.RebuiltDerivedData));
            _output.WriteLine(
                $"Logical fingerprint: {report.LogicalFingerprint}");
        }

        _output.WriteLine(
            "Output validation: " +
            (report.OutputValidationPassed ? "passed" : "not performed"));
        _output.WriteLine("Source modified: no");
        _output.WriteLine(
            "Output published: " + (report.OutputPublished ? "yes" : "no"));
    }

    private static CliCommandResult MapFailure(NotebookRepairResult result)
    {
        return result.Error switch
        {
            NotebookRepairErrorCode.SourceNotFound => Failure(
                CliErrorKind.NotFound,
                "The notebook repair source does not exist."),
            NotebookRepairErrorCode.InvalidTarget => Failure(
                CliErrorKind.Validation,
                "The notebook repair source or output path is invalid."),
            NotebookRepairErrorCode.LegacyFormat => Failure(
                CliErrorKind.Validation,
                "The notebook uses schema v1. Run 'mn notebooks migrate <source> " +
                "--output <notebook>' to create a schema v2 notebook."),
            NotebookRepairErrorCode.UnsupportedFormat => Failure(
                CliErrorKind.Validation,
                "The notebook uses a newer unsupported format" +
                (string.IsNullOrWhiteSpace(result.DetectedVersion)
                    ? string.Empty
                    : " (version " + result.DetectedVersion + ")") +
                ". Use a compatible newer Memoria Note release."),
            NotebookRepairErrorCode.UnreadableSource => Failure(
                CliErrorKind.Validation,
                "The source cannot be opened as a safely readable schema v2 notebook. " +
                "Restore it from a backup, CM2 archive, or another known-good copy."),
            NotebookRepairErrorCode.SourceBusy => Failure(
                CliErrorKind.Conflict,
                "The notebook is busy and no consistent repair snapshot was available. " +
                "Close the writer and retry the repair."),
            NotebookRepairErrorCode.OutputConflict => Failure(
                CliErrorKind.Conflict,
                "The repair output already exists or conflicts with the source. " +
                "Specify a different new --output path."),
            NotebookRepairErrorCode.StorageFailure => Failure(
                CliErrorKind.Storage,
                "Notebook repair failed because of an access, storage, or output " +
                "verification error. No partial output was published."),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    private static CliCommandResult Failure(CliErrorKind kind, string message) =>
        CliCommandResult.Failure(kind, message);
}
