using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Models;
using MemoriaNote.Persistence;

namespace MemoriaNote.Cli
{
    internal interface INotebookTargetSessionResolver
    {
        Task<ApplicationSession> ResolveAsync(
            string workspaceOption,
            string notebookOption,
            CancellationToken cancellationToken);
    }

    internal sealed class NotebookTargetConflictException : Exception
    {
        internal NotebookTargetConflictException(string message)
            : base(message)
        {
        }
    }

    internal sealed class NotebookTargetSessionResolver : INotebookTargetSessionResolver
    {
        readonly INotebookFormatValidator _formatValidator;
        readonly IWorkspaceConfigurationStore _workspaceConfigurationStore;
        readonly IPageRepository _pageRepository;
        readonly IPageSearchRepository _pageSearchRepository;
        readonly INotebookMetadataRepository _metadataRepository;

        internal NotebookTargetSessionResolver(
            INotebookFormatValidator formatValidator,
            IWorkspaceConfigurationStore workspaceConfigurationStore,
            IPageRepository pageRepository,
            IPageSearchRepository pageSearchRepository,
            INotebookMetadataRepository metadataRepository)
        {
            _formatValidator = formatValidator ??
                throw new ArgumentNullException(nameof(formatValidator));
            _workspaceConfigurationStore = workspaceConfigurationStore ??
                throw new ArgumentNullException(nameof(workspaceConfigurationStore));
            _pageRepository = pageRepository ??
                throw new ArgumentNullException(nameof(pageRepository));
            _pageSearchRepository = pageSearchRepository ??
                throw new ArgumentNullException(nameof(pageSearchRepository));
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
        }

        public async Task<ApplicationSession> ResolveAsync(
            string workspaceOption,
            string notebookOption,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workspacePath = WorkspacePathResolver.Resolve(workspaceOption);
            var notebookPath = notebookOption == null
                ? ResolveSelectedNotebookPath(workspacePath)
                : ResolveExplicitNotebookPath(workspacePath, notebookOption);
            await _formatValidator
                .ValidateCurrentAsync(notebookPath, cancellationToken)
                .ConfigureAwait(false);

            var notebook = new Notebook(
                notebookPath,
                _pageRepository,
                _pageSearchRepository,
                _metadataRepository);
            var workspace = new Workspace(
                Path.GetFileName(workspacePath),
                new[] { notebook },
                notebook);
            return ApplicationComposition.Compose(workspace);
        }

        static string ResolveExplicitNotebookPath(
            string workspacePath,
            string notebookOption)
        {
            var fileName = NotebookInputParser.Parse(notebookOption);
            var notebookPath = Path.Combine(workspacePath, fileName.Value);
            if (!File.Exists(notebookPath))
            {
                throw new FileNotFoundException(
                    $"The notebook file does not exist: {notebookPath}",
                    notebookPath);
            }

            return notebookPath;
        }

        string ResolveSelectedNotebookPath(string workspacePath)
        {
            var configuration = _workspaceConfigurationStore
                .Load(workspacePath)
                .Configuration;
            if (configuration.CurrentNotebook == null)
            {
                throw new NotebookTargetConflictException(
                    $"No notebook is selected in workspace '{workspacePath}'. " +
                    "Select one with 'mn use <notebook>' or specify " +
                    "'--notebook <notebook>' for this invocation.");
            }

            var notebookPath = Path.Combine(
                workspacePath,
                configuration.CurrentNotebook.Value);
            if (!File.Exists(notebookPath))
            {
                throw new FileNotFoundException(
                    $"The selected notebook file does not exist: {notebookPath}",
                    notebookPath);
            }

            return notebookPath;
        }
    }

}
