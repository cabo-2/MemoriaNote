using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;

namespace MemoriaNote.Cli
{
    internal sealed class CreateNotebookCommandHandler
    {
        internal const string NotebookExtension = ".mnote";

        readonly CliCommandExecutor _executor;
        readonly CreateNotebookUseCase _createNotebook;
        readonly ICommandOutput _output;

        internal CreateNotebookCommandHandler(
            CliCommandExecutor executor,
            CreateNotebookUseCase createNotebook,
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
                        exception.Message,
                        exception);
                }

                var name = Path.GetFileNameWithoutExtension(notebookPath);
                try
                {
                    await _createNotebook.CreateAsync(
                            name,
                            name,
                            notebookPath,
                            token)
                        .ConfigureAwait(false);
                }
                catch (ArgumentException exception) when (File.Exists(notebookPath))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Conflict,
                        $"The notebook file already exists: {notebookPath}",
                        exception);
                }
                catch (ArgumentException exception)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        exception.Message,
                        exception);
                }

                _output.WriteLine($"Created notebook: {notebookPath}");
                return CliCommandResult.Success();
            }, cancellationToken);
        }

        static string ResolveNotebookPath(string workspacePath, string notebookFile)
        {
            if (string.IsNullOrWhiteSpace(notebookFile))
                throw new ArgumentException("A notebook file is required.", nameof(notebookFile));
            if (!string.Equals(
                Path.GetExtension(notebookFile),
                NotebookExtension,
                StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The notebook file must use the '{NotebookExtension}' extension.",
                    nameof(notebookFile));
            }

            var notebookPath = WorkspacePathResolver.ResolveInside(
                workspacePath,
                notebookFile);

            var name = Path.GetFileNameWithoutExtension(notebookPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "The notebook file name must include a name before the extension.",
                    nameof(notebookFile));
            }

            var parentDirectory = Path.GetDirectoryName(notebookPath);
            if (string.IsNullOrEmpty(parentDirectory) || !Directory.Exists(parentDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"The notebook directory does not exist: {parentDirectory}");
            }

            return notebookPath;
        }
    }
}
