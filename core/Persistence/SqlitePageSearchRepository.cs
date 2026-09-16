using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MemoriaNote;
using MemoriaNote.Domain;
using MemoriaNote.Models;

namespace MemoriaNote.Persistence
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
        public async Task<IReadOnlyList<PageSummary>> SearchAsync(
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
            return await query.ReadAsync(
                    notebookId,
                    skipCount,
                    takeCount,
                    token)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<int> CountMatchesAsync(
            NotebookId notebookId,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));

            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(notebookId.Locator);
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
            if (pattern.MatchingType == SqliteSearchMatchType.None)
                return SqliteSearchQuery.FromContents(context.Contents);

            if (pattern.MatchingType == SqliteSearchMatchType.Partial)
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
            if (pattern.MatchingType == SqliteSearchMatchType.None)
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
            readonly IQueryable<PageSummaryRecord> _contents;
            readonly IQueryable<Page> _pages;

            SqliteSearchQuery(
                IQueryable<PageSummaryRecord> contents,
                IQueryable<Page> pages)
            {
                _contents = contents;
                _pages = pages;
            }

            internal static SqliteSearchQuery FromContents(
                IQueryable<PageSummaryRecord> contents)
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

            internal async Task<IReadOnlyList<PageSummary>> ReadAsync(
                NotebookId notebookId,
                int skipCount,
                int takeCount,
                CancellationToken token)
            {
                if (_contents != null)
                {
                    var records = await _contents
                        .OrderBy(content => EF.Functions.Collate(content.Name, "NOCASE"))
                        .ThenBy(content => content.Index)
                        .Skip(skipCount)
                        .Take(takeCount)
                        .ToListAsync(token)
                        .ConfigureAwait(false);
                    return records
                        .Select(record => PageSummaryMapper.FromRecord(notebookId, record))
                        .ToList();
                }

                var pages = await _pages
                    .OrderBy(page => EF.Functions.Collate(page.Name, "NOCASE"))
                    .ThenBy(page => page.Index)
                    .Skip(skipCount)
                    .Take(takeCount)
                    .ToListAsync(token);
                return pages
                    .Select(page => PageSummaryMapper.FromPage(notebookId, page))
                    .ToList();
            }
        }

    }
}
