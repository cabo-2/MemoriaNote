using System;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Reports whether a configured note data source already exists.
    /// </summary>
    public interface INoteDataSourceProbe
    {
        /// <summary>Returns whether the specified data source exists.</summary>
        /// <param name="dataSource">The configured data source.</param>
        /// <returns>True when the data source exists; otherwise, false.</returns>
        bool Exists(string dataSource);
    }

    /// <summary>
    /// Loads the configured workspace after its note databases are ready.
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
        /// <param name="defaultNoteCreated">Whether startup created the default note.</param>
        public ApplicationSession(
            Workspace workspace,
            IMemoriaNoteApplicationService applicationService,
            bool defaultNoteCreated = false)
        {
            Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            ApplicationService = applicationService ??
                throw new ArgumentNullException(nameof(applicationService));
            DefaultNoteCreated = defaultNoteCreated;
        }

        /// <summary>Gets the loaded workspace.</summary>
        public Workspace Workspace { get; }

        /// <summary>Gets the composed application service.</summary>
        public IMemoriaNoteApplicationService ApplicationService { get; }

        /// <summary>Gets whether startup created the configured default note.</summary>
        public bool DefaultNoteCreated { get; }
    }

    /// <summary>
    /// Creates and migrates configured notes before loading and composing the workspace.
    /// </summary>
    public sealed class ApplicationStartupService
    {
        readonly INoteMigrator _noteMigrator;
        readonly INoteDataSourceProbe _dataSourceProbe;
        readonly IWorkspaceLoader _workspaceLoader;

        /// <summary>
        /// Initializes an application startup service.
        /// </summary>
        /// <param name="noteMigrator">The note database lifecycle port.</param>
        /// <param name="dataSourceProbe">The data source availability port.</param>
        /// <param name="workspaceLoader">The configured workspace loader.</param>
        public ApplicationStartupService(
            INoteMigrator noteMigrator,
            INoteDataSourceProbe dataSourceProbe,
            IWorkspaceLoader workspaceLoader)
        {
            _noteMigrator = noteMigrator ??
                throw new ArgumentNullException(nameof(noteMigrator));
            _dataSourceProbe = dataSourceProbe ??
                throw new ArgumentNullException(nameof(dataSourceProbe));
            _workspaceLoader = workspaceLoader ??
                throw new ArgumentNullException(nameof(workspaceLoader));
        }

        /// <summary>
        /// Ensures the default note exists, migrates all configured notes, loads the
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
            var defaultNoteCreated = false;
            if (!_dataSourceProbe.Exists(request.DefaultDataSource))
            {
                await _noteMigrator.CreateAsync(
                        request.DefaultNoteName,
                        request.DefaultNoteTitle,
                        request.DefaultDataSource,
                        token)
                    .ConfigureAwait(false);
                defaultNoteCreated = true;
            }

            foreach (var dataSource in request.DataSources)
            {
                token.ThrowIfCancellationRequested();
                await _noteMigrator.MigrateAsync(dataSource, token)
                    .ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            var session = ApplicationComposition.Compose(_workspaceLoader.Load());
            return new ApplicationSession(
                session.Workspace,
                session.ApplicationService,
                defaultNoteCreated);
        }
    }
}
