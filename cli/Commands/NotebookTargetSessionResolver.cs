using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Persistence;

namespace MemoriaNote.Cli
{
    internal interface INotebookTargetSessionResolver
    {
        Task<NotebookTargetSession> ResolveAsync(
            string workspaceOption,
            string notebookOption,
            CancellationToken cancellationToken);
    }

    internal sealed class NotebookTargetSession : IAsyncDisposable
    {
        readonly NotebookApplicationSession _application;

        internal NotebookTargetSession(
            string databasePath,
            NotebookApplicationSession application)
        {
            DatabasePath = databasePath ??
                throw new ArgumentNullException(nameof(databasePath));
            _application = application ??
                throw new ArgumentNullException(nameof(application));
        }

        internal string DatabasePath { get; }

        internal NotebookDescriptor Notebook => _application.Notebook;

        internal INotebookMetadataApplication Metadata => _application.Metadata;

        internal INotebookPageApplication Pages => _application.Pages;

        public ValueTask DisposeAsync() => _application.DisposeAsync();
    }

    internal sealed class NotebookTargetConflictException : Exception
    {
        internal NotebookTargetConflictException(string message)
            : base(message)
        {
        }
    }

    internal sealed class NotebookTargetNotFoundException : Exception
    {
        internal NotebookTargetNotFoundException(string message)
            : base(message)
        {
        }

        internal NotebookTargetNotFoundException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class NotebookTargetValidationException : Exception
    {
        internal NotebookTargetValidationException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }

        internal NotebookTargetValidationException(string message)
            : base(message)
        {
        }
    }

    internal sealed class NotebookTargetSessionResolver : INotebookTargetSessionResolver
    {
        readonly IWorkspaceConfigurationStore _workspaceConfigurationStore;

        internal NotebookTargetSessionResolver(
            IWorkspaceConfigurationStore workspaceConfigurationStore)
        {
            _workspaceConfigurationStore = workspaceConfigurationStore ??
                throw new ArgumentNullException(nameof(workspaceConfigurationStore));
        }

        public async Task<NotebookTargetSession> ResolveAsync(
            string workspaceOption,
            string notebookOption,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workspacePath = WorkspacePathResolver.Resolve(workspaceOption);
            var notebookPath = notebookOption == null
                ? ResolveSelectedNotebookPath(workspacePath)
                : ResolveExplicitNotebookPath(workspacePath, notebookOption);
            var opened = await NotebookApplication.OpenAsync(
                    new NotebookOpenRequest(notebookPath),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!opened.IsSuccess)
                throw ToResolutionException(notebookPath, opened.Failure);

            return new NotebookTargetSession(notebookPath, opened.Value);
        }

        static Exception ToResolutionException(
            string notebookPath,
            NotebookFailure failure)
        {
            var fileName = Path.GetFileName(notebookPath);
            return failure.Category switch
            {
                NotebookFailureCategory.NotFound => new NotebookTargetNotFoundException(
                    $"The notebook file does not exist: {CliMessage.Quote(fileName)}."),
                NotebookFailureCategory.Format when failure.Code == "format.legacy" =>
                    new NotebookTargetValidationException(
                        $"Notebook {CliMessage.Quote(fileName)} uses schema v1. " +
                        "Run 'mn notebooks migrate' to create a schema v2 notebook."),
                NotebookFailureCategory.Format => new NotebookTargetValidationException(
                    $"Notebook {CliMessage.Quote(fileName)} is not a valid " +
                    $"current-format notebook. {failure.Message}"),
                NotebookFailureCategory.Validation => new NotebookTargetValidationException(
                    failure.Message),
                NotebookFailureCategory.AccessOrBusy or
                    NotebookFailureCategory.UnexpectedStorage => new IOException(
                        failure.Message),
                _ => new InvalidOperationException(failure.Message)
            };
        }

        static string ResolveExplicitNotebookPath(
            string workspacePath,
            string notebookOption)
        {
            var fileName = NotebookInputParser.Parse(notebookOption);
            var notebookPath = Path.Combine(workspacePath, fileName.Value);
            if (!File.Exists(notebookPath))
            {
                throw new NotebookTargetNotFoundException(
                    $"The notebook file does not exist: {CliMessage.Quote(fileName.Value)}.");
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
                throw new NotebookTargetNotFoundException(
                    "The selected notebook file does not exist: " +
                    $"{CliMessage.Quote(configuration.CurrentNotebook.Value)}.");
            }

            return notebookPath;
        }
    }

}
