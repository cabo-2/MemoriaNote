#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Queries;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Searches authoritative Schema V2 page bodies with literal SQLite scans.</summary>
internal sealed class SchemaV2BodySearchRepository : INotebookBodySearchReader
{
    public async Task<RepositoryBodySearchResult> SearchAsync(
        NotebookId notebookId,
        RepositoryBodySearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                notebookId.Locator,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var statement = new StringBuilder(query.IncludeSnippet
            ? "SELECT PageId, Name, UpdateTime, Text FROM Pages WHERE "
            : "SELECT PageId, Name, UpdateTime FROM Pages WHERE ");
        for (var index = 0; index < query.Query.Keywords.Count; index++)
        {
            if (index > 0)
                statement.Append(" AND ");
            var parameterName = $"$keyword{index}";
            statement.Append("instr(Text, ").Append(parameterName).Append(") > 0");
            command.Parameters.AddWithValue(parameterName, query.Query.Keywords[index]);
        }
        statement.Append(" ORDER BY ").Append(OrderBy(query.Sort));
        statement.Append(" LIMIT $fetchCount;");
        command.Parameters.AddWithValue("$fetchCount", query.Limit + 1);
        command.CommandText = statement.ToString();

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var hits = new List<RepositoryBodySearchHit>(query.Limit);
        var hasMore = false;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (hits.Count == query.Limit)
            {
                hasMore = true;
                break;
            }

            hits.Add(new RepositoryBodySearchHit(
                notebookId,
                PageId.FromUuid(reader.GetString(0)),
                reader.GetString(1),
                SchemaV2PageValueCodec.ParseTimestamp(reader.GetString(2)),
                query.IncludeSnippet
                    ? BodySearchSnippetBuilder.Build(reader.GetString(3), query.Query.Keywords)
                    : null));
        }

        return new RepositoryBodySearchResult(hits.AsReadOnly(), hasMore);
    }

    private static string OrderBy(RepositoryBodySearchSort sort) => sort switch
    {
        RepositoryBodySearchSort.Name =>
            "Name COLLATE BINARY ASC, PageId COLLATE BINARY ASC",
        RepositoryBodySearchSort.Updated =>
            "UpdateTime DESC, PageId COLLATE BINARY ASC",
        _ => throw new ArgumentOutOfRangeException(nameof(sort))
    };
}
