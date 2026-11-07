#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MemoriaNote.Domain.Queries;

namespace MemoriaNote.Domain.Repositories;

internal enum RepositoryBodySearchSort
{
    Name,
    Updated
}

internal sealed class RepositoryBodySearchQuery
{
    internal RepositoryBodySearchQuery(
        BodySearchQuery query,
        RepositoryBodySearchSort sort,
        int limit,
        bool includeSnippet)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        if (!Enum.IsDefined(sort))
            throw new ArgumentOutOfRangeException(nameof(sort));
        if (limit is <= 0 or > MemoriaNote.Application.NotebookBodySearchRequest.MaximumLimit)
            throw new ArgumentOutOfRangeException(nameof(limit));
        Sort = sort;
        Limit = limit;
        IncludeSnippet = includeSnippet;
    }

    internal BodySearchQuery Query { get; }

    internal RepositoryBodySearchSort Sort { get; }

    internal int Limit { get; }

    internal bool IncludeSnippet { get; }
}

internal sealed record RepositoryBodySearchHit(
    NotebookId NotebookId,
    PageId PageId,
    string Name,
    DateTimeOffset UpdateTimeUtc,
    BodySearchSnippet? Snippet);

internal sealed class RepositoryBodySearchBatch
{
    private readonly IReadOnlyList<RepositoryBodySearchHit> _hits;

    internal RepositoryBodySearchBatch(
        IReadOnlyList<RepositoryBodySearchHit> hits,
        bool isCompleted,
        bool hasMore)
    {
        if (hasMore && !isCompleted)
            throw new ArgumentException("An incomplete batch cannot report additional matches.");
        _hits = new ReadOnlyCollection<RepositoryBodySearchHit>([.. hits]);
        IsCompleted = isCompleted;
        HasMore = hasMore;
    }

    internal IReadOnlyList<RepositoryBodySearchHit> Hits => _hits;

    internal bool IsCompleted { get; }

    internal bool HasMore { get; }
}
