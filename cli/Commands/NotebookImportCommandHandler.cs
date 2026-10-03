using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Transfer;

namespace MemoriaNote.Cli
{
    internal sealed class NotebookImportCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly TextPageImporter _importer;
        readonly ICommandOutput _output;

        internal NotebookImportCommandHandler(
            CliCommandExecutor executor,
            INotebookTargetSessionResolver targetResolver,
            TextPageImporter importer,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _targetResolver = targetResolver ??
                throw new ArgumentNullException(nameof(targetResolver));
            _importer = importer ?? throw new ArgumentNullException(nameof(importer));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookOption,
            string sourceDirectory,
            string conflictPolicy,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (string.IsNullOrWhiteSpace(sourceDirectory))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "The import directory cannot be empty.");
                }

                if (!TryParseConflictPolicy(conflictPolicy, out var parsedPolicy))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "The conflict policy must be fail, skip, or replace.");
                }

                string sourcePath;
                try
                {
                    sourcePath = Path.GetFullPath(sourceDirectory);
                }
                catch (ArgumentException exception)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        exception.Message,
                        exception);
                }

                if (!Directory.Exists(sourcePath))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        $"The import directory does not exist: {sourcePath}");
                }

                var session = await _targetResolver.ResolveAsync(
                        workspaceOption,
                        notebookOption,
                        token)
                    .ConfigureAwait(false);
                var targetPath = session.Workspace.SelectedNotebook.DatabasePath;
                var result = await _importer.ImportAsync(
                        new TextPageImportRequest(
                            NotebookId.FromDatabasePath(targetPath),
                            sourcePath,
                            parsedPolicy,
                            dryRun),
                        token)
                    .ConfigureAwait(false);
                if (!result.IsSuccess)
                    return ToCliResult(result.ErrorCode.Value);

                _output.WriteLine(
                    $"Import {(dryRun ? "validated" : "completed")}: " +
                    $"source={Quote(sourcePath)}, target={Quote(targetPath)}, " +
                    $"created={result.CreatedCount}, replaced={result.ReplacedCount}, " +
                    $"skipped={result.SkippedCount}");
                return CliCommandResult.Success();
            }, cancellationToken);
        }

        static bool TryParseConflictPolicy(
            string value,
            out TextPageImportConflictPolicy policy)
        {
            switch (value ?? "fail")
            {
                case "fail":
                    policy = TextPageImportConflictPolicy.Fail;
                    return true;
                case "skip":
                    policy = TextPageImportConflictPolicy.Skip;
                    return true;
                case "replace":
                    policy = TextPageImportConflictPolicy.Replace;
                    return true;
                default:
                    policy = default;
                    return false;
            }
        }

        static CliCommandResult ToCliResult(TextPageImportErrorCode errorCode)
        {
            return errorCode switch
            {
                TextPageImportErrorCode.InputNotFound => Failure(
                    CliErrorKind.NotFound,
                    "An import input no longer exists."),
                TextPageImportErrorCode.InvalidEncoding => Failure(
                    CliErrorKind.Validation,
                    "Every imported text file must contain valid UTF-8."),
                TextPageImportErrorCode.InvalidPageName => Failure(
                    CliErrorKind.Validation,
                    "An import file name decodes to an invalid page name."),
                TextPageImportErrorCode.DuplicateInputName => Failure(
                    CliErrorKind.Conflict,
                    "Multiple import files decode to the same page name."),
                TextPageImportErrorCode.ExistingPageConflict => Failure(
                    CliErrorKind.Conflict,
                    "The target notebook already contains an imported page name."),
                TextPageImportErrorCode.AmbiguousPageConflict => Failure(
                    CliErrorKind.Conflict,
                    "Replace requires exactly one existing page with each imported name."),
                TextPageImportErrorCode.ReadOnly => Failure(
                    CliErrorKind.Conflict,
                    "The target notebook is read-only."),
                TextPageImportErrorCode.IoFailure => Failure(
                    CliErrorKind.Storage,
                    "The import failed while reading a source file."),
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
