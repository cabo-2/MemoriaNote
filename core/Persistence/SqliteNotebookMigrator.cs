using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MemoriaNote.Models;

namespace MemoriaNote.Persistence
{
    /// <summary>
    /// Manages notebook database creation and migration for SQLite data sources.
    /// </summary>
    public sealed class SqliteNotebookMigrator : INotebookMigrator
    {
        readonly INotebookDbContextFactory _databaseFactory;
        readonly INotebookMetadataRepository _metadataRepository;
        readonly INotebookFormatValidator _formatValidator;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteNotebookMigrator"/> class.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        /// <param name="metadataRepository">The repository used to persist metadata.</param>
        public SqliteNotebookMigrator(
            INotebookDbContextFactory databaseFactory,
            INotebookMetadataRepository metadataRepository)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
            _formatValidator = new SqliteNotebookFormatValidator(
                _databaseFactory,
                _metadataRepository);
        }

        /// <inheritdoc/>
        public async Task<Notebook> CreateAsync(
            string name,
            string title,
            string databasePath,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using var context = _databaseFactory.CreateDbContext(databasePath);
            var normalizedDatabasePath = context.DatabasePath;
            if (File.Exists(normalizedDatabasePath))
                throw new ArgumentException("File exists");

            var ownsDatabase = false;
            try
            {
                try
                {
                    using var reservation = new FileStream(
                        normalizedDatabasePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);
                    ownsDatabase = true;
                }
                catch (IOException) when (File.Exists(normalizedDatabasePath))
                {
                    throw new ArgumentException("File exists");
                }

                await context.Database.MigrateAsync(token).ConfigureAwait(false);
                await _metadataRepository.UpdateAsync(
                        normalizedDatabasePath,
                        new NotebookMetadataPatch()
                            .SetName(name)
                            .SetTitle(title)
                            .SetVersion(NotebookDbContext.CurrentVersion),
                        token)
                    .ConfigureAwait(false);

                var reopened = await _formatValidator
                    .ValidateCurrentAsync(normalizedDatabasePath, token)
                    .ConfigureAwait(false);
                ValidateCreatedNotebookMetadata(name, title, reopened);

                return new Notebook(
                    normalizedDatabasePath,
                    _metadataRepository,
                    reopened);
            }
            catch
            {
                context.Dispose();
                if (ownsDatabase)
                    DeleteCreatedDatabase(normalizedDatabasePath);
                throw;
            }
        }

        static void ValidateCreatedNotebookMetadata(
            string expectedName,
            string expectedTitle,
            NotebookMetadataResult reopened)
        {
            if (reopened == null ||
                reopened.Metadata.Name != expectedName ||
                reopened.Metadata.Title != expectedTitle)
            {
                throw new InvalidDataException(
                    "The created file could not be verified as a current Memoria Note notebook.");
            }
        }

        /// <inheritdoc/>
        public async Task MigrateAsync(string databasePath, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using (var context = _databaseFactory.CreateDbContext(databasePath))
            {
                if (!File.Exists(context.DatabasePath))
                    throw new ArgumentException("File does not exists");

                await context.Database.MigrateAsync(token).ConfigureAwait(false);
                databasePath = context.DatabasePath;
            }

            await _metadataRepository.UpdateAsync(
                    databasePath,
                    new NotebookMetadataPatch().SetVersion(NotebookDbContext.CurrentVersion),
                    token)
                .ConfigureAwait(false);
        }

        static void DeleteCreatedDatabase(string databasePath)
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(databasePath);
            DeleteIfExists(databasePath + "-journal");
            DeleteIfExists(databasePath + "-shm");
            DeleteIfExists(databasePath + "-wal");
        }

        static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
