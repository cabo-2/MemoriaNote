using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Domain;
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
        readonly IPageRepository _pageRepository;
        readonly IPageSearchRepository _pageSearchRepository;
        readonly INotebookMetadataRepository _metadataRepository;

        internal NotebookTargetSessionResolver(
            INotebookFormatValidator formatValidator,
            IPageRepository pageRepository,
            IPageSearchRepository pageSearchRepository,
            INotebookMetadataRepository metadataRepository)
        {
            _formatValidator = formatValidator ??
                throw new ArgumentNullException(nameof(formatValidator));
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
                ? ResolveSingleNotebookPath(workspacePath)
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

        static string ResolveSingleNotebookPath(string workspacePath)
        {
            var candidates = Directory
                .EnumerateFiles(workspacePath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(
                    Path.GetExtension(path),
                    NotebookFileName.Extension,
                    StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                throw new FileNotFoundException(
                    $"No notebook was found in workspace '{workspacePath}'. " +
                    "Create one with 'mn create <notebook>'.");
            }
            if (candidates.Length > 1)
            {
                throw new NotebookTargetConflictException(
                    $"More than one notebook exists in workspace '{workspacePath}'. " +
                    "Specify one with '--notebook <notebook>'.");
            }

            return candidates[0];
        }
    }

}
