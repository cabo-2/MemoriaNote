using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;

namespace MemoriaNote.Cli
{
    internal interface INotebookCreationService
    {
        Task<NotebookOperationResult<NotebookApplicationSession>> CreateAsync(
            string databasePath,
            string title,
            CancellationToken cancellationToken);
    }

    internal sealed class CurrentNotebookCreationService : INotebookCreationService
    {
        public Task<NotebookOperationResult<NotebookApplicationSession>> CreateAsync(
            string databasePath,
            string title,
            CancellationToken cancellationToken) => NotebookApplication.CreateAsync(
                new NotebookCreateRequest(databasePath, title),
                cancellationToken);
    }

    internal sealed class CreateNotebookCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly INotebookCreationService _createNotebook;
        readonly ICommandOutput _output;

        internal CreateNotebookCommandHandler(
            CliCommandExecutor executor,
            INotebookCreationService createNotebook,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _createNotebook = createNotebook ??
                throw new ArgumentNullException(nameof(createNotebook));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookFile,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                string workspacePath;
                string notebookPath;
                try
                {
                    workspacePath = WorkspacePathResolver.Resolve(workspaceOption);
                    notebookPath = ResolveNotebookPath(workspacePath, notebookFile);
                }
                catch (DirectoryNotFoundException exception)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        exception.Message,
                        exception);
                }
                catch (ArgumentException exception)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        CliMessage.ArgumentReason(exception),
                        exception);
                }

                var name = Path.GetFileNameWithoutExtension(notebookPath);
                var created = await _createNotebook.CreateAsync(
                        notebookPath,
                        name,
                        token)
                    .ConfigureAwait(false);
                if (!created.IsSuccess)
                {
                    return NotebookApplicationFailureMapper.ToCliResult(
                        created.Failure,
                        notebookPath);
                }
                await created.Value.DisposeAsync().ConfigureAwait(false);

                _output.WriteLine(
                    $"Created notebook {CliMessage.Quote(notebookPath)}.");
                return CliCommandResult.Success();
            }, cancellationToken);
        }

        static string ResolveNotebookPath(string workspacePath, string notebookFile)
        {
            var fileName = NotebookInputParser.Parse(notebookFile);
            var notebookPath = Path.Combine(workspacePath, fileName.Value);

            var name = Path.GetFileNameWithoutExtension(notebookPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "The notebook file name must include a name before the extension.",
                    nameof(notebookFile));
            }

            return notebookPath;
        }
    }

    internal static class NotebookApplicationFailureMapper
    {
        internal static CliCommandResult ToCliResult(
            NotebookFailure failure,
            string path = null)
        {
            if (failure == null)
                throw new ArgumentNullException(nameof(failure));

            var message = failure.Category == NotebookFailureCategory.Conflict &&
                          path != null
                ? $"The notebook destination already exists: {CliMessage.Quote(path)}."
                : failure.Message;
            var kind = failure.Category switch
            {
                NotebookFailureCategory.Validation => CliErrorKind.Validation,
                NotebookFailureCategory.NotFound => CliErrorKind.NotFound,
                NotebookFailureCategory.Conflict => CliErrorKind.Conflict,
                NotebookFailureCategory.Format => CliErrorKind.Validation,
                NotebookFailureCategory.DerivedData => CliErrorKind.Validation,
                NotebookFailureCategory.AccessOrBusy => CliErrorKind.Storage,
                NotebookFailureCategory.UnexpectedStorage => CliErrorKind.Storage,
                NotebookFailureCategory.Timeout => CliErrorKind.Storage,
                _ => CliErrorKind.Unexpected
            };
            return CliCommandResult.Failure(kind, message);
        }
    }
}
