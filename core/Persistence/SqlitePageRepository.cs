using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote
{
    /// <summary>
    /// Persists page use cases in SQLite notebook databases.
    /// </summary>
    public sealed class SqlitePageRepository : IPageRepository
    {
        readonly INotebookDbContextFactory _databaseFactory;
        readonly IClock _clock;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlitePageRepository"/> class.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        public SqlitePageRepository(INotebookDbContextFactory databaseFactory)
            : this(databaseFactory, SystemClock.Instance)
        {
        }

        /// <summary>
        /// Initializes a new instance with explicit database and time providers.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        /// <param name="clock">The clock used for persisted page timestamps.</param>
        public SqlitePageRepository(
            INotebookDbContextFactory databaseFactory,
            IClock clock)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <inheritdoc/>
        public Task<Page> FindPageAsync(
            string databasePath,
            Guid pageId,
            CancellationToken token)
        {
            return ReadPageByUuidAsync(databasePath, pageId.ToUuid(), token);
        }

        /// <inheritdoc/>
        public Task<Page> FindPageAsync(
            string databasePath,
            PageId pageId,
            CancellationToken token)
        {
            if (pageId == null)
                throw new ArgumentNullException(nameof(pageId));

            return ReadPageByUuidAsync(databasePath, pageId.ToUuid(), token);
        }

        async Task<Page> ReadPageByUuidAsync(
            string databasePath,
            string uuid,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            var page = await context.Pages
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Uuid == uuid, token)
                .ConfigureAwait(false);
            return page;
        }

        /// <inheritdoc/>
        public async Task<Page> FindPageAsync(
            string databasePath,
            string heading,
            int ordinal,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            var page = await context.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    candidate => candidate.Name == heading && candidate.Index == ordinal,
                    token)
                .ConfigureAwait(false);
            return page;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<Page>> ListPagesByHeadingAsync(
            string databasePath,
            string heading,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            var pages = await context.Pages
                .AsNoTracking()
                .Where(page => page.Name == heading)
                .OrderBy(page => page.Index)
                .ThenBy(page => page.Rowid)
                .ToListAsync(token)
                .ConfigureAwait(false);
            return pages;
        }

        /// <inheritdoc/>
        public async Task<Page> CreatePageAsync(
            string databasePath,
            string heading,
            string body,
            string directory,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            await using var transaction = await context.Database
                .BeginTransactionAsync(token)
                .ConfigureAwait(false);
            var pages = await ReadTrackedHeadingGroupAsync(context, heading, token)
                .ConfigureAwait(false);
            var page = Page.CreateAt(
                heading,
                body,
                _clock.UtcNow.UtcDateTime,
                directory);
            page.Index = pages.Select(candidate => candidate.Index).DefaultIfEmpty().Max() + 1;
            context.Pages.Add(page);
            pages.Add(page);
            NormalizeHeadingOrdinals(pages);

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return page;
        }

        /// <inheritdoc/>
        public async Task<Page> UpdatePageAsync(
            string databasePath,
            Page page,
            CancellationToken token)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));

            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            await using var transaction = await context.Database
                .BeginTransactionAsync(token)
                .ConfigureAwait(false);
            var uuid = PageId.FromGuid(page.Guid).ToUuid();
            var persistedPage = await context.Pages
                .SingleOrDefaultAsync(candidate => candidate.Uuid == uuid, token)
                .ConfigureAwait(false);
            if (persistedPage == null)
                throw new KeyNotFoundException($"Page '{page.Guid:D}' was not found.");

            var beforeHeading = persistedPage.Name;
            var beforeOrdinal = persistedPage.Index;
            var sourceHeadingPages = await ReadTrackedHeadingGroupAsync(context, beforeHeading, token)
                .ConfigureAwait(false);
            var headingChanged = !string.Equals(page.Name, beforeHeading, StringComparison.Ordinal);
            var destinationHeadingPages = headingChanged
                ? await ReadTrackedHeadingGroupAsync(context, page.Name, token)
                    .ConfigureAwait(false)
                : sourceHeadingPages;

            CopyMutableValues(page, persistedPage);
            persistedPage.UpdateLastModified(_clock.UtcNow.UtcDateTime);

            if (headingChanged)
            {
                NormalizeHeadingOrdinals(
                    sourceHeadingPages.Where(candidate => candidate.Guid != persistedPage.Guid));
                NormalizeHeadingOrdinals(destinationHeadingPages);
                persistedPage.Index = destinationHeadingPages.Count + 1;
            }
            else
            {
                persistedPage.Index = beforeOrdinal;
                NormalizeHeadingOrdinals(sourceHeadingPages);
            }

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return persistedPage;
        }

        /// <inheritdoc/>
        public Task DeletePageAsync(
            string databasePath,
            Guid pageId,
            CancellationToken token)
        {
            return DeletePageIgnoringAbsenceAsync(databasePath, pageId.ToUuid(), token);
        }

        /// <inheritdoc/>
        public Task DeletePageAsync(
            string databasePath,
            PageId pageId,
            CancellationToken token)
        {
            if (pageId == null)
                throw new ArgumentNullException(nameof(pageId));

            return DeletePageIgnoringAbsenceAsync(databasePath, pageId.ToUuid(), token);
        }

        /// <inheritdoc/>
        public Task<bool> TryDeletePageAsync(
            NotebookId notebookId,
            PageId pageId,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (pageId == null)
                throw new ArgumentNullException(nameof(pageId));

            return TryDeletePageByUuidAsync(notebookId.Locator, pageId.ToUuid(), token);
        }

        async Task DeletePageIgnoringAbsenceAsync(
            string databasePath,
            string uuid,
            CancellationToken token)
        {
            await TryDeletePageByUuidAsync(databasePath, uuid, token).ConfigureAwait(false);
        }

        async Task<bool> TryDeletePageByUuidAsync(
            string databasePath,
            string uuid,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            await using var transaction = await context.Database
                .BeginTransactionAsync(token)
                .ConfigureAwait(false);
            var page = await context.Pages
                .SingleOrDefaultAsync(candidate => candidate.Uuid == uuid, token)
                .ConfigureAwait(false);
            if (page == null)
            {
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return false;
            }

            var pages = await ReadTrackedHeadingGroupAsync(context, page.Name, token)
                .ConfigureAwait(false);
            context.Pages.Remove(page);
            NormalizeHeadingOrdinals(
                pages.Where(candidate => candidate.Guid != page.Guid));

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public async Task<int> CountPagesAsync(string databasePath, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            return await context.Contents.CountAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<PageSummary>> ListPageSummariesAsync(
            string databasePath,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            var contents = await context.Contents
                .AsNoTracking()
                .OrderBy(content => content.Rowid)
                .Skip(skipCount)
                .Take(takeCount)
                .ToListAsync(token)
                .ConfigureAwait(false);
            var notebookId = NotebookId.FromDatabasePath(context.DatabasePath);
            return contents
                .Select(record => PageSummaryMapper.FromRecord(notebookId, record))
                .ToList()
                .AsReadOnly();
        }

        static Task<List<Page>> ReadTrackedHeadingGroupAsync(
            NotebookDbContext context,
            string heading,
            CancellationToken token)
        {
            return context.Pages
                .Where(page => page.Name == heading)
                .OrderBy(page => page.Index)
                .ThenBy(page => page.Rowid)
                .ToListAsync(token);
        }

        static void CopyMutableValues(Page source, Page destination)
        {
            destination.Name = source.Name;
            destination.Tags = source.Tags;
            destination.ContentType = source.ContentType;
            destination.CreateTime = source.CreateTime;
            destination.IsErased = source.IsErased;
            destination.Text = source.Text;
        }

        static void NormalizeHeadingOrdinals(IEnumerable<Page> pages)
        {
            int ordinal = 1;
            foreach (var page in pages
                .OrderBy(page => page.Index)
                .ThenBy(page => page.Rowid))
            {
                page.Index = ordinal;
                ordinal++;
            }
        }

    }
}
