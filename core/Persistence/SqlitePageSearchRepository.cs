using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote
{
    /// <summary>
    /// Searches notebook databases using SQLite LIKE and FTS5 queries.
    /// </summary>
    public sealed class SqlitePageSearchRepository : IPageSearchRepository
    {
        readonly INotebookDbContextFactory _databaseFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlitePageSearchRepository"/> class.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        public SqlitePageSearchRepository(INotebookDbContextFactory databaseFactory)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
        }

        /// <inheritdoc/>
        public async Task<SearchResult> SearchAsync(
            string databasePath,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            var startTime = DateTime.UtcNow;
            var request = SearchRequest.ForNotebook(
                searchEntry,
                searchMethod,
                NotebookId.FromDatabasePath(databasePath),
                skipCount,
                takeCount);
            var result = await new SearchUseCase(this)
                .SearchAsync(request, token)
                .ConfigureAwait(false);
            return new SearchResult()
            {
                Contents = result.Items
                    .Select(PageSummaryContentAdapter.ToContent)
                    .ToList(),
                Count = result.TotalCount,
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };
        }

        /// <inheritdoc/>
        public async Task<NoteSearchResult> SearchPageSummariesAsync(
            string databasePath,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var startTime = DateTime.UtcNow;
            using var context = _databaseFactory.CreateDbContext(databasePath);
            var query = CreateQuery(context, searchEntry, searchMethod);
            var count = await query.CountMatchesAsync(token);
            var contents = await query.ReadAsync(skipCount, takeCount, token);
            var notebookId = NotebookId.FromDatabasePath(context.DatabasePath);
            var summaries = contents
                .Select(content => PageSummaryMapper.FromContent(notebookId, content))
                .ToList();

            return new NoteSearchResult(
                summaries,
                count,
                startTime,
                DateTime.UtcNow);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<PageSummary>> SearchPageSummariesAsync(
            NotebookId notebookId,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));

            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(notebookId.Locator);
            var query = CreateQuery(context, searchEntry, searchMethod);
            var contents = await query.ReadAsync(skipCount, takeCount, token);
            return contents
                .Select(content => PageSummaryMapper.FromContent(notebookId, content))
                .ToList();
        }

        /// <inheritdoc/>
        public async Task<int> CountMatchesAsync(
            string databasePath,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            var query = CreateQuery(context, searchEntry, searchMethod);
            return await query.CountMatchesAsync(token);
        }

        static SqliteSearchQuery CreateQuery(
            NotebookDbContext context,
            string searchEntry,
            SearchMethodType searchMethod)
        {
            var pattern = SqliteSearchPattern.Create(searchEntry);

            if (searchMethod == SearchMethodType.Heading)
                return CreateHeadingQuery(context, pattern);

            return CreateFullTextQuery(context, pattern);
        }

        static SqliteSearchQuery CreateHeadingQuery(
            NotebookDbContext context,
            SqliteSearchPattern pattern)
        {
            if (pattern.MatchingType == MatchingType.None)
                return SqliteSearchQuery.FromContents(context.Contents);

            if (pattern.MatchingType == MatchingType.Partial)
            {
                var contents = context.Contents.Where(content =>
                    EF.Functions.Like(content.Name, pattern.Pattern, "\\"));
                return SqliteSearchQuery.FromContents(contents);
            }

            var ftsQuery = $"Name : \"{pattern.Pattern}\"";
            var pages = context.Pages.FromSqlInterpolated($@"
                SELECT p.* FROM Pages p
                JOIN (
                    SELECT rowid FROM FtsIndex
                    WHERE FtsIndex MATCH {ftsQuery}
                ) f ON p.Rowid = f.rowid
                WHERE p.Name LIKE {pattern.Pattern} ESCAPE '\'");
            return SqliteSearchQuery.FromPages(pages);
        }

        static SqliteSearchQuery CreateFullTextQuery(
            NotebookDbContext context,
            SqliteSearchPattern pattern)
        {
            if (pattern.MatchingType == MatchingType.None)
                return SqliteSearchQuery.FromContents(context.Contents);

            var ftsQuery = $"Text : \"{pattern.Pattern}\"";
            var pages = context.Pages.FromSqlInterpolated($@"
                SELECT p.* FROM Pages p
                JOIN (
                    SELECT rowid FROM FtsIndex
                    WHERE FtsIndex MATCH {ftsQuery}
                ) f ON p.Rowid = f.rowid");
            return SqliteSearchQuery.FromPages(pages);
        }

        sealed class SqliteSearchQuery
        {
            readonly IQueryable<Content> _contents;
            readonly IQueryable<Page> _pages;

            SqliteSearchQuery(IQueryable<Content> contents, IQueryable<Page> pages)
            {
                _contents = contents;
                _pages = pages;
            }

            internal static SqliteSearchQuery FromContents(IQueryable<Content> contents)
            {
                return new SqliteSearchQuery(contents, null);
            }

            internal static SqliteSearchQuery FromPages(IQueryable<Page> pages)
            {
                return new SqliteSearchQuery(null, pages);
            }

            internal Task<int> CountMatchesAsync(CancellationToken token)
            {
                if (_contents != null)
                    return _contents.CountAsync(token);

                return _pages.CountAsync(token);
            }

            internal async Task<List<Content>> ReadAsync(
                int skipCount,
                int takeCount,
                CancellationToken token)
            {
                if (_contents != null)
                {
                    return await _contents
                        .OrderBy(content => EF.Functions.Collate(content.Name, "NOCASE"))
                        .ThenBy(content => content.Index)
                        .Skip(skipCount)
                        .Take(takeCount)
                        .ToListAsync(token);
                }

                var pages = await _pages
                    .OrderBy(page => EF.Functions.Collate(page.Name, "NOCASE"))
                    .ThenBy(page => page.Index)
                    .Skip(skipCount)
                    .Take(takeCount)
                    .ToListAsync(token);
                return pages.ConvertAll(page => page.GetContent());
            }
        }

        sealed class SqliteSearchPattern
        {
            SqliteSearchPattern(string pattern, MatchingType matchingType)
            {
                Pattern = pattern;
                MatchingType = matchingType;
            }

            internal string Pattern { get; }

            internal MatchingType MatchingType { get; }

            internal static SqliteSearchPattern Create(string searchEntry)
            {
                if (string.IsNullOrWhiteSpace(searchEntry))
                    return new SqliteSearchPattern(string.Empty, MatchingType.None);

                var pattern = searchEntry.Trim()
                    .Replace("\"", "\"\"")
                    .Replace("%", "\\%")
                    .Replace("_", "\\_")
                    .Replace("*", "%")
                    .Replace("?", "_");
                var matchingType = searchEntry.Contains("*") || searchEntry.Contains("?")
                    ? MatchingType.Partial
                    : MatchingType.Exact;
                return new SqliteSearchPattern(pattern, matchingType);
            }
        }
    }
}
