using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Persistence;

namespace MemoriaNote.Application
{
    /// <summary>Identifies the current state of a workspace selection.</summary>
    public enum WorkspaceStatusKind
    {
        /// <summary>The workspace root is selected.</summary>
        Root,

        /// <summary>The selected notebook is ready for read and write operations.</summary>
        Ready,

        /// <summary>The selected notebook does not exist.</summary>
        Missing,

        /// <summary>The workspace configuration or selected notebook is invalid.</summary>
        Invalid,

        /// <summary>The workspace configuration or selected notebook is newer than supported.</summary>
        Unsupported,

        /// <summary>The selected notebook permits read operations only.</summary>
        ReadOnly
    }

    /// <summary>Identifies which resource a workspace status describes.</summary>
    public enum WorkspaceStatusSubject
    {
        /// <summary>The status describes the workspace or its configuration.</summary>
        Workspace,

        /// <summary>The status describes the selected notebook.</summary>
        Notebook
    }

    /// <summary>Contains a non-mutating diagnostic snapshot of one workspace.</summary>
    public sealed class WorkspaceStatusResult
    {
        internal WorkspaceStatusResult(
            string workspacePath,
            string virtualLocation,
            NotebookFileName currentNotebook,
            WorkspaceStatusKind status,
            WorkspaceStatusSubject subject,
            string detail)
        {
            WorkspacePath = workspacePath ??
                throw new ArgumentNullException(nameof(workspacePath));
            VirtualLocation = virtualLocation;
            CurrentNotebook = currentNotebook;
            Status = status;
            Subject = subject;
            Detail = detail;
        }

        /// <summary>Gets the resolved physical workspace path.</summary>
        public string WorkspacePath { get; }

        /// <summary>
        /// Gets the virtual root or notebook location, or null when configuration is unusable.
        /// </summary>
        public string VirtualLocation { get; }

        /// <summary>Gets the saved notebook leaf name, or null when none can be determined.</summary>
        public NotebookFileName CurrentNotebook { get; }

        /// <summary>Gets the classified workspace status.</summary>
        public WorkspaceStatusKind Status { get; }

        /// <summary>Gets the resource described by the status.</summary>
        public WorkspaceStatusSubject Subject { get; }

        /// <summary>Gets an optional diagnostic explanation.</summary>
        public string Detail { get; }
    }

    /// <summary>Inspects workspace selection state without changing persisted data.</summary>
    public sealed class WorkspaceStatusUseCase
    {
        readonly IWorkspaceConfigurationStore _configurationStore;
        readonly INotebookFormatValidator _notebookFormatValidator;

        /// <summary>Initializes a workspace status use case.</summary>
        /// <param name="configurationStore">The workspace configuration store.</param>
        /// <param name="notebookFormatValidator">The live-notebook validator.</param>
        public WorkspaceStatusUseCase(
            IWorkspaceConfigurationStore configurationStore,
            INotebookFormatValidator notebookFormatValidator)
        {
            _configurationStore = configurationStore ??
                throw new ArgumentNullException(nameof(configurationStore));
            _notebookFormatValidator = notebookFormatValidator ??
                throw new ArgumentNullException(nameof(notebookFormatValidator));
        }

        /// <summary>Inspects the current selection and notebook state.</summary>
        /// <param name="workspacePath">The resolved workspace directory.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A diagnostic snapshot that does not modify the workspace.</returns>
        public async Task<WorkspaceStatusResult> InspectAsync(
            string workspacePath,
            CancellationToken cancellationToken)
        {
            if (workspacePath == null)
                throw new ArgumentNullException(nameof(workspacePath));

            cancellationToken.ThrowIfCancellationRequested();
            var normalizedWorkspacePath = Path.GetFullPath(workspacePath);
            WorkspaceConfigurationLoadResult loaded;
            try
            {
                loaded = _configurationStore.Load(normalizedWorkspacePath);
            }
            catch (UnsupportedWorkspaceConfigurationVersionException exception)
            {
                return ConfigurationFailure(
                    normalizedWorkspacePath,
                    WorkspaceStatusKind.Unsupported,
                    exception.Message);
            }
            catch (WorkspaceConfigurationFormatException exception)
            {
                return ConfigurationFailure(
                    normalizedWorkspacePath,
                    WorkspaceStatusKind.Invalid,
                    exception.Message);
            }

            var currentNotebook = loaded.Configuration.CurrentNotebook;
            if (currentNotebook == null)
            {
                return new WorkspaceStatusResult(
                    normalizedWorkspacePath,
                    "/",
                    null,
                    WorkspaceStatusKind.Root,
                    WorkspaceStatusSubject.Workspace,
                    null);
            }

            var virtualLocation = "/" + currentNotebook.Value.Substring(
                0,
                currentNotebook.Value.Length - NotebookFileName.Extension.Length);
            var notebookPath = Path.Combine(
                normalizedWorkspacePath,
                currentNotebook.Value);
            if (!File.Exists(notebookPath))
            {
                return NotebookFailure(
                    normalizedWorkspacePath,
                    virtualLocation,
                    currentNotebook,
                    WorkspaceStatusKind.Missing,
                    "The selected notebook file does not exist.");
            }

            try
            {
                var metadata = await _notebookFormatValidator
                    .ValidateCurrentAsync(notebookPath, cancellationToken)
                    .ConfigureAwait(false);
                return new WorkspaceStatusResult(
                    normalizedWorkspacePath,
                    virtualLocation,
                    currentNotebook,
                    metadata.Metadata.ReadOnly
                        ? WorkspaceStatusKind.ReadOnly
                        : WorkspaceStatusKind.Ready,
                    WorkspaceStatusSubject.Notebook,
                    metadata.Metadata.ReadOnly
                        ? "Read operations are available, but changes are not allowed."
                        : null);
            }
            catch (UnsupportedNotebookFormatVersionException exception)
            {
                return NotebookFailure(
                    normalizedWorkspacePath,
                    virtualLocation,
                    currentNotebook,
                    WorkspaceStatusKind.Unsupported,
                    exception.Message);
            }
            catch (FileNotFoundException)
            {
                return NotebookFailure(
                    normalizedWorkspacePath,
                    virtualLocation,
                    currentNotebook,
                    WorkspaceStatusKind.Missing,
                    "The selected notebook file does not exist.");
            }
            catch (InvalidDataException exception)
            {
                return NotebookFailure(
                    normalizedWorkspacePath,
                    virtualLocation,
                    currentNotebook,
                    WorkspaceStatusKind.Invalid,
                    exception.Message);
            }
        }

        static WorkspaceStatusResult ConfigurationFailure(
            string workspacePath,
            WorkspaceStatusKind status,
            string detail)
        {
            return new WorkspaceStatusResult(
                workspacePath,
                null,
                null,
                status,
                WorkspaceStatusSubject.Workspace,
                detail);
        }

        static WorkspaceStatusResult NotebookFailure(
            string workspacePath,
            string virtualLocation,
            NotebookFileName currentNotebook,
            WorkspaceStatusKind status,
            string detail)
        {
            return new WorkspaceStatusResult(
                workspacePath,
                virtualLocation,
                currentNotebook,
                status,
                WorkspaceStatusSubject.Notebook,
                detail);
        }
    }
}
