using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Archive;

namespace MemoriaNote.Cli
{
    internal sealed class NotebookRestoreCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ArchiveV1RestoreService _restoreService;
        readonly IBinaryStandardInput _binaryStandardInput;
        readonly ICommandOutput _output;

        internal NotebookRestoreCommandHandler(
            CliCommandExecutor executor,
            ArchiveV1RestoreService restoreService,
            IBinaryStandardInput binaryStandardInput,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _restoreService = restoreService ??
                throw new ArgumentNullException(nameof(restoreService));
            _binaryStandardInput = binaryStandardInput ??
                throw new ArgumentNullException(nameof(binaryStandardInput));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string target,
            string archive,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var workspacePath = WorkspacePathResolver.Resolve(workspaceOption);
                var targetFileName = NotebookInputParser.Parse(target);
                var destinationPath = Path.Combine(workspacePath, targetFileName.Value);
                if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Conflict,
                        $"The notebook destination already exists: {destinationPath}");
                }

                var readStandardInput = archive == null || archive == "-";
                if (readStandardInput && _binaryStandardInput.IsTerminal)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Refusing to read a binary archive from the terminal. " +
                        "Specify an archive file or redirect standard input.");
                }

                string archivePath = null;
                if (!readStandardInput)
                {
                    if (string.IsNullOrWhiteSpace(archive))
                    {
                        return CliCommandResult.Failure(
                            CliErrorKind.Validation,
                            "The archive path cannot be empty.");
                    }

                    try
                    {
                        archivePath = Path.GetFullPath(archive);
                    }
                    catch (ArgumentException exception)
                    {
                        return CliCommandResult.Failure(
                            CliErrorKind.Validation,
                            exception.Message,
                            exception);
                    }
                }

                var result = readStandardInput
                    ? await _restoreService.RestoreAsync(
                            new ArchiveV1StreamRestoreRequest(
                                _binaryStandardInput.Stream,
                                destinationPath,
                                dryRun),
                            token)
                        .ConfigureAwait(false)
                    : await _restoreService.RestoreAsync(
                            new ArchiveV1FileRestoreRequest(
                                archivePath,
                                destinationPath,
                                dryRun),
                            token)
                        .ConfigureAwait(false);

                if (!result.IsSuccess)
                    return ArchiveV1CommandResultMapper.ToCliResult(result.Error);

                _output.WriteLine(
                    $"Restore {(dryRun ? "validated" : "completed")}: " +
                    $"destination={Quote(destinationPath)}, " +
                    $"metadata={result.MetadataCount}, pages={result.PageCount}");
                return CliCommandResult.Success();
            }, cancellationToken);
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
