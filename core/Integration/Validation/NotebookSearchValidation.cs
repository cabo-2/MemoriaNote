#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.SchemaV2;

namespace MemoriaNote.Integration.Validation;

/// <summary>Creates revisioned search operations for first-party validation tools.</summary>
public static class NotebookSearchValidation
{
    /// <summary>Creates a search validation session bound to a validated application session.</summary>
    /// <param name="applicationSession">The current-format application session.</param>
    /// <returns>The search validation session.</returns>
    public static NotebookSearchValidationSession CreateSession(
        NotebookApplicationSession applicationSession)
    {
        ArgumentNullException.ThrowIfNull(applicationSession);
        applicationSession.Lifetime.ThrowIfDisposed();
        return new NotebookSearchValidationSession(applicationSession);
    }
}

/// <summary>
/// Executes fixed body-search candidates through the shared application search contract.
/// </summary>
/// <remarks>
/// This surface may change or be removed in a later integration contract revision. It is not an
/// end-user search-mode contract.
/// </remarks>
public sealed class NotebookSearchValidationSession
{
    private readonly NotebookApplicationSession _applicationSession;

    internal NotebookSearchValidationSession(
        NotebookApplicationSession applicationSession)
    {
        _applicationSession = applicationSession;
    }

    /// <summary>Starts one read-only query using the explicitly selected candidate.</summary>
    /// <param name="request">The candidate, logical query, ordering, and result bounds.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The shared application search session or a classified failure.</returns>
    public async Task<NotebookOperationResult<NotebookBodySearchSession>> StartAsync(
        BodySearchValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _applicationSession.Lifetime.ThrowIfDisposed();
        if (!Enum.IsDefined(request.Candidate))
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                InvalidSelection(
                    "page.search.invalid_candidate",
                    "The body-search candidate is not supported."));
        }
        if (!Enum.IsDefined(request.Sort))
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                InvalidSelection(
                    "page.search.invalid_sort",
                    "The body-search sort is not supported."));
        }
        if (!Enum.IsDefined(request.CaseSensitivity))
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                InvalidSelection(
                    "page.search.invalid_case_sensitivity",
                    "The body-search case sensitivity is not supported."));
        }
        if (request.Candidate == BodySearchCandidate.Scan &&
            request.Sort == BodySearchValidationSort.Relevance)
        {
            return NotebookOperationResult<NotebookBodySearchSession>.Failed(
                InvalidSelection(
                    "page.search.unsupported_sort",
                    "The Scan body-search candidate does not provide relevance ordering."));
        }

        return await NotebookBodySearchOperation.StartAsync(
                _applicationSession.Notebook.NotebookId,
                SchemaV2RepositoryProvider.CreateBodySearchReader(Map(request.Candidate)),
                request.Query,
                Map(request.Sort),
                request.Limit,
                request.IncludeSnippet,
                request.CaseSensitivity switch
                {
                    NotebookBodySearchCaseSensitivity.Insensitive =>
                        RepositoryBodySearchCaseSensitivity.Insensitive,
                    NotebookBodySearchCaseSensitivity.Sensitive =>
                        RepositoryBodySearchCaseSensitivity.Sensitive,
                    _ => throw new ArgumentOutOfRangeException(nameof(request))
                },
                request.Timeout,
                _applicationSession.Lifetime,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static SchemaV2BodySearchCandidate Map(BodySearchCandidate candidate) =>
        candidate switch
        {
            BodySearchCandidate.Scan => SchemaV2BodySearchCandidate.Scan,
            BodySearchCandidate.Fts5Unicode61 => SchemaV2BodySearchCandidate.FtsUnicode61,
            BodySearchCandidate.Fts5Trigram => SchemaV2BodySearchCandidate.FtsTrigram,
            _ => throw new ArgumentOutOfRangeException(nameof(candidate))
        };

    private static RepositoryBodySearchSort Map(BodySearchValidationSort sort) => sort switch
    {
        BodySearchValidationSort.Name => RepositoryBodySearchSort.Name,
        BodySearchValidationSort.Updated => RepositoryBodySearchSort.Updated,
        BodySearchValidationSort.Relevance => RepositoryBodySearchSort.Relevance,
        _ => throw new ArgumentOutOfRangeException(nameof(sort))
    };

    private static NotebookFailure InvalidSelection(string code, string message) =>
        new(NotebookFailureCategory.Validation, code, message);
}
