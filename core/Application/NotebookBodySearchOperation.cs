#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Queries;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;

namespace MemoriaNote.Application;

/// <summary>Shares body-search request interpretation across application compositions.</summary>
internal static class NotebookBodySearchOperation
{
    internal static async Task<NotebookOperationResult<NotebookBodySearchSession>> StartAsync(
        NotebookId notebookId,
        INotebookBodySearchReader bodySearchReader,
        string queryText,
        RepositoryBodySearchSort sort,
        int limit,
        bool includeSnippet,
        TimeSpan timeout,
        NotebookSessionLifetime lifetime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        ArgumentNullException.ThrowIfNull(bodySearchReader);
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(lifetime);
        lifetime.ThrowIfDisposed();

        if (limit is <= 0 or > NotebookBodySearchRequest.MaximumLimit)
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                NotebookFailureFactory.Validation(
                    "page.search.invalid_limit",
                    $"A body-search limit must be between 1 and " +
                    $"{NotebookBodySearchRequest.MaximumLimit}."));
        }
        if (timeout <= TimeSpan.Zero ||
            timeout > TimeSpan.FromSeconds(
                NotebookBodySearchRequest.MaximumTimeoutSeconds))
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                NotebookFailureFactory.Validation(
                    "page.search.invalid_timeout",
                    $"A body-search timeout must be greater than zero and at most " +
                    $"{NotebookBodySearchRequest.MaximumTimeoutSeconds} seconds."));
        }
        if (!BodySearchQueryParser.TryParse(
                queryText,
                out var parsed,
                out var parseError))
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                BodySearchFailure(parseError));
        }

        var query = new RepositoryBodySearchQuery(
            parsed!,
            sort,
            limit,
            includeSnippet);
        return await NotebookBodySearchSession.StartAsync(
                effectiveCancellationToken => bodySearchReader.StartAsync(
                    notebookId,
                    query,
                    effectiveCancellationToken),
                lifetime,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
    }

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
}
