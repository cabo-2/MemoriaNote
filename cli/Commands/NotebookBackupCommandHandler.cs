using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Archive;

namespace MemoriaNote.Cli
{
    internal sealed class NotebookBackupCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly ArchiveV1BackupService _backupService;
        readonly IBinaryStandardOutput _binaryStandardOutput;
        readonly ICommandOutput _output;

        internal NotebookBackupCommandHandler(
            CliCommandExecutor executor,
            INotebookTargetSessionResolver targetResolver,
            ArchiveV1BackupService backupService,
            IBinaryStandardOutput binaryStandardOutput,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _targetResolver = targetResolver ??
                throw new ArgumentNullException(nameof(targetResolver));
            _backupService = backupService ??
                throw new ArgumentNullException(nameof(backupService));
            _binaryStandardOutput = binaryStandardOutput ??
                throw new ArgumentNullException(nameof(binaryStandardOutput));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookOption,
            string archive,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var writeToStandardOutput = archive == null || archive == "-";
                if (writeToStandardOutput && _binaryStandardOutput.IsTerminal)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Refusing to write a binary archive to the terminal. " +
                        "Specify an archive file or redirect standard output.");
                }

                string archivePath = null;
                if (!writeToStandardOutput)
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

                var session = await _targetResolver.ResolveAsync(
                        workspaceOption,
                        notebookOption,
                        token)
                    .ConfigureAwait(false);
                var sourcePath = session.Workspace.SelectedNotebook.DatabasePath;
                var result = writeToStandardOutput
                    ? await _backupService.BackupAsync(
                            new ArchiveV1StreamBackupRequest(
                                sourcePath,
                                _binaryStandardOutput.Stream),
                            token)
                        .ConfigureAwait(false)
                    : await _backupService.BackupAsync(
                            new ArchiveV1FileBackupRequest(sourcePath, archivePath),
                            token)
                        .ConfigureAwait(false);

                if (!result.IsSuccess)
                    return ArchiveV1CommandResultMapper.ToCliResult(result.Error);

                if (!writeToStandardOutput)
                {
                    _output.WriteLine(
                        $"Backup completed: source={Quote(sourcePath)}, " +
                        $"archive={Quote(archivePath)}, " +
                        $"metadata={result.MetadataCount}, pages={result.PageCount}");
                }

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
