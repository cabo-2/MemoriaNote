using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Transfer;

namespace MemoriaNote.Cli
{
    internal sealed class NotebookExportCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly TextPageExporter _exporter;
        readonly ICommandOutput _output;

        internal NotebookExportCommandHandler(
            CliCommandExecutor executor,
            INotebookTargetSessionResolver targetResolver,
            TextPageExporter exporter,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _targetResolver = targetResolver ??
                throw new ArgumentNullException(nameof(targetResolver));
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookOption,
            string destinationDirectory,
            string nameConflictPolicy,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "The export directory cannot be empty.");
                }

                if (!TryParseNameConflictPolicy(
                    nameConflictPolicy,
                    out var parsedPolicy))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "The name conflict policy must be fail or id-suffix.");
                }

                string destinationPath;
                try
                {
                    destinationPath = Path.GetFullPath(destinationDirectory);
                }
                catch (ArgumentException exception)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        exception.Message,
                        exception);
                }

                if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Conflict,
                        $"The export destination already exists: {destinationPath}");
                }

                var session = await _targetResolver.ResolveAsync(
                        workspaceOption,
                        notebookOption,
                        token)
                    .ConfigureAwait(false);
                var sourcePath = session.Workspace.SelectedNotebook.DatabasePath;
                var result = await _exporter.ExportAsync(
                        new TextPageExportRequest(
                            NotebookId.FromDatabasePath(sourcePath),
                            destinationPath,
                            parsedPolicy),
                        token)
                    .ConfigureAwait(false);
                if (!result.IsSuccess)
                    return ToCliResult(result.ErrorCode.Value, destinationPath);

                _output.WriteLine(
                    $"Export completed: source={Quote(sourcePath)}, " +
                    $"destination={Quote(destinationPath)}, " +
                    $"exported={result.ExportedCount}");
                return CliCommandResult.Success();
            }, cancellationToken);
        }

        static bool TryParseNameConflictPolicy(
            string value,
            out TextPageExportNameConflictPolicy policy)
        {
            switch (value ?? "fail")
            {
                case "fail":
                    policy = TextPageExportNameConflictPolicy.Fail;
                    return true;
                case "id-suffix":
                    policy = TextPageExportNameConflictPolicy.IdSuffix;
                    return true;
                default:
                    policy = default;
                    return false;
            }
        }

        static CliCommandResult ToCliResult(
            TextPageExportErrorCode errorCode,
            string destinationPath)
        {
            return errorCode switch
            {
                TextPageExportErrorCode.DestinationConflict => Failure(
                    CliErrorKind.Conflict,
                    $"The export destination already exists: {destinationPath}"),
                TextPageExportErrorCode.NameConflict => Failure(
                    CliErrorKind.Conflict,
                    "The notebook contains page names that cannot be exported " +
                    "to unique, safe file paths."),
                TextPageExportErrorCode.IoFailure => Failure(
                    CliErrorKind.Storage,
                    "The export failed while writing the destination directory."),
                _ => throw new ArgumentOutOfRangeException(nameof(errorCode))
            };
        }

        static CliCommandResult Failure(CliErrorKind kind, string message)
        {
            return CliCommandResult.Failure(kind, message);
        }

        static string Quote(string value)
        {
            return "\"" + value
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
        }
    }
}
