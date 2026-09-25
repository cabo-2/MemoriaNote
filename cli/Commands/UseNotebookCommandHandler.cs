using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;

namespace MemoriaNote.Cli
{
    internal sealed class UseNotebookCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly WorkspaceSelectionUseCase _selection;
        readonly ICommandOutput _output;

        internal UseNotebookCommandHandler(
            CliCommandExecutor executor,
            WorkspaceSelectionUseCase selection,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebook,
            bool root,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var hasNotebook = notebook != null;
                if (hasNotebook == root)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Specify exactly one of <notebook> or --root.");
                }

                var workspacePath = WorkspacePathResolver.Resolve(workspaceOption);
                if (root)
                {
                    _selection.SelectRoot(workspacePath);
                    _output.WriteLine("Using workspace root: /");
                }
                else
                {
                    var notebookFileName = NotebookInputParser.Parse(notebook);
                    await _selection
                        .SelectAsync(workspacePath, notebookFileName, token)
                        .ConfigureAwait(false);
                    _output.WriteLine($"Using notebook: {notebookFileName.Value}");
                }

                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }
}
