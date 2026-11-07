#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Queries;
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
    private readonly INotebookBodySearchReader _bodySearchReader;
    private readonly INotebookPageWriter _writer;
    private readonly NotebookSessionLifetime _lifetime;

    internal NotebookPageApplicationAdapter(
        NotebookId notebookId,
        INotebookMetadataReader metadataReader,
        INotebookPageReader reader,
        INotebookBodySearchReader bodySearchReader,
        INotebookPageWriter writer,
        NotebookSessionLifetime lifetime)
    {
        _notebookId = notebookId;
        _metadataReader = metadataReader;
        _reader = reader;
        _bodySearchReader = bodySearchReader;
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

    public Task<NotebookOperationResult<IReadOnlyList<NotebookPageSummary>>> QuerySummariesAsync(
        NotebookPageQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _lifetime.ThrowIfDisposed();
        if (!Enum.IsDefined(request.Sort))
        {
            return Task.FromResult(NotebookOperationResult<
                IReadOnlyList<NotebookPageSummary>>.Failed(
                    NotebookFailureFactory.Validation(
                        "page.query.invalid_sort",
                        "The page query sort is not supported.")));
        }
        if (request.Limit <= 0)
        {
            return Task.FromResult(NotebookOperationResult<
                IReadOnlyList<NotebookPageSummary>>.Failed(
                    NotebookFailureFactory.Validation(
                        "page.query.invalid_limit",
                        "A page query limit must be greater than zero.")));
        }

        MemoriaNoteGlob pattern;
        if (request.Pattern is null)
        {
            pattern = MemoriaNoteGlob.MatchAll;
        }
        else if (!MemoriaNoteGlobParser.TryParse(
                     request.Pattern,
                     out var parsed,
                     out var parseError))
        {
            return Task.FromResult(NotebookOperationResult<
                IReadOnlyList<NotebookPageSummary>>.Failed(
                    GlobFailure(parseError)));
        }
        else
        {
            pattern = parsed!;
        }

        var query = new RepositoryPageQuery(
            pattern,
            request.Sort switch
            {
                NotebookPageQuerySort.Name => RepositoryPageQuerySort.Name,
                NotebookPageQuerySort.Updated => RepositoryPageQuerySort.Updated,
                NotebookPageQuerySort.Created => RepositoryPageQuerySort.Created,
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            },
            request.Reverse,
            request.Limit);
        return NotebookOperationExecutor.ExecuteAsync<IReadOnlyList<NotebookPageSummary>>(
            async () => (await _reader.QuerySummariesAsync(
                    _notebookId,
                    query,
                    cancellationToken)
                .ConfigureAwait(false)).Select(Map).ToArray(),
            "page.query",
            cancellationToken);
    }

    public async Task<NotebookOperationResult<NotebookBodySearchSession>> StartBodySearchAsync(
        NotebookBodySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _lifetime.ThrowIfDisposed();
        if (!Enum.IsDefined(request.Sort))
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                NotebookFailureFactory.Validation(
                    "page.search.invalid_sort",
                    "The body-search sort is not supported."));
        }
        if (request.Limit is <= 0 or > NotebookBodySearchRequest.MaximumLimit)
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                NotebookFailureFactory.Validation(
                    "page.search.invalid_limit",
                    $"A body-search limit must be between 1 and " +
                    $"{NotebookBodySearchRequest.MaximumLimit}."));
        }
        if (request.Timeout <= TimeSpan.Zero ||
            request.Timeout > TimeSpan.FromSeconds(
                NotebookBodySearchRequest.MaximumTimeoutSeconds))
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                NotebookFailureFactory.Validation(
                    "page.search.invalid_timeout",
                    $"A body-search timeout must be greater than zero and at most " +
                    $"{NotebookBodySearchRequest.MaximumTimeoutSeconds} seconds."));
        }
        if (!BodySearchQueryParser.TryParse(
                request.Query,
                out var parsed,
                out var parseError))
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                BodySearchFailure(parseError));
        }

        var query = new RepositoryBodySearchQuery(
            parsed!,
            request.Sort switch
            {
                NotebookBodySearchSort.Name => RepositoryBodySearchSort.Name,
                NotebookBodySearchSort.Updated => RepositoryBodySearchSort.Updated,
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            },
            request.Limit,
            request.IncludeSnippet);
        return await NotebookBodySearchSession.StartAsync(
                effectiveCancellationToken => _bodySearchReader.StartAsync(
                    _notebookId,
                    query,
                    effectiveCancellationToken),
                _lifetime,
                request.Timeout,
                cancellationToken)
            .ConfigureAwait(false);
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

    private static NotebookFailure BodySearchFailure(BodySearchQueryParseError error) =>
        error switch
        {
            BodySearchQueryParseError.Empty => NotebookFailureFactory.Validation(
                "page.search.empty_query",
                "A body-search query must contain at least one keyword."),
            BodySearchQueryParseError.QueryTooLong => NotebookFailureFactory.Validation(
                "page.search.query_too_long",
                $"A body-search query must contain at most " +
                $"{NotebookBodySearchRequest.MaximumQueryScalarCount} Unicode scalars."),
            BodySearchQueryParseError.KeywordTooLong => NotebookFailureFactory.Validation(
                "page.search.keyword_too_long",
                $"A body-search keyword must contain at most " +
                $"{NotebookBodySearchRequest.MaximumKeywordScalarCount} Unicode scalars."),
            BodySearchQueryParseError.TooManyKeywords => NotebookFailureFactory.Validation(
                "page.search.too_many_keywords",
                $"A body-search query must contain at most " +
                $"{NotebookBodySearchRequest.MaximumKeywordCount} distinct keywords."),
            _ => NotebookFailureFactory.Validation(
                "page.search.invalid_query",
                "The body-search query contains invalid Unicode or a control character.")
        };

    private static NotebookFailure GlobFailure(MemoriaNoteGlobParseError error) =>
        error switch
        {
            MemoriaNoteGlobParseError.Empty => NotebookFailureFactory.Validation(
                "page.query.empty_pattern",
                "A page-name pattern must not be empty."),
            MemoriaNoteGlobParseError.SourceTooLong => NotebookFailureFactory.Validation(
                "page.query.pattern_too_long",
                $"A page-name pattern must contain at most " +
                $"{MemoriaNoteGlob.MaximumSourceScalarCount} Unicode scalars."),
            _ => NotebookFailureFactory.Validation(
                "page.query.invalid_pattern",
                "The page-name pattern is not valid MemoriaNote glob syntax.")
        };
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

    internal static NotebookFailure Timeout(string code, string message) =>
        new(NotebookFailureCategory.Timeout, code, message);

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
