using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence;

namespace MemoriaNote.Archive
{
    /// <summary>Creates validated archive v1 backups without replacing existing files.</summary>
    public sealed class ArchiveV1BackupService
    {
        readonly SqliteArchiveV1PersistenceAdapter _persistence;
        readonly ITemporaryFileStore _temporaryFileStore;
        readonly IClock _clock;
        readonly ArchiveV1Creator _creator;
        readonly ArchiveV1Writer _writer = new ArchiveV1Writer();
        readonly ArchiveV1Reader _reader = new ArchiveV1Reader();

        /// <summary>Initializes the archive v1 backup service.</summary>
        public ArchiveV1BackupService(
            INotebookDbContextFactory databaseFactory,
            ITemporaryFileStore temporaryFileStore,
            IClock clock,
            ArchiveV1Creator creator)
        {
            _persistence = new SqliteArchiveV1PersistenceAdapter(
                databaseFactory ?? throw new ArgumentNullException(nameof(databaseFactory)));
            _temporaryFileStore = temporaryFileStore ??
                throw new ArgumentNullException(nameof(temporaryFileStore));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _creator = creator ?? throw new ArgumentNullException(nameof(creator));
        }

        /// <summary>Creates and atomically publishes a new archive file.</summary>
        public async Task<ArchiveV1BackupResult> BackupAsync(
            ArchiveV1FileBackupRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = Path.GetFullPath(request.DestinationArchivePath);
            if (File.Exists(destinationPath))
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.DestinationConflict);
            }

            try
            {
                using var temporary = ArchiveV1TemporaryOutput.CreateBeside(
                    destinationPath,
                    database: false);
                var created = await CreateValidatedArchiveAsync(
                        request.SourceDatabasePath,
                        temporary.Path,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!created.IsSuccess)
                    return created.Result;

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Move(temporary.Path, destinationPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(destinationPath))
                {
                    return ArchiveV1BackupResult.Failed(
                        ArchiveV1OperationErrorCode.DestinationConflict);
                }

                return created.Result;
            }
            catch (FileNotFoundException)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.InputNotFound);
            }
            catch (ArchiveV1ValidationException exception)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.SourceNotebookInvalid,
                    exception.Report);
            }
            catch (InvalidDataException)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.SourceNotebookInvalid);
            }
            catch (Microsoft.Data.Sqlite.SqliteException exception) when (
                IsInvalidNotebook(exception))
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.SourceNotebookInvalid);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
            catch (UnauthorizedAccessException)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
            catch (IOException)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
        }

        /// <summary>
        /// Creates a validated archive and then copies it to a caller-owned stream.
        /// </summary>
        public async Task<ArchiveV1BackupResult> BackupAsync(
            ArchiveV1StreamBackupRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!request.Destination.CanWrite)
            {
                throw new ArgumentException(
                    "The destination stream must be writable.",
                    nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var temporary = _temporaryFileStore.CreateFile("archive-v1.zip");
                var created = await CreateValidatedArchiveAsync(
                        request.SourceDatabasePath,
                        temporary.Path,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!created.IsSuccess)
                    return created.Result;

                await using var input = new FileStream(
                    temporary.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(request.Destination, 81920, cancellationToken)
                    .ConfigureAwait(false);
                return created.Result;
            }
            catch (FileNotFoundException)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.InputNotFound);
            }
            catch (ArchiveV1ValidationException exception)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.SourceNotebookInvalid,
                    exception.Report);
            }
            catch (InvalidDataException)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.SourceNotebookInvalid);
            }
            catch (Microsoft.Data.Sqlite.SqliteException exception) when (
                IsInvalidNotebook(exception))
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.SourceNotebookInvalid);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
            catch (UnauthorizedAccessException)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
            catch (IOException)
            {
                return ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
        }

        async Task<CreatedArchive> CreateValidatedArchiveAsync(
            string sourceDatabasePath,
            string temporaryArchivePath,
            CancellationToken cancellationToken)
        {
            await using var snapshot = await _persistence
                .OpenSnapshotAsync(sourceDatabasePath, cancellationToken)
                .ConfigureAwait(false);
            await using (var output = new FileStream(
                temporaryArchivePath,
                FileMode.Truncate,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                var writeRequest = new ArchiveV1WriteRequest(
                    snapshot.SourceNotebookFormatVersion,
                    _creator,
                    _clock.UtcNow,
                    snapshot.Metadata,
                    snapshot.ReadPagesAsync(cancellationToken));
                await _writer.WriteAsync(output, writeRequest, cancellationToken)
                    .ConfigureAwait(false);
            }

            ArchiveV1ValidationReport report;
            await using (var input = new FileStream(
                temporaryArchivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                report = await _reader.ValidateAsync(input, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (!report.IsValid)
            {
                return CreatedArchive.Failed(ArchiveV1BackupResult.Failed(
                    ArchiveV1OperationErrorCode.IntegrityFailure,
                    report));
            }

            return CreatedArchive.Succeeded(ArchiveV1BackupResult.Succeeded(
                snapshot.Metadata.Count,
                snapshot.PageCount));
        }

        static bool IsInvalidNotebook(Microsoft.Data.Sqlite.SqliteException exception)
        {
            return exception.SqliteErrorCode == 1 ||
                exception.SqliteErrorCode == 11 ||
                exception.SqliteErrorCode == 26;
        }

        sealed class CreatedArchive
        {
            CreatedArchive(bool isSuccess, ArchiveV1BackupResult result)
            {
                IsSuccess = isSuccess;
                Result = result;
            }

            internal bool IsSuccess { get; }

            internal ArchiveV1BackupResult Result { get; }

            internal static CreatedArchive Succeeded(ArchiveV1BackupResult result)
            {
                return new CreatedArchive(true, result);
            }

            internal static CreatedArchive Failed(ArchiveV1BackupResult result)
            {
                return new CreatedArchive(false, result);
            }
        }
    }
}
