using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Archive;

/// <summary>Restores validated CM2 archives to new Schema V2 notebooks.</summary>
public sealed class ArchiveV1ContentModel2RestoreService : IArchiveV1RestoreService
{
    private readonly ITemporaryFileStore _temporaryFileStore;
    private readonly ArchiveV1ContentModel2Reader _reader = new();

    /// <summary>Initializes the CM2 to Schema V2 restore service.</summary>
    public ArchiveV1ContentModel2RestoreService(ITemporaryFileStore temporaryFileStore)
    {
        _temporaryFileStore = temporaryFileStore ?? throw new ArgumentNullException(nameof(temporaryFileStore));
    }

    /// <inheritdoc />
    public async Task<ArchiveV1RestoreResult> RestoreAsync(
        ArchiveV1FileRestoreRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = Path.GetFullPath(request.SourceArchivePath);
        var destinationPath = Path.GetFullPath(request.DestinationDatabasePath);
        if (PathsEqual(sourcePath, destinationPath) || DestinationExists(destinationPath))
            return ArchiveV1RestoreResult.Failed(ArchiveV1OperationErrorCode.DestinationConflict);
        if (!File.Exists(sourcePath))
            return ArchiveV1RestoreResult.Failed(ArchiveV1OperationErrorCode.InputNotFound);

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
                    request.DryRun,
                    new[] { sourcePath },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return ArchiveV1RestoreResult.Failed(ArchiveV1OperationErrorCode.InputNotFound);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ArchiveV1RestoreResult.Failed(ArchiveV1OperationErrorCode.IoFailure);
        }
    }

    /// <inheritdoc />
    public async Task<ArchiveV1RestoreResult> RestoreAsync(
        ArchiveV1StreamRestoreRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Source.CanRead)
            throw new ArgumentException("The archive source must be readable.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        var destinationPath = Path.GetFullPath(request.DestinationDatabasePath);
        if (DestinationExists(destinationPath))
            return ArchiveV1RestoreResult.Failed(ArchiveV1OperationErrorCode.DestinationConflict);

        try
        {
            using var spool = _temporaryFileStore.CreateFile("archive-v1-cm2-input.zip");
            var sizeIssue = await SpoolAsync(request.Source, spool.Path, cancellationToken)
                .ConfigureAwait(false);
            if (sizeIssue is not null)
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
                    request.DryRun,
                    Array.Empty<string>(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ArchiveV1RestoreResult.Failed(ArchiveV1OperationErrorCode.IoFailure);
        }
    }

    private async Task<ArchiveV1RestoreResult> RestoreSeekableAsync(
        Stream archive,
        string destinationPath,
        bool dryRun,
        string[] sourcePaths,
        CancellationToken cancellationToken)
    {
        var preflight = await _reader.ValidateAsync(archive, cancellationToken).ConfigureAwait(false);
        if (!preflight.IsValid)
        {
            return ArchiveV1RestoreResult.Failed(
                ArchiveV1OperationErrorCode.ArchiveValidationFailed,
                preflight);
        }

        try
        {
            var bridge = await ArchiveV1ContentModel2TransferBridge.PrepareAsync(
                    archive,
                    _reader,
                    cancellationToken)
                .ConfigureAwait(false);
            if (dryRun)
                return ArchiveV1RestoreResult.Succeeded(bridge.MetadataCount, bridge.PageCount);

            var result = await SchemaV2NotebookSink.PublishAsync(
                    destinationPath,
                    bridge.CreateTransfer(cancellationToken),
                    sourcePaths,
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.Fingerprint.MetadataCount != bridge.MetadataCount ||
                result.Fingerprint.PageCount != bridge.PageCount)
            {
                return ArchiveV1RestoreResult.Failed(ArchiveV1OperationErrorCode.IntegrityFailure);
            }
            return ArchiveV1RestoreResult.Succeeded(bridge.MetadataCount, bridge.PageCount);
        }
        catch (InvalidDataException)
        {
            return ArchiveV1RestoreResult.Failed(ArchiveV1OperationErrorCode.IntegrityFailure);
        }
        catch (SqliteException)
        {
            return ArchiveV1RestoreResult.Failed(ArchiveV1OperationErrorCode.IntegrityFailure);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ArchiveV1RestoreResult.Failed(
                DestinationExists(destinationPath)
                    ? ArchiveV1OperationErrorCode.DestinationConflict
                    : ArchiveV1OperationErrorCode.IoFailure);
        }
    }

    private static async Task<ArchiveV1ValidationReport> SpoolAsync(
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
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
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
                            ArchiveV1Format.MaximumArchiveLength.ToString(CultureInfo.InvariantCulture),
                            total.ToString(CultureInfo.InvariantCulture))
                    });
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool DestinationExists(string path) =>
        File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
