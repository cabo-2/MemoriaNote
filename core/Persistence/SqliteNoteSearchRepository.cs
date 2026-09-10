using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote
{
    /// <summary>
    /// Searches note databases using SQLite LIKE and FTS5 queries.
    /// </summary>
    public sealed class SqliteNoteSearchRepository : INoteSearchRepository
    {
        readonly INoteDatabaseFactory _databaseFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteNoteSearchRepository"/> class.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        public SqliteNoteSearchRepository(INoteDatabaseFactory databaseFactory)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
        }

        /// <inheritdoc/>
        public async Task<SearchResult> SearchAsync(
            string dataSource,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            var result = await SearchPageSummariesAsync(
                    dataSource,
                    searchEntry,
                    searchMethod,
                    skipCount,
                    takeCount,
                    token)
                .ConfigureAwait(false);
            return new SearchResult()
            {
                Contents = result.PageSummaries
                    .Select(PageSummaryContentAdapter.ToContent)
                    .ToList(),
                Count = result.Count,
                StartTime = result.StartTime,
                EndTime = result.EndTime
            };
        }

        /// <inheritdoc/>
        public async Task<NoteSearchResult> SearchPageSummariesAsync(
            string dataSource,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var startTime = DateTime.UtcNow;
            using var context = _databaseFactory.Create(dataSource);
            var query = CreateQuery(context, searchEntry, searchMethod);
            var count = await query.CountAsync(token);
            var contents = await query.ReadAsync(skipCount, takeCount, token);
            var noteId = NoteId.FromDataSource(context.DataSource);
            var summaries = contents
                .Select(content => PageSummaryMapper.FromContent(noteId, content))
                .ToList();

            return new NoteSearchResult(
                summaries,
                count,
                startTime,
                DateTime.UtcNow);
        }

        /// <inheritdoc/>
        public async Task<int> CountAsync(
            string dataSource,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            var query = CreateQuery(context, searchEntry, searchMethod);
            return await query.CountAsync(token);
        }

        static SqliteSearchQuery CreateQuery(
            NoteDbContext context,
            string searchEntry,
            SearchMethodType searchMethod)
        {
            var pattern = SqliteSearchPattern.Create(searchEntry);

            if (searchMethod == SearchMethodType.Heading)
                return CreateHeadingQuery(context, pattern);

            return CreateFullTextQuery(context, pattern);
        }

        static SqliteSearchQuery CreateHeadingQuery(
            NoteDbContext context,
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
            NoteDbContext context,
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

            internal Task<int> CountAsync(CancellationToken token)
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
