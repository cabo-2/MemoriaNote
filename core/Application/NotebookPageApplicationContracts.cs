#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;

namespace MemoriaNote.Application;

/// <summary>Represents one complete current-format notebook page.</summary>
public sealed class NotebookPage
{
    internal NotebookPage(
        NotebookId notebookId,
        PageId pageId,
        string name,
        DateTimeOffset createTimeUtc,
        DateTimeOffset updateTimeUtc,
        long revision,
        IReadOnlyList<string> tags,
        string text)
    {
        NotebookId = notebookId;
        PageId = pageId;
        Name = name;
        CreateTimeUtc = createTimeUtc;
        UpdateTimeUtc = updateTimeUtc;
        Revision = revision;
        Tags = Array.AsReadOnly([.. tags]);
        Text = text;
    }

    /// <summary>Gets the owning notebook identifier.</summary>
    public NotebookId NotebookId { get; }

    /// <summary>Gets the stable page identifier.</summary>
    public PageId PageId { get; }

    /// <summary>Gets the page name.</summary>
    public string Name { get; }

    /// <summary>Gets the UTC creation timestamp.</summary>
    public DateTimeOffset CreateTimeUtc { get; }

    /// <summary>Gets the UTC last-update timestamp.</summary>
    public DateTimeOffset UpdateTimeUtc { get; }

    /// <summary>Gets the optimistic-concurrency revision.</summary>
    public long Revision { get; }

    /// <summary>Gets the canonical, duplicate-free tag collection.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Gets the page body.</summary>
    public string Text { get; }
}

/// <summary>Represents a body-free projection of one notebook page.</summary>
public sealed class NotebookPageSummary
{
    internal NotebookPageSummary(
        NotebookId notebookId,
        PageId pageId,
        string name,
        DateTimeOffset createTimeUtc,
        DateTimeOffset updateTimeUtc,
        long revision)
    {
        NotebookId = notebookId;
        PageId = pageId;
        Name = name;
        CreateTimeUtc = createTimeUtc;
        UpdateTimeUtc = updateTimeUtc;
        Revision = revision;
    }

    /// <summary>Gets the owning notebook identifier.</summary>
    public NotebookId NotebookId { get; }

    /// <summary>Gets the stable page identifier.</summary>
    public PageId PageId { get; }

    /// <summary>Gets the page name.</summary>
    public string Name { get; }

    /// <summary>Gets the UTC creation timestamp.</summary>
    public DateTimeOffset CreateTimeUtc { get; }

    /// <summary>Gets the UTC last-update timestamp.</summary>
    public DateTimeOffset UpdateTimeUtc { get; }

    /// <summary>Gets the optimistic-concurrency revision.</summary>
    public long Revision { get; }
}

/// <summary>Identifies the primary ordering of a page-name query.</summary>
public enum NotebookPageQuerySort
{
    /// <summary>Orders by binary page name.</summary>
    Name,

    /// <summary>Orders by last-update time, newest first by default.</summary>
    Updated,

    /// <summary>Orders by creation time, newest first by default.</summary>
    Created
}

/// <summary>Describes a storage-independent page-name query.</summary>
public sealed class NotebookPageQueryRequest
{
    /// <summary>Initializes a page-name query.</summary>
    /// <param name="pattern">
    /// The optional MemoriaNote glob matched against the complete page name, or
    /// <see langword="null"/> to match every page.
    /// </param>
    /// <param name="sort">The primary ordering to apply.</param>
    /// <param name="reverse">Whether to reverse every ordering key.</param>
    /// <param name="limit">The optional positive maximum result count.</param>
    public NotebookPageQueryRequest(
        string? pattern = null,
        NotebookPageQuerySort sort = NotebookPageQuerySort.Name,
        bool reverse = false,
        int? limit = null)
    {
        Pattern = pattern;
        Sort = sort;
        Reverse = reverse;
        Limit = limit;
    }

    /// <summary>Gets the optional MemoriaNote glob.</summary>
    public string? Pattern { get; }

    /// <summary>Gets the primary ordering.</summary>
    public NotebookPageQuerySort Sort { get; }

    /// <summary>Gets whether every ordering key is reversed.</summary>
    public bool Reverse { get; }

    /// <summary>Gets the optional maximum result count.</summary>
    public int? Limit { get; }
}

/// <summary>Identifies one deterministic body-search ordering.</summary>
public enum NotebookBodySearchSort
{
    /// <summary>Orders by binary page name and then by Page ID.</summary>
    Name,

    /// <summary>Orders by last-update time, newest first, and then by Page ID.</summary>
    Updated
}

/// <summary>Identifies the ordinal case comparison used by a body search.</summary>
public enum NotebookBodySearchCaseSensitivity
{
    /// <summary>Matches text using culture-independent ordinal case folding.</summary>
    Insensitive,

    /// <summary>Matches text using ordinal case-sensitive comparison.</summary>
    Sensitive
}

/// <summary>Describes a storage-independent search of authoritative page bodies.</summary>
public sealed class NotebookBodySearchRequest
{
    /// <summary>The default maximum number of hits returned by one search.</summary>
    public const int DefaultLimit = 20;

    /// <summary>The largest accepted result limit.</summary>
    public const int MaximumLimit = 1000;

    /// <summary>The largest accepted number of distinct keywords.</summary>
    public const int MaximumKeywordCount = 16;

    /// <summary>The largest accepted keyword length in Unicode scalars.</summary>
    public const int MaximumKeywordScalarCount = 128;

    /// <summary>The largest accepted query length in Unicode scalars.</summary>
    public const int MaximumQueryScalarCount = 1024;

    /// <summary>The default search execution time limit in seconds.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>The largest accepted search execution time limit in seconds.</summary>
    public const int MaximumTimeoutSeconds = 600;

    /// <summary>Initializes an authoritative-body search request.</summary>
    /// <param name="query">
    /// The query split on Unicode whitespace into literal keywords joined by logical AND.
    /// </param>
    /// <param name="sort">The deterministic ordering to apply.</param>
    /// <param name="limit">The positive maximum number of hits to return.</param>
    /// <param name="includeSnippet">Whether each hit should include a structured snippet.</param>
    /// <param name="timeout">
    /// The positive execution time limit, or null to use <see cref="DefaultTimeoutSeconds"/>.
    /// </param>
    /// <param name="caseSensitivity">The ordinal case comparison to use.</param>
    public NotebookBodySearchRequest(
        string query,
        NotebookBodySearchSort sort,
        int limit = DefaultLimit,
        bool includeSnippet = true,
        TimeSpan? timeout = null,
        NotebookBodySearchCaseSensitivity caseSensitivity =
            NotebookBodySearchCaseSensitivity.Insensitive)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        Sort = sort;
        Limit = limit;
        IncludeSnippet = includeSnippet;
        Timeout = timeout ?? TimeSpan.FromSeconds(DefaultTimeoutSeconds);
        CaseSensitivity = caseSensitivity;
    }

    /// <summary>Gets the unparsed query text.</summary>
    public string Query { get; }

    /// <summary>Gets the deterministic result ordering.</summary>
    public NotebookBodySearchSort Sort { get; }

    /// <summary>Gets the maximum number of hits to return.</summary>
    public int Limit { get; }

    /// <summary>Gets whether each hit should include a structured snippet.</summary>
    public bool IncludeSnippet { get; }

    /// <summary>Gets the application-level execution time limit.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Gets the ordinal case comparison used by the search.</summary>
    public NotebookBodySearchCaseSensitivity CaseSensitivity { get; }
}

/// <summary>Identifies one matched range in sanitized snippet text.</summary>
public sealed class NotebookBodySearchMatch
{
    internal NotebookBodySearchMatch(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>Gets the zero-based UTF-16 start offset in the snippet text.</summary>
    public int Start { get; }

    /// <summary>Gets the positive UTF-16 length in the snippet text.</summary>
    public int Length { get; }
}

/// <summary>Contains sanitized snippet text and its visible match ranges.</summary>
public sealed class NotebookBodySearchSnippet
{
    internal NotebookBodySearchSnippet(
        string text,
        IReadOnlyList<NotebookBodySearchMatch> matches)
    {
        Text = text;
        Matches = Array.AsReadOnly([.. matches]);
    }

    /// <summary>Gets the sanitized plain text.</summary>
    public string Text { get; }

    /// <summary>Gets ordered, non-overlapping UTF-16 match ranges.</summary>
    public IReadOnlyList<NotebookBodySearchMatch> Matches { get; }
}

/// <summary>Represents one authoritative-body search hit.</summary>
public sealed class NotebookBodySearchHit
{
    internal NotebookBodySearchHit(
        NotebookId notebookId,
        PageId pageId,
        string name,
        DateTimeOffset updateTimeUtc,
        NotebookBodySearchSnippet? snippet)
    {
        NotebookId = notebookId;
        PageId = pageId;
        Name = name;
        UpdateTimeUtc = updateTimeUtc;
        Snippet = snippet;
    }

    /// <summary>Gets the owning notebook identifier.</summary>
    public NotebookId NotebookId { get; }

    /// <summary>Gets the stable page identifier.</summary>
    public PageId PageId { get; }

    /// <summary>Gets the page name.</summary>
    public string Name { get; }

    /// <summary>Gets the UTC last-update timestamp.</summary>
    public DateTimeOffset UpdateTimeUtc { get; }

    /// <summary>Gets the structured snippet, or null when snippet generation was disabled.</summary>
    public NotebookBodySearchSnippet? Snippet { get; }
}

/// <summary>Contains one incrementally read body-search batch.</summary>
public sealed class NotebookBodySearchBatch
{
    internal NotebookBodySearchBatch(
        IReadOnlyList<NotebookBodySearchHit> hits,
        bool isCompleted,
        bool hasMore)
    {
        Hits = Array.AsReadOnly([.. hits]);
        IsCompleted = isCompleted;
        HasMore = hasMore;
    }

    /// <summary>Gets the ordered search hits.</summary>
    public IReadOnlyList<NotebookBodySearchHit> Hits { get; }

    /// <summary>Gets whether the single provider query has reached its terminal state.</summary>
    public bool IsCompleted { get; }

    /// <summary>
    /// Gets whether at least one hit exists beyond the request limit.
    /// This value is false until <see cref="IsCompleted"/> is true.
    /// </summary>
    public bool HasMore { get; }
}

/// <summary>Contains the authoritative values for a new page.</summary>
public sealed class NotebookPageCreateRequest
{
    /// <summary>Initializes a page creation request.</summary>
    /// <param name="name">The unique page name.</param>
    /// <param name="text">The page body.</param>
    /// <param name="tags">The page tag labels.</param>
    public NotebookPageCreateRequest(
        string name,
        string text,
        IReadOnlyList<string> tags)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Tags = tags is null
            ? throw new ArgumentNullException(nameof(tags))
            : Array.AsReadOnly([.. tags]);
    }

    /// <summary>Gets the unique page name.</summary>
    public string Name { get; }

    /// <summary>Gets the page body.</summary>
    public string Text { get; }

    /// <summary>Gets a copy of the page tag labels.</summary>
    public IReadOnlyList<string> Tags { get; }
}

/// <summary>Contains the complete mutable replacement values for a page.</summary>
public sealed class NotebookPageReplaceRequest
{
    /// <summary>Initializes a page replacement request.</summary>
    /// <param name="name">The resulting unique page name.</param>
    /// <param name="text">The resulting page body.</param>
    /// <param name="tags">The resulting page tag labels.</param>
    public NotebookPageReplaceRequest(
        string name,
        string text,
        IReadOnlyList<string> tags)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Tags = tags is null
            ? throw new ArgumentNullException(nameof(tags))
            : Array.AsReadOnly([.. tags]);
    }

    /// <summary>Gets the resulting unique page name.</summary>
    public string Name { get; }

    /// <summary>Gets the resulting page body.</summary>
    public string Text { get; }

    /// <summary>Gets a copy of the resulting page tag labels.</summary>
    public IReadOnlyList<string> Tags { get; }
}

/// <summary>Provides storage-independent current-format page operations.</summary>
public interface INotebookPageApplication
{
    /// <summary>Reads a page by its complete identifier.</summary>
    Task<NotebookOperationResult<NotebookPage>> GetByIdAsync(
        PageId pageId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a page by its exact, binary-compared name.</summary>
    Task<NotebookOperationResult<NotebookPage>> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves one normalized Page ID prefix and rejects ambiguous prefixes.</summary>
    Task<NotebookOperationResult<NotebookPage>> ResolveByIdPrefixAsync(
        string pageIdPrefix,
        CancellationToken cancellationToken = default);

    /// <summary>Counts authoritative pages.</summary>
    Task<NotebookOperationResult<int>> CountAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Lists body-free page summaries using stable name and Page ID ordering.</summary>
    Task<NotebookOperationResult<IReadOnlyList<NotebookPageSummary>>> ListSummariesAsync(
        int skipCount,
        int takeCount,
        CancellationToken cancellationToken = default);

    /// <summary>Queries body-free summaries by a MemoriaNote page-name glob.</summary>
    Task<NotebookOperationResult<IReadOnlyList<NotebookPageSummary>>> QuerySummariesAsync(
        NotebookPageQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Starts one streaming search of authoritative page bodies.</summary>
    Task<NotebookOperationResult<NotebookBodySearchSession>> StartBodySearchAsync(
        NotebookBodySearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a page using production Page ID and timestamp generation.</summary>
    Task<NotebookOperationResult<NotebookPage>> CreateAsync(
        NotebookPageCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces all mutable page values when the expected revision is current.</summary>
    Task<NotebookOperationResult<NotebookPage>> ReplaceAsync(
        PageId pageId,
        long expectedRevision,
        NotebookPageReplaceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a page when the expected revision is current.</summary>
    Task<NotebookOperationResult<bool>> DeleteAsync(
        PageId pageId,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}
