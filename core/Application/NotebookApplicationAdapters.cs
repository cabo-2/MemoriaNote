#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.Repositories;

namespace MemoriaNote.Application;

internal sealed class NotebookMetadataApplicationAdapter : INotebookMetadataApplication
{
    private readonly NotebookId _notebookId;
    private readonly INotebookMetadataReader _reader;
    private readonly INotebookMetadataWriter _writer;
    private readonly NotebookSessionLifetime _lifetime;

    internal NotebookMetadataApplicationAdapter(
        NotebookId notebookId,
        INotebookMetadataReader reader,
        INotebookMetadataWriter writer,
        NotebookSessionLifetime lifetime)
    {
        _notebookId = notebookId;
        _reader = reader;
        _writer = writer;
        _lifetime = lifetime;
    }

    public Task<NotebookOperationResult<NotebookMetadataSnapshot>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        _lifetime.ThrowIfDisposed();
        return NotebookOperationExecutor.ExecuteAsync(
            async () => Map(await _reader
                .LoadAsync(_notebookId, cancellationToken)
                .ConfigureAwait(false)),
            "metadata.read",
            cancellationToken);
    }

    public Task<NotebookOperationResult<NotebookMetadataSnapshot>> UpdateAsync(
        NotebookMetadataChangeSet request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _lifetime.ThrowIfDisposed();
        return NotebookOperationExecutor.ExecuteAsync(
            async () => Map(await _writer
                .UpdateAsync(
                    _notebookId,
                    new NotebookMetadataChanges(request.Changes.ToDictionary(
                        change => change.Key,
                        change => change.Value!,
                        StringComparer.Ordinal)),
                    cancellationToken)
                .ConfigureAwait(false)),
            "metadata.update",
            cancellationToken);
    }

    private static NotebookMetadataSnapshot Map(
        Domain.Repositories.NotebookMetadataSnapshot snapshot)
    {
        return new NotebookMetadataSnapshot(
            snapshot.NotebookId,
            snapshot.Values.ToDictionary(
                entry => entry.Key,
                entry => (string?)entry.Value,
                StringComparer.Ordinal));
    }
}

internal sealed class NotebookPageApplicationAdapter : INotebookPageApplication
{
    private readonly NotebookId _notebookId;
    private readonly INotebookMetadataReader _metadataReader;
    private readonly INotebookPageReader _reader;
    private readonly INotebookPageWriter _writer;
    private readonly NotebookSessionLifetime _lifetime;

    internal NotebookPageApplicationAdapter(
        NotebookId notebookId,
        INotebookMetadataReader metadataReader,
        INotebookPageReader reader,
        INotebookPageWriter writer,
        NotebookSessionLifetime lifetime)
    {
        _notebookId = notebookId;
        _metadataReader = metadataReader;
        _reader = reader;
        _writer = writer;
        _lifetime = lifetime;
    }

    public async Task<NotebookOperationResult<NotebookPage>> GetByIdAsync(
        PageId pageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageId);
        _lifetime.ThrowIfDisposed();
        return await ReadAsync(
                () => _reader.FindByIdAsync(_notebookId, pageId, cancellationToken),
                "page.read",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NotebookOperationResult<NotebookPage>> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        _lifetime.ThrowIfDisposed();
        return await ReadAsync(
                () => _reader.FindByNameAsync(_notebookId, name, cancellationToken),
                "page.read_by_name",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NotebookOperationResult<NotebookPage>> ResolveByIdPrefixAsync(
        string pageIdPrefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageIdPrefix);
        _lifetime.ThrowIfDisposed();
        if (!PageSelector.TryFromPageId(pageIdPrefix, out var selector))
        {
            return NotebookOperationResult<NotebookPage>.Failed(
                NotebookFailureFactory.Validation(
                    "page.invalid_id_prefix",
                    "A Page ID prefix must contain 4 to 32 hexadecimal characters."));
        }

        var matches = await NotebookOperationExecutor.ExecuteAsync(
                () => _reader.ListByIdPrefixAsync(
                    _notebookId,
                    selector.PageIdPrefix,
                    maximumCount: 2,
                    cancellationToken),
                "page.resolve_id_prefix",
                cancellationToken)
            .ConfigureAwait(false);
        if (!matches.IsSuccess)
        {
            return NotebookOperationResult<NotebookPage>.Failed(matches.Failure!);
        }
        if (matches.Value!.Count == 0)
        {
            return NotebookOperationResult<NotebookPage>.Failed(
                NotebookFailureFactory.NotFound(
                    "page.not_found",
                    "No page matches the Page ID prefix."));
        }
        if (matches.Value.Count > 1)
        {
            return NotebookOperationResult<NotebookPage>.Failed(
                NotebookFailureFactory.Conflict(
                    "page.ambiguous_id_prefix",
                    "The Page ID prefix matches more than one page."));
        }

        return NotebookOperationResult<NotebookPage>.Succeeded(Map(matches.Value[0]));
    }

    public Task<NotebookOperationResult<int>> CountAsync(
        CancellationToken cancellationToken = default)
    {
        _lifetime.ThrowIfDisposed();
        return NotebookOperationExecutor.ExecuteAsync(
            () => _reader.CountAsync(_notebookId, cancellationToken),
            "page.count",
            cancellationToken);
    }

    public Task<NotebookOperationResult<IReadOnlyList<NotebookPageSummary>>> ListSummariesAsync(
        int skipCount,
        int takeCount,
        CancellationToken cancellationToken = default)
    {
        _lifetime.ThrowIfDisposed();
        return NotebookOperationExecutor.ExecuteAsync<IReadOnlyList<NotebookPageSummary>>(
            async () => (await _reader.ListSummariesAsync(
                    _notebookId,
                    skipCount,
                    takeCount,
                    cancellationToken)
                .ConfigureAwait(false)).Select(Map).ToArray(),
            "page.list_summaries",
            cancellationToken);
    }

    public async Task<NotebookOperationResult<NotebookPage>> CreateAsync(
        NotebookPageCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _lifetime.ThrowIfDisposed();
        var writeFailure = await CheckWritableAsync(cancellationToken).ConfigureAwait(false);
        if (writeFailure is not null)
            return NotebookOperationResult<NotebookPage>.Failed(writeFailure);
        var result = await NotebookOperationExecutor.ExecuteAsync(
                () => _writer.CreateAsync(
                    _notebookId,
                    new RepositoryPageCreateRequest(request.Name, request.Text, request.Tags),
                    cancellationToken),
                "page.create",
                cancellationToken)
            .ConfigureAwait(false);
        return MapWrite(result);
    }

    public async Task<NotebookOperationResult<NotebookPage>> ReplaceAsync(
        PageId pageId,
        long expectedRevision,
        NotebookPageReplaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageId);
        ArgumentNullException.ThrowIfNull(request);
        _lifetime.ThrowIfDisposed();
        var writeFailure = await CheckWritableAsync(cancellationToken).ConfigureAwait(false);
        if (writeFailure is not null)
            return NotebookOperationResult<NotebookPage>.Failed(writeFailure);
        var result = await NotebookOperationExecutor.ExecuteAsync(
                () => _writer.UpdateAsync(
                    _notebookId,
                    pageId,
                    expectedRevision,
                    new RepositoryPageUpdateRequest(request.Name, request.Text, request.Tags),
                    cancellationToken),
                "page.replace",
                cancellationToken)
            .ConfigureAwait(false);
        return MapWrite(result);
    }

    public async Task<NotebookOperationResult<bool>> DeleteAsync(
        PageId pageId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageId);
        _lifetime.ThrowIfDisposed();
        var writeFailure = await CheckWritableAsync(cancellationToken).ConfigureAwait(false);
        if (writeFailure is not null)
            return NotebookOperationResult<bool>.Failed(writeFailure);
        var result = await NotebookOperationExecutor.ExecuteAsync(
                () => _writer.DeleteAsync(
                    _notebookId,
                    pageId,
                    expectedRevision,
                    cancellationToken),
                "page.delete",
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
            return NotebookOperationResult<bool>.Failed(result.Failure!);

        return result.Value!.Status switch
        {
            RepositoryPageWriteStatus.Success => NotebookOperationResult<bool>.Succeeded(true),
            RepositoryPageWriteStatus.NotFound => NotebookOperationResult<bool>.Failed(
                NotebookFailureFactory.NotFound("page.not_found", "The page does not exist.")),
            RepositoryPageWriteStatus.RevisionConflict => NotebookOperationResult<bool>.Failed(
                NotebookFailureFactory.Conflict(
                    "page.revision_conflict",
                    "The page revision has changed.")),
            RepositoryPageWriteStatus.NameConflict => throw new InvalidOperationException(
                "A delete operation cannot report a page name conflict."),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    private async Task<NotebookOperationResult<NotebookPage>> ReadAsync(
        Func<Task<RepositoryPage>> readAsync,
        string operationCode,
        CancellationToken cancellationToken)
    {
        var result = await NotebookOperationExecutor
            .ExecuteAsync(readAsync, operationCode, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
            return NotebookOperationResult<NotebookPage>.Failed(result.Failure!);
        if (result.Value is null)
        {
            return NotebookOperationResult<NotebookPage>.Failed(
                NotebookFailureFactory.NotFound(
                    "page.not_found",
                    "The page does not exist."));
        }

        return NotebookOperationResult<NotebookPage>.Succeeded(Map(result.Value));
    }

    private async Task<NotebookFailure?> CheckWritableAsync(
        CancellationToken cancellationToken)
    {
        var metadata = await NotebookOperationExecutor.ExecuteAsync(
                () => _metadataReader.LoadAsync(_notebookId, cancellationToken),
                "page.read_metadata",
                cancellationToken)
            .ConfigureAwait(false);
        if (!metadata.IsSuccess)
            return metadata.Failure;

        return metadata.Value!.Values.TryGetValue("ReadOnly", out var value) &&
               string.Equals(value, bool.TrueString, StringComparison.Ordinal)
            ? NotebookFailureFactory.Conflict(
                "notebook.read_only",
                "The notebook is read-only.")
            : null;
    }

    private static NotebookOperationResult<NotebookPage> MapWrite(
        NotebookOperationResult<RepositoryPageWriteResult> result)
    {
        if (!result.IsSuccess)
            return NotebookOperationResult<NotebookPage>.Failed(result.Failure!);

        return result.Value!.Status switch
        {
            RepositoryPageWriteStatus.Success =>
                NotebookOperationResult<NotebookPage>.Succeeded(Map(result.Value.Page)),
            RepositoryPageWriteStatus.NotFound => NotebookOperationResult<NotebookPage>.Failed(
                NotebookFailureFactory.NotFound("page.not_found", "The page does not exist.")),
            RepositoryPageWriteStatus.RevisionConflict =>
                NotebookOperationResult<NotebookPage>.Failed(
                    NotebookFailureFactory.Conflict(
                        "page.revision_conflict",
                        "The page revision has changed.")),
            RepositoryPageWriteStatus.NameConflict => NotebookOperationResult<NotebookPage>.Failed(
                NotebookFailureFactory.Conflict(
                    "page.name_conflict",
                    "Another page has the same name.")),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    private static NotebookPage Map(RepositoryPage page) => new(
        page.NotebookId,
        page.PageId,
        page.Name,
        page.CreateTimeUtc,
        page.UpdateTimeUtc,
        page.Revision,
        page.Tags,
        page.Text);

    private static NotebookPageSummary Map(RepositoryPageSummary page) => new(
        page.NotebookId,
        page.PageId,
        page.Name,
        page.CreateTimeUtc,
        page.UpdateTimeUtc,
        page.Revision);
}

internal static class NotebookOperationExecutor
{
    internal static async Task<NotebookOperationResult<T>> ExecuteAsync<T>(
        Func<Task<T>> operation,
        string operationCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return NotebookOperationResult<T>.Succeeded(
                await operation().ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (NotebookFailureFactory.IsClassifiable(exception))
        {
            return NotebookOperationResult<T>.Failed(
                NotebookFailureFactory.FromException(exception, operationCode));
        }
    }
}

internal static class NotebookFailureFactory
{
    internal static NotebookFailure Validation(string code, string message) =>
        new(NotebookFailureCategory.Validation, code, message);

    internal static NotebookFailure NotFound(string code, string message) =>
        new(NotebookFailureCategory.NotFound, code, message);

    internal static NotebookFailure Conflict(string code, string message) =>
        new(NotebookFailureCategory.Conflict, code, message);

    internal static NotebookFailure Format(string code, string message) =>
        new(NotebookFailureCategory.Format, code, message);

    internal static bool IsClassifiable(Exception exception) =>
        exception is ArgumentException or InvalidDataException or IOException or
        UnauthorizedAccessException || NotebookStorageFailureClassifier.CanClassify(exception);

    internal static NotebookFailure FromException(Exception exception, string operationCode)
    {
        return exception switch
        {
            ArgumentException => Validation(
                $"{operationCode}.invalid_value",
                "The operation contains an invalid value."),
            InvalidDataException => Format(
                $"{operationCode}.invalid_data",
                "The notebook contains invalid persisted data."),
            UnauthorizedAccessException => new NotebookFailure(
                NotebookFailureCategory.AccessOrBusy,
                $"{operationCode}.access_denied",
                "Access to the notebook was denied."),
            IOException => new NotebookFailure(
                NotebookFailureCategory.AccessOrBusy,
                $"{operationCode}.io_failure",
                "The notebook could not be accessed."),
            _ => FromStorageFailure(
                NotebookStorageFailureClassifier.Classify(exception),
                operationCode)
        };
    }

    private static NotebookFailure FromStorageFailure(
        NotebookStorageFailureKind failure,
        string operationCode)
    {
        return failure switch
        {
            NotebookStorageFailureKind.Busy => new NotebookFailure(
                NotebookFailureCategory.AccessOrBusy,
                $"{operationCode}.busy",
                "The notebook is busy or locked."),
            NotebookStorageFailureKind.Access => new NotebookFailure(
                NotebookFailureCategory.AccessOrBusy,
                $"{operationCode}.access_failure",
                "The notebook could not be accessed."),
            NotebookStorageFailureKind.Constraint => Validation(
                $"{operationCode}.invalid_data",
                "The operation contains conflicting or invalid logical data."),
            NotebookStorageFailureKind.Unexpected => new NotebookFailure(
                NotebookFailureCategory.UnexpectedStorage,
                $"{operationCode}.storage_failure",
                "The storage provider could not complete the operation."),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
    }
}
