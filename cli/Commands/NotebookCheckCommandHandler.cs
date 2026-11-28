using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.NotebookMaintenance;

namespace MemoriaNote.Cli;

internal sealed class NotebookCheckCommandHandler
{
    private readonly CliCommandExecutor _executor;
    private readonly INotebookCheckService _checkService;
    private readonly ICommandOutput _output;

    internal NotebookCheckCommandHandler(
        CliCommandExecutor executor,
        INotebookCheckService checkService,
        ICommandOutput output)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _checkService = checkService ?? throw new ArgumentNullException(nameof(checkService));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    internal Task<int> ExecuteAsync(
        string source,
        CancellationToken cancellationToken)
    {
        return _executor.ExecuteAsync(async token =>
        {
            var result = await _checkService.CheckAsync(
                    new NotebookCheckRequest(source),
                    token)
                .ConfigureAwait(false);
            if (!result.IsCompleted)
                return MapFailure(result);

            WriteReport(result.Report!);
            if (result.Report!.IsValid)
                return CliCommandResult.Success();

            _output.WriteLine(
                "Repair may be possible. Run 'mn notebooks repair <source> " +
                "--output <new-notebook>'. Repair performs a separate preflight " +
                "and may refuse this source.");
            return CliCommandResult.Failure(
                CliErrorKind.Validation,
                "Notebook check found one or more issues.",
                alreadyReported: true);
        }, cancellationToken);
    }

    private void WriteReport(NotebookCheckReport report)
    {
        _output.WriteLine(report.IsValid
            ? "Notebook check: valid"
            : "Notebook check: issues found");
        _output.WriteLine($"Source: {CliMessage.Quote(report.SourcePath)}");
        _output.WriteLine(
            "Format: schema-v2 " +
            $"(version {report.FormatVersion.ToString(CultureInfo.InvariantCulture)})");
        foreach (var layer in report.Layers)
        {
            _output.WriteLine(
                $"{FormatLayer(layer.Layer)}: {FormatStatus(layer.Status)}");
            foreach (var issue in layer.Issues)
            {
                var fields = " location=" + CliMessage.Quote(issue.Location);
                if (issue.Expected is not null)
                    fields += " expected=" + CliMessage.Quote(issue.Expected);
                if (issue.Actual is not null)
                    fields += " actual=" + CliMessage.Quote(issue.Actual);
                _output.WriteLine($"  ISSUE {issue.Code}{fields}");
            }
        }

        _output.WriteLine("Source modified: no");
        _output.WriteLine(
            "Issues: " + report.Issues.Count.ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatLayer(NotebookCheckLayerKind layer) => layer switch
    {
        NotebookCheckLayerKind.Identity => "identity",
        NotebookCheckLayerKind.Schema => "schema",
        NotebookCheckLayerKind.Triggers => "triggers",
        NotebookCheckLayerKind.Integrity => "integrity",
        NotebookCheckLayerKind.AuthoritativeValues => "authoritative-values",
        NotebookCheckLayerKind.DerivedData => "derived-data",
        _ => throw new ArgumentOutOfRangeException(nameof(layer))
    };

    private static string FormatStatus(NotebookCheckLayerStatus status) => status switch
    {
        NotebookCheckLayerStatus.Passed => "passed",
        NotebookCheckLayerStatus.Failed => "failed",
        NotebookCheckLayerStatus.NotEvaluated => "not evaluated",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static CliCommandResult MapFailure(NotebookCheckResult result)
    {
        return result.Error switch
        {
            NotebookCheckErrorCode.SourceNotFound => Failure(
                CliErrorKind.NotFound,
                "The notebook check source does not exist."),
            NotebookCheckErrorCode.InvalidTarget => Failure(
                CliErrorKind.Validation,
                "The notebook check source path is invalid or is not a file."),
            NotebookCheckErrorCode.LegacyFormat => Failure(
                CliErrorKind.Validation,
                "The notebook uses schema v1. Run 'mn notebooks migrate <source> " +
                "--output <notebook>' to create a schema v2 notebook."),
            NotebookCheckErrorCode.UnsupportedFormat => Failure(
                CliErrorKind.Validation,
                "The notebook uses a newer unsupported format" +
                (string.IsNullOrWhiteSpace(result.DetectedVersion)
                    ? string.Empty
                    : " (version " + result.DetectedVersion + ")") +
                ". Use a compatible newer Memoria Note release."),
            NotebookCheckErrorCode.UnreadableSource => Failure(
                CliErrorKind.Validation,
                "The source cannot be opened as a safely readable schema v2 notebook. " +
                "Restore it from a backup, CM2 archive, or another known-good copy."),
            NotebookCheckErrorCode.SourceBusy => Failure(
                CliErrorKind.Conflict,
                "The notebook is busy and no consistent read snapshot was available. " +
                "Close the writer and retry the check."),
            NotebookCheckErrorCode.StorageFailure => Failure(
                CliErrorKind.Storage,
                "The notebook check failed because of an access or storage error."),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    private static CliCommandResult Failure(CliErrorKind kind, string message) =>
        CliCommandResult.Failure(kind, message);
}
