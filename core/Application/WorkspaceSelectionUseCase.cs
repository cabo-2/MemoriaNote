using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Persistence;

namespace MemoriaNote.Application
{
    /// <summary>Changes the persistent notebook selection for one workspace.</summary>
    public sealed class WorkspaceSelectionUseCase
    {
        readonly INotebookFormatValidator _notebookFormatValidator;
        readonly IWorkspaceConfigurationStore _configurationStore;

        /// <summary>Initializes a workspace selection use case.</summary>
        /// <param name="notebookFormatValidator">The live-notebook validator.</param>
        /// <param name="configurationStore">The workspace configuration store.</param>
        public WorkspaceSelectionUseCase(
            INotebookFormatValidator notebookFormatValidator,
            IWorkspaceConfigurationStore configurationStore)
        {
            _notebookFormatValidator = notebookFormatValidator ??
                throw new ArgumentNullException(nameof(notebookFormatValidator));
            _configurationStore = configurationStore ??
                throw new ArgumentNullException(nameof(configurationStore));
        }

        /// <summary>Validates and selects a workspace-root notebook.</summary>
        /// <param name="workspacePath">The resolved workspace directory.</param>
        /// <param name="notebookFileName">The normalized notebook leaf name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>True when the persisted selection changed; otherwise false.</returns>
        public async Task<bool> SelectAsync(
            string workspacePath,
            NotebookFileName notebookFileName,
            CancellationToken cancellationToken)
        {
            if (notebookFileName == null)
                throw new ArgumentNullException(nameof(notebookFileName));

            cancellationToken.ThrowIfCancellationRequested();
            var loaded = _configurationStore.Load(workspacePath);
            var notebookPath = Path.Combine(workspacePath, notebookFileName.Value);
            await _notebookFormatValidator
                .ValidateCurrentAsync(notebookPath, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (loaded.Status == WorkspaceConfigurationLoadStatus.Loaded &&
                notebookFileName.Equals(loaded.Configuration.CurrentNotebook))
            {
                return false;
            }

            _configurationStore.Save(
                workspacePath,
                WorkspaceConfiguration.CreateSelected(notebookFileName));
            return true;
        }

        /// <summary>Clears a saved selection and returns to the virtual root.</summary>
        /// <param name="workspacePath">The resolved workspace directory.</param>
        /// <returns>True when the persisted selection changed; otherwise false.</returns>
        public bool SelectRoot(string workspacePath)
        {
            var loaded = _configurationStore.Load(workspacePath);
            if (loaded.Status == WorkspaceConfigurationLoadStatus.Missing ||
                loaded.Configuration.CurrentNotebook == null)
            {
                return false;
            }

            _configurationStore.Save(
                workspacePath,
                WorkspaceConfiguration.CreateRoot());
            return true;
        }
    }
}
