using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MemoriaNote.Domain;
using MemoriaNote.Models;
using MemoriaNote.Persistence;

namespace MemoriaNote.Transfer
{
    /// <summary>
    /// Creates and restores notebook backup archives.
    /// </summary>
    public sealed class NotebookBackupService
    {
        readonly INotebookTransferRepository _transferRepository;
        readonly INotebookMetadataRepository _metadataRepository;
        readonly INotebookMigrator _notebookMigrator;
        readonly NotebookFilePathFactory _pathFactory;
        readonly NotebookArchiveCodec _archiveCodec;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotebookBackupService"/> class.
        /// </summary>
        /// <param name="transferRepository">The repository used for bulk page transfers.</param>
        /// <param name="metadataRepository">The repository used for notebook metadata.</param>
        /// <param name="notebookMigrator">The service used for database lifecycle operations.</param>
        /// <param name="pathFactory">The factory used to select restored database paths.</param>
        public NotebookBackupService(
            INotebookTransferRepository transferRepository,
            INotebookMetadataRepository metadataRepository,
            INotebookMigrator notebookMigrator,
            NotebookFilePathFactory pathFactory)
        {
            _transferRepository = transferRepository ??
                throw new ArgumentNullException(nameof(transferRepository));
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
            _notebookMigrator = notebookMigrator ??
                throw new ArgumentNullException(nameof(notebookMigrator));
            _pathFactory = pathFactory ?? throw new ArgumentNullException(nameof(pathFactory));
            _archiveCodec = new NotebookArchiveCodec();
        }

        /// <summary>
        /// Creates a backup archive without replacing an existing file.
        /// </summary>
        /// <param name="notebookId">The notebook to back up.</param>
        /// <param name="outputPath">The archive path to create.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>A task representing the backup operation.</returns>
        public async Task CreateBackupAsync(
            NotebookId notebookId,
            string outputPath,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (outputPath == null)
                throw new ArgumentNullException(nameof(outputPath));

            token.ThrowIfCancellationRequested();
            var metadataResult = await _metadataRepository
                .LoadAsync(notebookId.Locator, token)
                .ConfigureAwait(false);
            var metadata = metadataResult.Metadata.StoredValues
                .Select(value => new NoteKeyValue
                {
                    Key = value.Key,
                    Value = value.Value
                })
                .ToList();
            var pages = await _transferRepository.ListPagesAsync(notebookId, token)
                .ConfigureAwait(false);

            var ownsOutput = false;
            try
            {
                await using var output = new FileStream(
                    outputPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous);
                ownsOutput = true;
                await _archiveCodec.WriteAsync(output, metadata, pages, token)
                    .ConfigureAwait(false);
            }
            catch
            {
                if (ownsOutput)
                    DeleteIncompleteArchive(outputPath);
                throw;
            }
        }

        /// <summary>
        /// Restores a backup archive into a newly created notebook database.
        /// </summary>
        /// <param name="inputPath">The backup archive to restore.</param>
        /// <param name="outputDirectory">The directory for the restored database.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>The restored notebook.</returns>
        public async Task<Notebook> RestoreBackupAsync(
            string inputPath,
            string outputDirectory,
            CancellationToken token)
        {
            if (inputPath == null)
                throw new ArgumentNullException(nameof(inputPath));
            if (outputDirectory == null)
                throw new ArgumentNullException(nameof(outputDirectory));

            token.ThrowIfCancellationRequested();
            NotebookArchive archive;
            await using (var input = new FileStream(
                inputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                archive = await _archiveCodec.ReadAsync(input, token).ConfigureAwait(false);
            }

            var name = GetRequiredMetadataValue(archive.Metadata, NoteKeyValue.Name);
            var title = GetRequiredMetadataValue(archive.Metadata, NoteKeyValue.Title);
            var databasePath = _pathFactory.CreateDatabasePath(outputDirectory, name);
            var ownsDatabase = false;
            try
            {
                var notebook = await _notebookMigrator.CreateAsync(
                        name,
                        title,
                        databasePath,
                        token)
                    .ConfigureAwait(false);
                ownsDatabase = true;

                var patch = new NotebookMetadataPatch();
                var description = GetOptionalMetadataValue(
                    archive.Metadata,
                    NoteKeyValue.Description);
                var author = GetOptionalMetadataValue(archive.Metadata, NoteKeyValue.Author);
                if (description != null)
                    patch.SetDescription(description);
                if (author != null)
                    patch.SetAuthor(author);
                await notebook.UpdateMetadataAsync(patch, token).ConfigureAwait(false);

                var notebookId = NotebookId.FromDatabasePath(databasePath);
                await _transferRepository.AddPagesAsync(notebookId, archive.Pages, token)
                    .ConfigureAwait(false);
                await _notebookMigrator.MigrateAsync(databasePath, token)
                    .ConfigureAwait(false);
                return notebook;
            }
            catch
            {
                if (ownsDatabase)
                    DeleteCreatedDatabase(databasePath);
                throw;
            }
        }

        static string GetRequiredMetadataValue(
            IEnumerable<NoteKeyValue> metadata,
            string key)
        {
            var value = metadata.FirstOrDefault(entry => entry?.Key == key)?.Value;
            if (value == null)
                throw new InvalidDataException($"The backup metadata has no '{key}' value.");
            return value;
        }

        static string GetOptionalMetadataValue(
            IEnumerable<NoteKeyValue> metadata,
            string key)
        {
            return metadata.FirstOrDefault(entry => entry?.Key == key)?.Value;
        }

        static void DeleteIncompleteArchive(string outputPath)
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
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
