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
    /// Represents a Note in the MemoriaNote application.
    /// Provides methods for managing note metadata and pages.
    /// </summary>
    public class Note
    {
        string _dataSource = null;
        readonly INoteRepository _repository;
        readonly INoteSearchRepository _searchRepository;
        readonly INoteMetadataRepository _metadataRepository;

        public Note() { }
        public Note(string dataSource)
        {
            InitializeDataSource(dataSource);
        }

        /// <summary>
        /// Initializes a note with an explicit search persistence boundary.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="searchRepository">The repository used for note searches.</param>
        public Note(string dataSource, INoteSearchRepository searchRepository)
        {
            _searchRepository = searchRepository ??
                throw new ArgumentNullException(nameof(searchRepository));
            InitializeDataSource(dataSource);
        }

        /// <summary>
        /// Initializes a note with an explicit page persistence boundary.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="repository">The repository used for page operations.</param>
        public Note(string dataSource, INoteRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            InitializeDataSource(dataSource);
        }

        /// <summary>
        /// Initializes a note with an explicit metadata persistence boundary.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="metadataRepository">The repository used for metadata operations.</param>
        public Note(string dataSource, INoteMetadataRepository metadataRepository)
        {
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
            InitializeDataSource(dataSource);
        }

        /// <summary>
        /// Initializes a note with explicit page and search persistence boundaries.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="repository">The repository used for page operations.</param>
        /// <param name="searchRepository">The repository used for note searches.</param>
        public Note(
            string dataSource,
            INoteRepository repository,
            INoteSearchRepository searchRepository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _searchRepository = searchRepository ??
                throw new ArgumentNullException(nameof(searchRepository));
            InitializeDataSource(dataSource);
        }

        /// <summary>
        /// Initializes a note with explicit page, search, and metadata persistence boundaries.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="repository">The repository used for page operations.</param>
        /// <param name="searchRepository">The repository used for note searches.</param>
        /// <param name="metadataRepository">The repository used for metadata operations.</param>
        public Note(
            string dataSource,
            INoteRepository repository,
            INoteSearchRepository searchRepository,
            INoteMetadataRepository metadataRepository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _searchRepository = searchRepository ??
                throw new ArgumentNullException(nameof(searchRepository));
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
            InitializeDataSource(dataSource);
        }

        internal Note(
            string dataSource,
            INoteMetadataRepository metadataRepository,
            MetadataLoadResult metadata)
        {
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            DataSource = dataSource;
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
            return SetOwner(Repository.ReadPageAsync(
                DataSource,
                name,
                index,
                CancellationToken.None).GetAwaiter().GetResult());
        }

        /// <summary>
        /// Reads a specific Page from the database based on the provided unique identifier (GUID).
        /// </summary>
        /// <param name="guid">The unique identifier (GUID) of the Page to read.</param>
        /// <returns>The Page object if found, or null if not found.</returns>
        public Page ReadPage(Guid guid)
        {
            return SetOwner(Repository.ReadPageAsync(
                DataSource,
                guid,
                CancellationToken.None).GetAwaiter().GetResult());
        }

        /// <summary>
        /// Reads a specific Page from the database based on the provided Content object.
        /// </summary>
        /// <param name="content">The Content object representing the Page to read.</param>
        /// <returns>The Page object if found, or null if not found.</returns>
        public Page ReadPage(IContent content) => ReadPage(content.Guid);

        /// <summary>
        /// Retrieves a collection of pages with the specified name from the database.
        /// </summary>
        /// <param name="name">The name of the pages to retrieve.</param>
        /// <returns>An IEnumerable collection of Page objects.</returns>
        public IEnumerable<Page> ReadPage(string name)
        {
            return Repository.ReadPagesAsync(
                    DataSource,
                    name,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult()
                .Select(SetOwner)
                .ToList();
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
            return SetOwner(Repository.CreatePageAsync(
                DataSource,
                name,
                text,
                dir,
                CancellationToken.None).GetAwaiter().GetResult());
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
                DataSource,
                newPage,
                CancellationToken.None).GetAwaiter().GetResult();
            newPage.Rowid = persistedPage.Rowid;
            newPage.Index = persistedPage.Index;
            newPage.UpdateTime = persistedPage.UpdateTime;
            SetOwner(newPage);
        }

        /// <summary>
        /// Deletes a specific page from the database based on the provided content object.
        /// The page with the corresponding identifier is removed and the remaining indexes
        /// for its exact-name group are compacted in the same transaction.
        /// </summary>
        /// <param name="content">The content object representing the page to delete.</param>
        public void DeletePage(IContent content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            DeletePage(content.Guid);
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
                DataSource,
                guid,
                CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously searches this note using the specified search method.
        /// </summary>
        /// <param name="searchEntry">The search entry to match.</param>
        /// <param name="searchMethod">The search method to use.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The matching contents and total count.</returns>
        public Task<SearchResult> SearchAsync(
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            return SearchAsync(searchEntry, searchMethod, 0, int.MaxValue, token);
        }

        /// <summary>
        /// Asynchronously searches this note using the specified search method and paging values.
        /// </summary>
        /// <param name="searchEntry">The search entry to match.</param>
        /// <param name="searchMethod">The search method to use.</param>
        /// <param name="skipCount">The number of matching contents to skip.</param>
        /// <param name="takeCount">The maximum number of matching contents to return.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The matching contents and total count.</returns>
        public async Task<SearchResult> SearchAsync(
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            var result = await SearchRepository.SearchAsync(
                DataSource,
                searchEntry,
                searchMethod,
                skipCount,
                takeCount,
                token);
            result.Contents.ForEach(content => SetOwner(content));
            return result;
        }

        internal Task<int> CountSearchResultsAsync(
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            return SearchRepository.CountAsync(
                DataSource,
                searchEntry,
                searchMethod,
                token);
        }

        INoteSearchRepository SearchRepository =>
            _searchRepository ?? DefaultSearchRepository.Instance;

        INoteRepository Repository => _repository ?? DefaultRepository.Instance;

        INoteMetadataRepository MetadataRepository =>
            _metadataRepository ?? DefaultMetadataRepository.Instance;

        static class DefaultRepository
        {
            internal static readonly INoteRepository Instance =
                new SqliteNoteRepository(
                    new SqliteNoteDatabaseFactory(NoteDbContext.MyLoggerFactory));
        }

        static class DefaultSearchRepository
        {
            internal static readonly INoteSearchRepository Instance =
                new SqliteNoteSearchRepository(
                    new SqliteNoteDatabaseFactory(NoteDbContext.MyLoggerFactory));
        }

        static class DefaultMetadataRepository
        {
            internal static readonly INoteMetadataRepository Instance =
                new SqliteNoteMetadataRepository(
                    new SqliteNoteDatabaseFactory(NoteDbContext.MyLoggerFactory));
        }

        /// <summary>
        /// Gets the count of contents in the note repository.
        /// </summary>
        /// <returns>An integer representing the total count of contents in the database.</returns>
        public int Count
        {
            get => Repository.CountAsync(DataSource, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Retrieves a list of content items from the note repository using the provided paging values.
        /// </summary>
        /// <param name="skipCount">The number of content items to skip before retrieving data.</param>
        /// <param name="takeCount">The maximum number of content items to retrieve from the database.</param>
        /// <returns>A list of Content objects representing the retrieved content items.</returns>
        public List<Content> GetContents(int skipCount, int takeCount)
        {
            return Repository.ReadContentsAsync(
                    DataSource,
                    skipCount,
                    takeCount,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult()
                .Select(SetOwner)
                .ToList();
        }

        private T SetOwner<T>(T content) where T : class, IContent
        {
            if (content != null)
            {
                content.OwnerDataSource = Path.GetFullPath(DataSource);
                content.Parent = this;
            }

            return content;
        }

        /// <summary>
        /// Gets or sets the data source for note operations.
        /// Setting this property clears the currently loaded metadata snapshot without accessing the database.
        /// </summary>
        public string DataSource
        {
            get => _dataSource;
            set
            {
                _dataSource = value;
                Metadata = null;
                MetadataIssues = Array.Empty<MetadataLoadIssue>();
            }
        }

        /// <summary>
        /// Gets the most recently loaded or persisted metadata snapshot.
        /// </summary>
        public NoteMetadata Metadata { get; private set; }

        /// <summary>
        /// Gets classifiable problems found while producing the current metadata snapshot.
        /// </summary>
        public IReadOnlyList<MetadataLoadIssue> MetadataIssues { get; private set; } =
            Array.Empty<MetadataLoadIssue>();

        /// <summary>
        /// Reloads the metadata snapshot from the note database.
        /// </summary>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The loaded snapshot and any classifiable value problems.</returns>
        public async Task<MetadataLoadResult> ReloadMetadataAsync(CancellationToken token)
        {
            var result = await MetadataRepository
                .LoadAsync(DataSource, token)
                .ConfigureAwait(false);
            ApplyMetadata(result);
            return result;
        }

        /// <summary>
        /// Persists requested metadata fields atomically and replaces the current snapshot.
        /// </summary>
        /// <param name="update">The metadata fields to update together.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The saved snapshot and any classifiable value problems.</returns>
        public async Task<MetadataLoadResult> UpdateMetadataAsync(
            NoteMetadataUpdate update,
            CancellationToken token)
        {
            var result = await MetadataRepository
                .UpdateAsync(DataSource, update, token)
                .ConfigureAwait(false);
            ApplyMetadata(result);
            return result;
        }

        /// <summary>
        /// Persists requested metadata fields atomically through the synchronous compatibility API.
        /// </summary>
        /// <param name="update">The metadata fields to update together.</param>
        /// <returns>The saved snapshot and any classifiable value problems.</returns>
        public MetadataLoadResult UpdateMetadata(NoteMetadataUpdate update)
        {
            return UpdateMetadataAsync(update, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        void InitializeDataSource(string dataSource)
        {
            DataSource = dataSource;
            if (dataSource == null || !File.Exists(dataSource))
                return;

            var result = MetadataRepository.LoadAsync(dataSource, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ApplyMetadata(result);
        }

        void ApplyMetadata(MetadataLoadResult result)
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
