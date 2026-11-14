#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Queries;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Identifies one fixed schema v2 FTS5 body-search candidate.</summary>
internal enum SchemaV2FtsBodySearchKind
{
    Unicode61,
    Trigram
}

/// <summary>Searches one fixed schema v2 FTS5 index without exposing provider syntax.</summary>
internal sealed class SchemaV2FtsBodySearchRepository : INotebookBodySearchReader
{
    private const int SnippetTokenCount = 64;
    private readonly SchemaV2FtsBodySearchKind _kind;

    internal SchemaV2FtsBodySearchRepository(SchemaV2FtsBodySearchKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        _kind = kind;
    }

    public async Task<INotebookBodySearchCursor> StartAsync(
        NotebookId notebookId,
        RepositoryBodySearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notebookId);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateQuery(query);

        var tableName = TableName(_kind);
        var startMarker = $"\uFDD0mn-start-{Guid.NewGuid():N}\uFDD1";
        var endMarker = $"\uFDD0mn-end-{Guid.NewGuid():N}\uFDD1";
        SqliteConnection? connection = null;
        SqliteCommand? command = null;
        SqliteDataReader? reader = null;
        try
        {
            connection = await SqliteConnectionFactory.OpenAsync(
                    notebookId.Locator,
                    SqliteOpenMode.ReadOnly,
                    cancellationToken)
                .ConfigureAwait(false);
            command = connection.CreateCommand();
            command.CommandText = query.IncludeSnippet
                ? $"""
                    SELECT
                        Pages.PageId,
                        Pages.Name,
                        Pages.UpdateTime,
                        snippet(
                            "{tableName}",
                            1,
                            $startMarker,
                            $endMarker,
                            $ellipsis,
                            $snippetTokenCount),
                        instr(Pages.Text, $startMarker) > 0
                            OR instr(Pages.Text, $endMarker) > 0
                    FROM "{tableName}"
                    INNER JOIN Pages ON Pages.InternalId = "{tableName}".rowid
                    WHERE "{tableName}".Text MATCH $matchQuery
                    ORDER BY {OrderBy(query.Sort, tableName)}
                    LIMIT $fetchCount;
                    """
                : $"""
                    SELECT Pages.PageId, Pages.Name, Pages.UpdateTime
                    FROM "{tableName}"
                    INNER JOIN Pages ON Pages.InternalId = "{tableName}".rowid
                    WHERE "{tableName}".Text MATCH $matchQuery
                    ORDER BY {OrderBy(query.Sort, tableName)}
                    LIMIT $fetchCount;
                    """;
            command.Parameters.Add("$matchQuery", SqliteType.Text).Value =
                Fts5BodySearchQueryCompiler.Compile(query.Query);
            command.Parameters.AddWithValue("$fetchCount", query.Limit + 1);
            if (query.IncludeSnippet)
            {
                command.Parameters.Add("$startMarker", SqliteType.Text).Value = startMarker;
                command.Parameters.Add("$endMarker", SqliteType.Text).Value = endMarker;
                command.Parameters.Add("$ellipsis", SqliteType.Text).Value = " … ";
                command.Parameters.AddWithValue("$snippetTokenCount", SnippetTokenCount);
            }

            try
            {
                reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SqliteException exception) when (IsIndexFailure(exception))
            {
                throw new NotebookBodySearchIndexException(exception);
            }

            return new SchemaV2BodySearchCursor(
                query.Limit,
                connection,
                command,
                reader,
                activeReader => new RepositoryBodySearchHit(
                    notebookId,
                    PageId.FromUuid(activeReader.GetString(0)),
                    activeReader.GetString(1),
                    SchemaV2PageValueCodec.ParseTimestamp(activeReader.GetString(2)),
                    query.IncludeSnippet
                        ? Fts5BodySearchSnippetParser.Parse(
                            activeReader.GetString(3),
                            startMarker,
                            endMarker,
                            activeReader.GetInt64(4) != 0)
                        : null),
                TranslateFailure);
        }
        catch
        {
            if (reader is not null)
                await reader.DisposeAsync().ConfigureAwait(false);
            if (command is not null)
                await command.DisposeAsync().ConfigureAwait(false);
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void ValidateQuery(RepositoryBodySearchQuery query)
    {
        if (query.CaseSensitivity == RepositoryBodySearchCaseSensitivity.Sensitive)
            throw new NotebookBodySearchCaseSensitivityException();

        if (_kind == SchemaV2FtsBodySearchKind.Trigram &&
            query.Query.Keywords.Any(keyword => keyword.EnumerateRunes().Count() < 3))
        {
            throw new NotebookBodySearchValidationException(
                "Trigram body-search keywords must contain at least three Unicode scalars.");
        }
    }

    private static string TableName(SchemaV2FtsBodySearchKind kind) => kind switch
    {
        SchemaV2FtsBodySearchKind.Unicode61 => "FtsIndexUnicode61",
        SchemaV2FtsBodySearchKind.Trigram => "FtsIndexTrigram",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string OrderBy(RepositoryBodySearchSort sort, string tableName) => sort switch
    {
        RepositoryBodySearchSort.Name =>
            "Pages.Name COLLATE BINARY ASC, Pages.PageId COLLATE BINARY ASC",
        RepositoryBodySearchSort.Updated =>
            "Pages.UpdateTime DESC, Pages.PageId COLLATE BINARY ASC",
        RepositoryBodySearchSort.Relevance =>
            $"\"{tableName}\".rank ASC, Pages.PageId COLLATE BINARY ASC",
        _ => throw new ArgumentOutOfRangeException(nameof(sort))
    };

    private static Exception? TranslateFailure(Exception exception) =>
        exception is SqliteException sqliteException && IsIndexFailure(sqliteException)
            ? new NotebookBodySearchIndexException(sqliteException)
            : null;

    private static bool IsIndexFailure(SqliteException exception) =>
        exception.SqliteErrorCode is 1 or 11 or 17 or 26;
}
