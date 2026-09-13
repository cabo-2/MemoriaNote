using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a notebook in the MemoriaNote application.
    /// Provides methods for managing notebook metadata and pages.
    /// </summary>
    public class Notebook
    {
        string _databasePath = null;
        readonly IPageRepository _repository;
        readonly IPageSearchRepository _searchRepository;
        readonly INotebookMetadataRepository _metadataRepository;

        /// <summary>Initializes an empty notebook reference.</summary>
        public Notebook() { }

        /// <summary>Initializes a notebook for the specified SQLite database path.</summary>
        /// <param name="databasePath">The notebook database path.</param>
        public Notebook(string databasePath)
        {
            InitializeDatabasePath(databasePath);
        }

        /// <summary>
        /// Initializes a notebook with an explicit search persistence boundary.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="searchRepository">The repository used for notebook searches.</param>
        public Notebook(string databasePath, IPageSearchRepository searchRepository)
        {
            _searchRepository = searchRepository ??
                throw new ArgumentNullException(nameof(searchRepository));
            InitializeDatabasePath(databasePath);
        }

        /// <summary>
        /// Initializes a notebook with an explicit page persistence boundary.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="repository">The repository used for page operations.</param>
        public Notebook(string databasePath, IPageRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            InitializeDatabasePath(databasePath);
        }

        /// <summary>
        /// Initializes a notebook with an explicit metadata persistence boundary.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="metadataRepository">The repository used for metadata operations.</param>
        public Notebook(string databasePath, INotebookMetadataRepository metadataRepository)
        {
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
            InitializeDatabasePath(databasePath);
        }

        /// <summary>
        /// Initializes a notebook with explicit page and search persistence boundaries.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="repository">The repository used for page operations.</param>
        /// <param name="searchRepository">The repository used for notebook searches.</param>
        public Notebook(
            string databasePath,
            IPageRepository repository,
            IPageSearchRepository searchRepository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _searchRepository = searchRepository ??
                throw new ArgumentNullException(nameof(searchRepository));
            InitializeDatabasePath(databasePath);
        }

        /// <summary>
        /// Initializes a notebook with explicit page, search, and metadata persistence boundaries.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="repository">The repository used for page operations.</param>
        /// <param name="searchRepository">The repository used for notebook searches.</param>
        /// <param name="metadataRepository">The repository used for metadata operations.</param>
        public Notebook(
            string databasePath,
            IPageRepository repository,
            IPageSearchRepository searchRepository,
            INotebookMetadataRepository metadataRepository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _searchRepository = searchRepository ??
                throw new ArgumentNullException(nameof(searchRepository));
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
            InitializeDatabasePath(databasePath);
        }

        internal Notebook(
            string databasePath,
            INotebookMetadataRepository metadataRepository,
            NotebookMetadataResult metadata)
        {
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            DatabasePath = databasePath;
            ApplyMetadata(metadata);
        }

        /// <summary>
        /// Reads a specific Page from the database based on the provided name and index.
        /// </summary>
        /// <param name="name">The name of the Page to read.</param>
        /// <param name="index">The index of the Page to read.</param>
        /// <returns>The Page object if found, or null if not found.</returns>
        public Page ReadPage(string name, int index)
        {
            return Repository.FindPageAsync(
                DatabasePath,
                name,
                index,
                CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads a specific Page from the database based on the provided unique identifier (GUID).
        /// </summary>
        /// <param name="guid">The unique identifier (GUID) of the Page to read.</param>
        /// <returns>The Page object if found, or null if not found.</returns>
        public Page ReadPage(Guid guid)
        {
            return Repository.FindPageAsync(
                DatabasePath,
                guid,
                CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Retrieves a collection of pages with the specified name from the database.
        /// </summary>
        /// <param name="name">The name of the pages to retrieve.</param>
        /// <returns>An IEnumerable collection of Page objects.</returns>
        public IEnumerable<Page> ReadPage(string name)
        {
            return Repository.ListPagesByHeadingAsync(
                    DatabasePath,
                    name,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Creates a new Page with the specified name, text content, and optional directory.
        /// The page is added to the database, its index is set, and the database is saved.
        /// </summary>
        /// <param name="name">The name of the page.</param>
        /// <param name="text">The text content of the page.</param>
        /// <param name="dir">Optional directory for the page. Default is null.</param>
        /// <returns>The newly created Page object.</returns>
        public Page CreatePage(string name, string text, string dir = null)
        {
            return Repository.CreatePageAsync(
                DatabasePath,
                name,
                text,
                dir,
                CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates an existing Page in the database with the provided new Page object.
        /// The method retrieves the old Page from the database, updates its last modified timestamp,
        /// and updates the new Page without changing its dictionary sense index. If the name changes,
        /// the page is appended to the destination name and the source indexes are compacted.
        /// </summary>
        /// <param name="newPage">The new Page object containing the updated information.</param>
        public void UpdatePage(Page newPage)
        {
            var persistedPage = Repository.UpdatePageAsync(
                DatabasePath,
                newPage,
                CancellationToken.None).GetAwaiter().GetResult();
            newPage.Rowid = persistedPage.Rowid;
            newPage.Index = persistedPage.Index;
            newPage.UpdateTime = persistedPage.UpdateTime;
        }

        /// <summary>
        /// Deletes a specific page from the database based on its stable identifier.
        /// The page with the corresponding identifier is removed and the remaining indexes
        /// for its exact-name group are compacted in the same transaction.
        /// </summary>
        /// <param name="guid">The stable identifier of the page to delete.</param>
        public void DeletePage(Guid guid)
        {
            Repository.DeletePageAsync(
                DatabasePath,
                guid,
                CancellationToken.None).GetAwaiter().GetResult();
        }

        internal IPageSearchRepository SearchRepository =>
            _searchRepository ?? DefaultSearchRepository.Instance;

        internal IPageRepository Repository =>
            _repository ?? DefaultRepository.Instance;

        INotebookMetadataRepository MetadataRepository =>
            _metadataRepository ?? DefaultMetadataRepository.Instance;

        static class DefaultRepository
        {
            internal static readonly IPageRepository Instance =
                new SqlitePageRepository(
                    new SqliteNotebookDbContextFactory(NotebookDbContext.MyLoggerFactory));
        }

        static class DefaultSearchRepository
        {
            internal static readonly IPageSearchRepository Instance =
                new SqlitePageSearchRepository(
                    new SqliteNotebookDbContextFactory(NotebookDbContext.MyLoggerFactory));
        }

        static class DefaultMetadataRepository
        {
            internal static readonly INotebookMetadataRepository Instance =
                new SqliteNotebookMetadataRepository(
                    new SqliteNotebookDbContextFactory(NotebookDbContext.MyLoggerFactory));
        }

        /// <summary>
        /// Gets the count of contents in the notebook repository.
        /// </summary>
        /// <returns>An integer representing the total count of contents in the database.</returns>
        public int Count
        {
            get => Repository.CountPagesAsync(DatabasePath, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Gets or sets the database path for notebook operations.
        /// Setting this property clears the currently loaded metadata snapshot without accessing the database.
        /// </summary>
        public string DatabasePath
        {
            get => _databasePath;
            set
            {
                _databasePath = value;
                Metadata = null;
                MetadataIssues = Array.Empty<MetadataIssue>();
            }
        }

        /// <summary>
        /// Gets the most recently loaded or persisted metadata snapshot.
        /// </summary>
        public NotebookMetadata Metadata { get; private set; }

        /// <summary>
        /// Gets classifiable problems found while producing the current metadata snapshot.
        /// </summary>
        public IReadOnlyList<MetadataIssue> MetadataIssues { get; private set; } =
            Array.Empty<MetadataIssue>();

        /// <summary>
        /// Reloads the metadata snapshot from the notebook database.
        /// </summary>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The loaded snapshot and any classifiable value problems.</returns>
        public async Task<NotebookMetadataResult> ReloadMetadataAsync(CancellationToken token)
        {
            var result = await MetadataRepository
                .LoadAsync(DatabasePath, token)
                .ConfigureAwait(false);
            ApplyMetadata(result);
            return result;
        }

        /// <summary>
        /// Persists requested metadata fields atomically and replaces the current snapshot.
        /// </summary>
        /// <param name="patch">The metadata fields to patch together.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The saved snapshot and any classifiable value problems.</returns>
        public async Task<NotebookMetadataResult> UpdateMetadataAsync(
            NotebookMetadataPatch patch,
            CancellationToken token)
        {
            var result = await MetadataRepository
                .UpdateAsync(DatabasePath, patch, token)
                .ConfigureAwait(false);
            ApplyMetadata(result);
            return result;
        }

        /// <summary>
        /// Persists requested metadata fields atomically through the synchronous compatibility API.
        /// </summary>
        /// <param name="patch">The metadata fields to patch together.</param>
        /// <returns>The saved snapshot and any classifiable value problems.</returns>
        public NotebookMetadataResult UpdateMetadata(NotebookMetadataPatch patch)
        {
            return UpdateMetadataAsync(patch, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        void InitializeDatabasePath(string databasePath)
        {
            DatabasePath = databasePath;
            if (databasePath == null || !File.Exists(databasePath))
                return;

            var result = MetadataRepository.LoadAsync(databasePath, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ApplyMetadata(result);
        }

        void ApplyMetadata(NotebookMetadataResult result)
        {
            Metadata = result.Metadata;
            MetadataIssues = result.Issues;
        }
        
        public override string ToString()
        {
            if (Metadata != null)
            {
                StringBuilder buffer = new StringBuilder();
                buffer.Append(Metadata.Name);
                buffer.Append(" (");
                buffer.Append(Metadata.Title);
                buffer.Append(")");
                return buffer.ToString();
            }
            return base.ToString();
        }
    }
}
