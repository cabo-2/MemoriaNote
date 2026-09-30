using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence;

namespace MemoriaNote.Archive
{
    /// <summary>Restores validated archive v1 inputs to new current-format notebooks.</summary>
    public sealed class ArchiveV1RestoreService
    {
        readonly SqliteArchiveV1PersistenceAdapter _persistence;
        readonly ITemporaryFileStore _temporaryFileStore;
        readonly ArchiveV1Reader _reader = new ArchiveV1Reader();

        /// <summary>Initializes the archive v1 restore service.</summary>
        public ArchiveV1RestoreService(
            INotebookDbContextFactory databaseFactory,
            ITemporaryFileStore temporaryFileStore)
        {
            _persistence = new SqliteArchiveV1PersistenceAdapter(
                databaseFactory ?? throw new ArgumentNullException(nameof(databaseFactory)));
            _temporaryFileStore = temporaryFileStore ??
                throw new ArgumentNullException(nameof(temporaryFileStore));
        }

        /// <summary>Restores an archive file to a new notebook without replacement.</summary>
        public async Task<ArchiveV1RestoreResult> RestoreAsync(
            ArchiveV1FileRestoreRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = Path.GetFullPath(request.DestinationDatabasePath);
            if (File.Exists(destinationPath))
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.DestinationConflict);
            }

            var sourcePath = Path.GetFullPath(request.SourceArchivePath);
            if (!File.Exists(sourcePath))
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.InputNotFound);
            }

            try
            {
                await using var input = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await RestoreSeekableAsync(
                        input,
                        destinationPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.InputNotFound);
            }
            catch (UnauthorizedAccessException)
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
            catch (IOException)
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
        }

        /// <summary>
        /// Spools a caller-owned stream with a size limit, then restores a new notebook.
        /// </summary>
        public async Task<ArchiveV1RestoreResult> RestoreAsync(
            ArchiveV1StreamRestoreRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!request.Source.CanRead)
            {
                throw new ArgumentException(
                    "The source stream must be readable.",
                    nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = Path.GetFullPath(request.DestinationDatabasePath);
            if (File.Exists(destinationPath))
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.DestinationConflict);
            }

            try
            {
                using var spool = _temporaryFileStore.CreateFile("archive-v1-input.zip");
                var sizeIssue = await SpoolAsync(
                        request.Source,
                        spool.Path,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (sizeIssue != null)
                {
                    return ArchiveV1RestoreResult.Failed(
                        ArchiveV1OperationErrorCode.ArchiveValidationFailed,
                        sizeIssue);
                }

                await using var input = new FileStream(
                    spool.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await RestoreSeekableAsync(
                        input,
                        destinationPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
            catch (IOException)
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
        }

        async Task<ArchiveV1RestoreResult> RestoreSeekableAsync(
            Stream archive,
            string destinationDatabasePath,
            CancellationToken cancellationToken)
        {
            var destinationPath = Path.GetFullPath(destinationDatabasePath);
            if (File.Exists(destinationPath))
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.DestinationConflict);
            }

            var preflight = await _reader.ValidateAsync(archive, cancellationToken)
                .ConfigureAwait(false);
            if (!preflight.IsValid)
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.ArchiveValidationFailed,
                    preflight);
            }

            try
            {
                using var temporary = ArchiveV1TemporaryOutput.CreateBeside(
                    destinationPath,
                    database: true);
                await _persistence.CreateCurrentDatabaseAsync(
                        temporary.Path,
                        cancellationToken)
                    .ConfigureAwait(false);
                var written = await _persistence.RestoreAsync(
                        temporary.Path,
                        archive,
                        _reader,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!written.ValidationReport.IsValid)
                {
                    return ArchiveV1RestoreResult.Failed(
                        ArchiveV1OperationErrorCode.ArchiveValidationFailed,
                        written.ValidationReport);
                }
                if (written.Summary == null ||
                    !await _persistence.VerifyAsync(
                            temporary.Path,
                            written.Summary,
                            cancellationToken)
                        .ConfigureAwait(false))
                {
                    return ArchiveV1RestoreResult.Failed(
                        ArchiveV1OperationErrorCode.IntegrityFailure);
                }

                SqliteArchiveV1PersistenceAdapter.FlushToDisk(temporary.Path);
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Move(temporary.Path, destinationPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(destinationPath))
                {
                    return ArchiveV1RestoreResult.Failed(
                        ArchiveV1OperationErrorCode.DestinationConflict);
                }

                return ArchiveV1RestoreResult.Succeeded(
                    written.Summary.MetadataCount,
                    written.Summary.PageCount);
            }
            catch (InvalidDataException)
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.IntegrityFailure);
            }
            catch (Microsoft.Data.Sqlite.SqliteException exception) when (
                IsIoFailure(exception))
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.IntegrityFailure);
            }
            catch (UnauthorizedAccessException)
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
            catch (IOException)
            {
                return ArchiveV1RestoreResult.Failed(
                    ArchiveV1OperationErrorCode.IoFailure);
            }
        }

        static async Task<ArchiveV1ValidationReport> SpoolAsync(
            Stream source,
            string spoolPath,
            CancellationToken cancellationToken)
        {
            await using var output = new FileStream(
                spoolPath,
                FileMode.Truncate,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    return null;

                total += read;
                if (total > ArchiveV1Format.MaximumArchiveLength)
                {
                    return new ArchiveV1ValidationReport(
                        ArchiveV1Classification.MalformedZip,
                        new[]
                        {
                            new ArchiveV1ValidationIssue(
                                ArchiveV1IssueCode.ArchiveTooLarge,
                                "$archive",
                                ArchiveV1Format.MaximumArchiveLength.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture),
                                total.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture))
                        });
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        static bool IsIoFailure(Microsoft.Data.Sqlite.SqliteException exception)
        {
            return exception.SqliteErrorCode == 8 ||
                exception.SqliteErrorCode == 10 ||
                exception.SqliteErrorCode == 13 ||
                exception.SqliteErrorCode == 14;
        }
    }
}
