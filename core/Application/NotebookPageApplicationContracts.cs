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
