using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote
{
    /// <summary>
    /// Manages note database creation and migration for SQLite data sources.
    /// </summary>
    public sealed class SqliteNoteMigrator : INoteMigrator
    {
        readonly INoteDatabaseFactory _databaseFactory;
        readonly INoteMetadataRepository _metadataRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteNoteMigrator"/> class.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        /// <param name="metadataRepository">The repository used to persist metadata.</param>
        public SqliteNoteMigrator(
            INoteDatabaseFactory databaseFactory,
            INoteMetadataRepository metadataRepository)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
        }

        /// <inheritdoc/>
        public async Task<Note> CreateAsync(
            string name,
            string title,
            string dataSource,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using var context = _databaseFactory.Create(dataSource);
            var normalizedDataSource = context.DataSource;
            if (File.Exists(normalizedDataSource))
                throw new ArgumentException("File exists");

            var ownsDatabase = false;
            try
            {
                try
                {
                    using var reservation = new FileStream(
                        normalizedDataSource,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);
                    ownsDatabase = true;
                }
                catch (IOException) when (File.Exists(normalizedDataSource))
                {
                    throw new ArgumentException("File exists");
                }

                await context.Database.MigrateAsync(token).ConfigureAwait(false);
                var metadata = await _metadataRepository.UpdateAsync(
                        normalizedDataSource,
                        new NoteMetadataUpdate()
                            .SetName(name)
                            .SetTitle(title)
                            .SetVersion(NoteDbContext.CurrentVersion),
                        token)
                    .ConfigureAwait(false);

                return new Note(normalizedDataSource, _metadataRepository, metadata);
            }
            catch
            {
                context.Dispose();
                if (ownsDatabase)
                    DeleteCreatedDatabase(normalizedDataSource);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task MigrateAsync(string dataSource, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using (var context = _databaseFactory.Create(dataSource))
            {
                if (!File.Exists(context.DataSource))
                    throw new ArgumentException("File does not exists");

                await context.Database.MigrateAsync(token).ConfigureAwait(false);
                dataSource = context.DataSource;
            }

            await _metadataRepository.UpdateAsync(
                    dataSource,
                    new NoteMetadataUpdate().SetVersion(NoteDbContext.CurrentVersion),
                    token)
                .ConfigureAwait(false);
        }

        static void DeleteCreatedDatabase(string dataSource)
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dataSource);
            DeleteIfExists(dataSource + "-journal");
            DeleteIfExists(dataSource + "-shm");
            DeleteIfExists(dataSource + "-wal");
        }

        static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
