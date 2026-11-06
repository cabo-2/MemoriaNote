#nullable enable

using System;
using MemoriaNote.Domain.Queries;

namespace MemoriaNote.Domain.Repositories;

internal enum RepositoryPageQuerySort
{
    Name,
    Updated,
    Created
}

internal sealed class RepositoryPageQuery
{
    internal RepositoryPageQuery(
        MemoriaNoteGlob pattern,
        RepositoryPageQuerySort sort,
        bool reverse,
        int? limit)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        if (!Enum.IsDefined(sort))
            throw new ArgumentOutOfRangeException(nameof(sort));
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));

        Sort = sort;
        Reverse = reverse;
        Limit = limit;
    }

    internal MemoriaNoteGlob Pattern { get; }

    internal RepositoryPageQuerySort Sort { get; }

    internal bool Reverse { get; }

    internal int? Limit { get; }
}
