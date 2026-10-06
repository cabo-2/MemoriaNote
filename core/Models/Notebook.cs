using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MemoriaNote;
using MemoriaNote.Persistence;

namespace MemoriaNote.Models
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
                    new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
        }

        static class DefaultSearchRepository
        {
            internal static readonly IPageSearchRepository Instance =
                new SqlitePageSearchRepository(
                    new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
        }

        static class DefaultMetadataRepository
        {
            internal static readonly INotebookMetadataRepository Instance =
                new SqliteNotebookMetadataRepository(
                    new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
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
