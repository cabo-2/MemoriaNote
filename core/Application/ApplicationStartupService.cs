using System;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Reports whether a configured notebook database already exists.
    /// </summary>
    public interface INotebookDatabaseProbe
    {
        /// <summary>Returns whether the specified database path exists.</summary>
        /// <param name="databasePath">The configured notebook database path.</param>
        /// <returns>True when the database exists; otherwise, false.</returns>
        bool Exists(string databasePath);
    }

    /// <summary>
    /// Loads the configured workspace after its notebook databases are ready.
    /// </summary>
    public interface IWorkspaceLoader
    {
        /// <summary>Loads the current configured workspace.</summary>
        /// <returns>The loaded workspace.</returns>
        Workspace Load();
    }

    /// <summary>
    /// Contains a loaded workspace and its composed application service.
    /// </summary>
    public sealed class ApplicationSession
    {
        /// <summary>
        /// Initializes a started application session.
        /// </summary>
        /// <param name="workspace">The loaded workspace.</param>
        /// <param name="applicationService">The composed application service.</param>
        /// <param name="defaultNotebookCreated">Whether startup created the default notebook.</param>
        public ApplicationSession(
            Workspace workspace,
            IMemoriaNoteApplicationService applicationService,
            bool defaultNotebookCreated = false)
        {
            Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            ApplicationService = applicationService ??
                throw new ArgumentNullException(nameof(applicationService));
            DefaultNotebookCreated = defaultNotebookCreated;
        }

        /// <summary>Gets the loaded workspace.</summary>
        public Workspace Workspace { get; }

        /// <summary>Gets the composed application service.</summary>
        public IMemoriaNoteApplicationService ApplicationService { get; }

        /// <summary>Gets whether startup created the configured default notebook.</summary>
        public bool DefaultNotebookCreated { get; }
    }

    /// <summary>
    /// Creates and migrates configured notebooks before loading and composing the workspace.
    /// </summary>
    public sealed class ApplicationStartupService
    {
        readonly INotebookMigrator _notebookMigrator;
        readonly INotebookDatabaseProbe _databaseProbe;
        readonly IWorkspaceLoader _workspaceLoader;

        /// <summary>
        /// Initializes an application startup service.
        /// </summary>
        /// <param name="notebookMigrator">The notebook database lifecycle port.</param>
        /// <param name="databaseProbe">The data source availability port.</param>
        /// <param name="workspaceLoader">The configured workspace loader.</param>
        public ApplicationStartupService(
            INotebookMigrator notebookMigrator,
            INotebookDatabaseProbe databaseProbe,
            IWorkspaceLoader workspaceLoader)
        {
            _notebookMigrator = notebookMigrator ??
                throw new ArgumentNullException(nameof(notebookMigrator));
            _databaseProbe = databaseProbe ??
                throw new ArgumentNullException(nameof(databaseProbe));
            _workspaceLoader = workspaceLoader ??
                throw new ArgumentNullException(nameof(workspaceLoader));
        }

        /// <summary>
        /// Ensures the default notebook exists, migrates all configured notebooks, loads the
        /// workspace, and composes its application use cases.
        /// </summary>
        /// <param name="request">The immutable startup request.</param>
        /// <param name="token">The cancellation token for startup I/O.</param>
        /// <returns>The loaded and composed application session.</returns>
        public async Task<ApplicationSession> StartAsync(
            ApplicationStartupRequest request,
            CancellationToken token)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            token.ThrowIfCancellationRequested();
            var defaultNotebookCreated = false;
            if (!_databaseProbe.Exists(request.DefaultNotebookDatabasePath))
            {
                await _notebookMigrator.CreateAsync(
                        request.DefaultNotebookName,
                        request.DefaultNotebookTitle,
                        request.DefaultNotebookDatabasePath,
                        token)
                    .ConfigureAwait(false);
                defaultNotebookCreated = true;
            }

            foreach (var databasePath in request.NotebookDatabasePaths)
            {
                token.ThrowIfCancellationRequested();
                await _notebookMigrator.MigrateAsync(databasePath, token)
                    .ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            var session = ApplicationComposition.Compose(_workspaceLoader.Load());
            return new ApplicationSession(
                session.Workspace,
                session.ApplicationService,
                defaultNotebookCreated);
        }
    }
}
