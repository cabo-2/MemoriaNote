using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.NotebookMigration;

/// <summary>Migrates schema v1 notebooks and CM1 archives through the common schema v2 sink.</summary>
public sealed class NotebookMigrationService : INotebookMigrationService
{
    /// <inheritdoc />
    public async Task<NotebookMigrationResult> MigrateAsync(
        NotebookMigrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string sourcePath;
        string destinationPath;
        string resolutionPath;
        try
        {
            sourcePath = Path.GetFullPath(request.SourcePath);
            destinationPath = Path.GetFullPath(request.DestinationPath);
            resolutionPath = request.ResolutionPath is null
                ? null
                : Path.GetFullPath(request.ResolutionPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return NotebookMigrationResult.Failed(NotebookMigrationErrorCode.InvalidSource);
        }

        if (PathsEqual(sourcePath, destinationPath) ||
            (resolutionPath is not null && PathsEqual(resolutionPath, destinationPath)) ||
            DestinationExists(destinationPath))
        {
            return NotebookMigrationResult.Failed(
                NotebookMigrationErrorCode.DestinationConflict);
        }

        try
        {
            var resolution = await MigrationResolutionReader.ReadAsync(
                    resolutionPath,
                    cancellationToken)
                .ConfigureAwait(false);
            await using var source = await MigrationSourceFactory.OpenAsync(
                    sourcePath,
                    cancellationToken)
                .ConfigureAwait(false);
            var plan = await LegacyMigrationPlanner.CreateAsync(
                    source,
                    resolution,
                    cancellationToken)
                .ConfigureAwait(false);

            NotebookLogicalFingerprint fingerprint;
            if (request.DryRun)
            {
                fingerprint = await FingerprintAsync(
                        plan.CreateTransfer(source, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var inputPaths = resolutionPath is null
                    ? new[] { sourcePath }
                    : new[] { sourcePath, resolutionPath };
                var sink = await SchemaV2NotebookSink.PublishAsync(
                        destinationPath,
                        plan.CreateTransfer(source, cancellationToken),
                        inputPaths,
                        cancellationToken)
                    .ConfigureAwait(false);
                fingerprint = sink.Fingerprint;
            }

            return NotebookMigrationResult.Completed(new NotebookMigrationReport(
                source.Kind,
                request.DryRun,
                sourcePath,
                destinationPath,
                plan.Counts,
                plan.Issues,
                FormatFingerprint(fingerprint)));
        }
        catch (LegacySourceException exception)
        {
            return NotebookMigrationResult.Failed(exception.Error);
        }
        catch (FileNotFoundException)
        {
            return NotebookMigrationResult.Failed(NotebookMigrationErrorCode.InputNotFound);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            return NotebookMigrationResult.Failed(NotebookMigrationErrorCode.SourceBusy);
        }
        catch (SqliteException)
        {
            return NotebookMigrationResult.Failed(
                NotebookMigrationErrorCode.IntegrityFailure);
        }
        catch (InvalidDataException)
        {
            return NotebookMigrationResult.Failed(
                NotebookMigrationErrorCode.IntegrityFailure);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotebookMigrationResult.Failed(
                DestinationExists(destinationPath)
                    ? NotebookMigrationErrorCode.DestinationConflict
                    : NotebookMigrationErrorCode.IoFailure);
        }
    }

    private static async Task<NotebookLogicalFingerprint> FingerprintAsync(
        CurrentNotebookTransfer transfer,
        CancellationToken cancellationToken)
    {
        var builder = new NotebookLogicalFingerprintBuilder();
        foreach (var metadata in transfer.Metadata)
            builder.Add(metadata);
        await foreach (var page in transfer.Pages
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            builder.Add(page);
        }
        return builder.Build();
    }

    private static string FormatFingerprint(NotebookLogicalFingerprint fingerprint) =>
        $"{Convert.ToHexString(fingerprint.MetadataDigest).ToLowerInvariant()}:" +
        Convert.ToHexString(fingerprint.PageDigest).ToLowerInvariant();

    private static bool DestinationExists(string path) =>
        SqliteDatabaseFiles.AnyExists(path) ||
        Directory.Exists(path) ||
        SqliteDatabaseFiles.Enumerate(path).Any(
            candidate => new FileInfo(candidate).LinkTarget is not null);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
