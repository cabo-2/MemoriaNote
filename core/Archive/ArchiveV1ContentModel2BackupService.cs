using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Archive;

/// <summary>Creates validated CM2 backups from Schema V2 notebooks.</summary>
public sealed class ArchiveV1ContentModel2BackupService : IArchiveV1BackupService
{
    private readonly ITemporaryFileStore _temporaryFileStore;
    private readonly IClock _clock;
    private readonly ArchiveV1Creator _creator;
    private readonly ArchiveV1ContentModel2Writer _writer = new();
    private readonly ArchiveV1ContentModel2Reader _reader = new();

    /// <summary>Initializes the Schema V2 to CM2 backup service.</summary>
    public ArchiveV1ContentModel2BackupService(
        ITemporaryFileStore temporaryFileStore,
        IClock clock,
        ArchiveV1Creator creator)
    {
        _temporaryFileStore = temporaryFileStore ?? throw new ArgumentNullException(nameof(temporaryFileStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _creator = creator ?? throw new ArgumentNullException(nameof(creator));
    }

    /// <inheritdoc />
    public async Task<ArchiveV1BackupResult> BackupAsync(
        ArchiveV1FileBackupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = Path.GetFullPath(request.SourceDatabasePath);
        var destinationPath = Path.GetFullPath(request.DestinationArchivePath);
        if (PathsEqual(sourcePath, destinationPath) || DestinationExists(destinationPath))
            return ArchiveV1BackupResult.Failed(ArchiveV1OperationErrorCode.DestinationConflict);

        try
        {
            using var temporary = ArchiveV1TemporaryOutput.CreateBeside(destinationPath, database: false);
            var result = await CreateValidatedArchiveAsync(sourcePath, temporary.Path, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
                return result;
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary.Path, destinationPath, overwrite: false);
            return result;
        }
        catch (FileNotFoundException)
        {
            return ArchiveV1BackupResult.Failed(ArchiveV1OperationErrorCode.InputNotFound);
        }
        catch (ArchiveV1ValidationException exception)
        {
            return ArchiveV1BackupResult.Failed(
                ArchiveV1OperationErrorCode.SourceNotebookInvalid,
                exception.Report);
        }
        catch (InvalidDataException)
        {
            return ArchiveV1BackupResult.Failed(ArchiveV1OperationErrorCode.SourceNotebookInvalid);
        }
        catch (SqliteException)
        {
            return ArchiveV1BackupResult.Failed(ArchiveV1OperationErrorCode.SourceNotebookInvalid);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ArchiveV1BackupResult.Failed(
                DestinationExists(destinationPath)
                    ? ArchiveV1OperationErrorCode.DestinationConflict
                    : ArchiveV1OperationErrorCode.IoFailure);
        }
    }

    /// <inheritdoc />
    public async Task<ArchiveV1BackupResult> BackupAsync(
        ArchiveV1StreamBackupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Destination.CanWrite)
            throw new ArgumentException("The archive destination must be writable.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var temporary = _temporaryFileStore.CreateFile("archive-v1-cm2.zip");
            var result = await CreateValidatedArchiveAsync(
                    request.SourceDatabasePath,
                    temporary.Path,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
                return result;
            await using var input = new FileStream(
                temporary.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(request.Destination, 81920, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (FileNotFoundException)
        {
            return ArchiveV1BackupResult.Failed(ArchiveV1OperationErrorCode.InputNotFound);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArchiveV1ValidationException or SqliteException)
        {
            return ArchiveV1BackupResult.Failed(ArchiveV1OperationErrorCode.SourceNotebookInvalid);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ArchiveV1BackupResult.Failed(ArchiveV1OperationErrorCode.IoFailure);
        }
    }

    private async Task<ArchiveV1BackupResult> CreateValidatedArchiveAsync(
        string sourcePath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        await using var snapshot = await SchemaV2ArchiveSnapshot.OpenAsync(sourcePath, cancellationToken)
            .ConfigureAwait(false);
        await using (var output = new FileStream(
            temporaryPath,
            FileMode.Truncate,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous))
        {
            await _writer.WriteAsync(
                    output,
                    new ArchiveV1ContentModel2WriteRequest(
                        _creator,
                        _clock.UtcNow,
                        snapshot.Metadata,
                        snapshot.ReadPagesAsync(cancellationToken)),
                    cancellationToken)
                .ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            if (output.Length > ArchiveV1Format.MaximumArchiveLength)
                return ArchiveV1BackupResult.Failed(ArchiveV1OperationErrorCode.IntegrityFailure);
        }

        ArchiveV1ValidationReport report;
        await using (var input = new FileStream(
            temporaryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            report = await _reader.ValidateAsync(input, cancellationToken).ConfigureAwait(false);
        }
        return report.IsValid
            ? ArchiveV1BackupResult.Succeeded(snapshot.Metadata.Count, snapshot.PageCount)
            : ArchiveV1BackupResult.Failed(ArchiveV1OperationErrorCode.IntegrityFailure, report);
    }

    private static bool DestinationExists(string path) =>
        File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
