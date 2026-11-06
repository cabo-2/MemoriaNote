#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;

namespace MemoriaNote.Integration.Validation;

/// <summary>Publishes canonical notebooks from deterministic first-party validation records.</summary>
public static class DeterministicNotebookPublisher
{
    /// <summary>Publishes a new notebook through the production sink and validation gate.</summary>
    /// <param name="request">The deterministic logical input and new destination.</param>
    /// <param name="cancellationToken">The token used to cancel publication.</param>
    /// <returns>The published notebook descriptor or a classified failure.</returns>
    public static async Task<NotebookOperationResult<NotebookDescriptor>> PublishAsync(
        DeterministicNotebookPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var metadata = request.Metadata
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new CurrentNotebookMetadataRecord(entry.Key, entry.Value!))
                .ToArray();
            await SchemaV2NotebookSink.PublishAsync(
                    request.DestinationPath,
                    new CurrentNotebookTransfer(
                        metadata,
                        ConvertPagesAsync(request.Pages, cancellationToken)),
                    Array.Empty<string>(),
                    cancellationToken)
                .ConfigureAwait(false);
            return NotebookOperationResult<NotebookDescriptor>.Succeeded(
                new NotebookDescriptor(
                    NotebookId.FromDatabasePath(request.DestinationPath),
                    SchemaV2Format.UserVersion));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException) when (DestinationConflicts(request.DestinationPath))
        {
            return NotebookOperationResult<NotebookDescriptor>.Failed(
                NotebookFailureFactory.Conflict(
                    "corpus.destination_exists",
                    "The corpus destination or one of its SQLite sidecars already exists."));
        }
        catch (Exception exception) when (NotebookFailureFactory.IsClassifiable(exception))
        {
            return NotebookOperationResult<NotebookDescriptor>.Failed(
                NotebookFailureFactory.FromException(exception, "corpus.publish"));
        }
    }

    private static async IAsyncEnumerable<CurrentNotebookPageRecord> ConvertPagesAsync(
        IAsyncEnumerable<DeterministicNotebookPage> pages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var page in pages
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (page is null)
                throw new ArgumentException("The deterministic page stream contains null.", nameof(pages));
            SchemaV2PageValueCodec.ValidateName(page.Name, nameof(pages));
            var tags = SchemaV2TagCodec.Encode(page.Tags);
            yield return new CurrentNotebookPageRecord(
                page.PageId.ToUuid(),
                page.Name,
                SchemaV2PageValueCodec.FormatTimestamp(page.CreateTimeUtc),
                SchemaV2PageValueCodec.FormatTimestamp(page.UpdateTimeUtc),
                page.Revision,
                tags,
                page.Text);
        }
    }

    private static bool DestinationConflicts(string destinationPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(destinationPath);
            return Directory.Exists(fullPath) || SqliteDatabaseFiles.Enumerate(fullPath).Any(path =>
            {
                var information = new FileInfo(path);
                information.Refresh();
                return information.Exists || information.LinkTarget is not null;
            });
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return false;
        }
    }
}
