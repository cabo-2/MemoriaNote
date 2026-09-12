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
    /// Loads the configured workgroup after its note databases are ready.
    /// </summary>
    public interface IWorkgroupLoader
    {
        /// <summary>Loads the current configured workgroup.</summary>
        /// <returns>The loaded workgroup.</returns>
        Workgroup Load();
    }

    /// <summary>
    /// Contains a loaded workgroup and its composed application service.
    /// </summary>
    public sealed class ApplicationSession
    {
        /// <summary>
        /// Initializes a started application session.
        /// </summary>
        /// <param name="workgroup">The loaded workgroup.</param>
        /// <param name="applicationService">The composed application service.</param>
        /// <param name="defaultNoteCreated">Whether startup created the default note.</param>
        public ApplicationSession(
            Workgroup workgroup,
            IMemoriaNoteApplicationService applicationService,
            bool defaultNoteCreated = false)
        {
            Workgroup = workgroup ?? throw new ArgumentNullException(nameof(workgroup));
            ApplicationService = applicationService ??
                throw new ArgumentNullException(nameof(applicationService));
            DefaultNoteCreated = defaultNoteCreated;
        }

        /// <summary>Gets the loaded workgroup.</summary>
        public Workgroup Workgroup { get; }

        /// <summary>Gets the composed application service.</summary>
        public IMemoriaNoteApplicationService ApplicationService { get; }

        /// <summary>Gets whether startup created the configured default note.</summary>
        public bool DefaultNoteCreated { get; }
    }

    /// <summary>
    /// Creates and migrates configured notes before loading and composing the workgroup.
    /// </summary>
    public sealed class ApplicationStartupService
    {
        readonly INoteMigrator _noteMigrator;
        readonly INoteDataSourceProbe _dataSourceProbe;
        readonly IWorkgroupLoader _workgroupLoader;

        /// <summary>
        /// Initializes an application startup service.
        /// </summary>
        /// <param name="noteMigrator">The note database lifecycle port.</param>
        /// <param name="dataSourceProbe">The data source availability port.</param>
        /// <param name="workgroupLoader">The configured workgroup loader.</param>
        public ApplicationStartupService(
            INoteMigrator noteMigrator,
            INoteDataSourceProbe dataSourceProbe,
            IWorkgroupLoader workgroupLoader)
        {
            _noteMigrator = noteMigrator ??
                throw new ArgumentNullException(nameof(noteMigrator));
            _dataSourceProbe = dataSourceProbe ??
                throw new ArgumentNullException(nameof(dataSourceProbe));
            _workgroupLoader = workgroupLoader ??
                throw new ArgumentNullException(nameof(workgroupLoader));
        }

        /// <summary>
        /// Ensures the default note exists, migrates all configured notes, loads the
        /// workgroup, and composes its application use cases.
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
            var session = ApplicationComposition.Compose(_workgroupLoader.Load());
            return new ApplicationSession(
                session.Workgroup,
                session.ApplicationService,
                defaultNoteCreated);
        }
    }
}
