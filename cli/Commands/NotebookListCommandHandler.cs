using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;

namespace MemoriaNote.Cli
{
    internal sealed class NotebookListCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly WorkspaceNotebookListUseCase _notebookList;
        readonly ICommandOutput _output;

        internal NotebookListCommandHandler(
            CliCommandExecutor executor,
            WorkspaceNotebookListUseCase notebookList,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _notebookList = notebookList ??
                throw new ArgumentNullException(nameof(notebookList));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            bool longFormat,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var workspacePath = WorkspacePathResolver.Resolve(workspaceOption);
                var entries = await _notebookList
                    .ListAsync(workspacePath, token)
                    .ConfigureAwait(false);
                _output.WriteWorkspaceNotebookList(entries, longFormat);
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }
}
