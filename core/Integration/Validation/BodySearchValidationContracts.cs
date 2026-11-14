#nullable enable

using System;
using MemoriaNote.Application;

namespace MemoriaNote.Integration.Validation;

/// <summary>Identifies one fixed body-search candidate for first-party validation.</summary>
public enum BodySearchCandidate
{
    /// <summary>Scans authoritative page bodies using ordinal literal substrings.</summary>
    Scan,

    /// <summary>Searches the fixed FTS5 unicode61 index.</summary>
    Fts5Unicode61,

    /// <summary>Searches the fixed FTS5 trigram index.</summary>
    Fts5Trigram
}

/// <summary>Identifies one body-search ordering available to validation consumers.</summary>
public enum BodySearchValidationSort
{
    /// <summary>Orders by binary page name and then by Page ID.</summary>
    Name,

    /// <summary>Orders by last-update time, newest first, and then by Page ID.</summary>
    Updated,

    /// <summary>Orders an FTS5 candidate by BM25 rank and then by Page ID.</summary>
    Relevance
}

/// <summary>Describes one explicitly selected body-search candidate invocation.</summary>
public sealed class BodySearchValidationRequest
{
    /// <summary>Initializes a first-party candidate search request.</summary>
    /// <param name="candidate">The fixed candidate to execute.</param>
    /// <param name="query">
    /// The query split on Unicode whitespace into literal keywords joined by logical AND.
    /// </param>
    /// <param name="sort">The deterministic ordering to apply.</param>
    /// <param name="limit">The positive maximum number of hits to return.</param>
    /// <param name="includeSnippet">Whether each hit should include a structured snippet.</param>
    /// <param name="timeout">
    /// The positive execution time limit, or null to use the application default.
    /// </param>
    /// <param name="caseSensitivity">The ordinal case comparison to use.</param>
    public BodySearchValidationRequest(
        BodySearchCandidate candidate,
        string query,
        BodySearchValidationSort sort,
        int limit = NotebookBodySearchRequest.DefaultLimit,
        bool includeSnippet = true,
        TimeSpan? timeout = null,
        NotebookBodySearchCaseSensitivity caseSensitivity =
            NotebookBodySearchCaseSensitivity.Insensitive)
    {
        Candidate = candidate;
        Query = query ?? throw new ArgumentNullException(nameof(query));
        Sort = sort;
        Limit = limit;
        IncludeSnippet = includeSnippet;
        Timeout = timeout ?? TimeSpan.FromSeconds(
            NotebookBodySearchRequest.DefaultTimeoutSeconds);
        CaseSensitivity = caseSensitivity;
    }

    /// <summary>Gets the fixed candidate to execute.</summary>
    public BodySearchCandidate Candidate { get; }

    /// <summary>Gets the unparsed query text.</summary>
    public string Query { get; }

    /// <summary>Gets the deterministic result ordering.</summary>
    public BodySearchValidationSort Sort { get; }

    /// <summary>Gets the maximum number of hits to return.</summary>
    public int Limit { get; }

    /// <summary>Gets whether each hit should include a structured snippet.</summary>
    public bool IncludeSnippet { get; }

    /// <summary>Gets the execution time limit.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Gets the ordinal case comparison used by the search.</summary>
    public NotebookBodySearchCaseSensitivity CaseSensitivity { get; }
}
