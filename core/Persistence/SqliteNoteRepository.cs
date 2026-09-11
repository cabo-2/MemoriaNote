using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote
{
    /// <summary>
    /// Persists page use cases in SQLite note databases.
    /// </summary>
    public sealed class SqliteNoteRepository : INoteRepository
    {
        readonly INoteDatabaseFactory _databaseFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteNoteRepository"/> class.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        public SqliteNoteRepository(INoteDatabaseFactory databaseFactory)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
        }

        /// <inheritdoc/>
        public Task<Page> ReadPageAsync(
            string dataSource,
            Guid pageId,
            CancellationToken token)
        {
            return ReadPageByUuidAsync(dataSource, pageId.ToUuid(), token);
        }

        /// <inheritdoc/>
        public Task<Page> ReadPageAsync(
            string dataSource,
            PageId pageId,
            CancellationToken token)
        {
            if (pageId == null)
                throw new ArgumentNullException(nameof(pageId));

            return ReadPageByUuidAsync(dataSource, pageId.ToUuid(), token);
        }

        async Task<Page> ReadPageByUuidAsync(
            string dataSource,
            string uuid,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            var page = await context.Pages
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Uuid == uuid, token)
                .ConfigureAwait(false);
            return SetOwner(page, context.DataSource);
        }

        /// <inheritdoc/>
        public async Task<Page> ReadPageAsync(
            string dataSource,
            string name,
            int index,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            var page = await context.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    candidate => candidate.Name == name && candidate.Index == index,
                    token)
                .ConfigureAwait(false);
            return SetOwner(page, context.DataSource);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<Page>> ReadPagesAsync(
            string dataSource,
            string name,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            var pages = await context.Pages
                .AsNoTracking()
                .Where(page => page.Name == name)
                .OrderBy(page => page.Index)
                .ThenBy(page => page.Rowid)
                .ToListAsync(token)
                .ConfigureAwait(false);
            pages.ForEach(page => SetOwner(page, context.DataSource));
            return pages;
        }

        /// <inheritdoc/>
        public async Task<Page> CreatePageAsync(
            string dataSource,
            string name,
            string text,
            string directory,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            await using var transaction = await context.Database
                .BeginTransactionAsync(token)
                .ConfigureAwait(false);
            var pages = await ReadTrackedNameGroupAsync(context, name, token)
                .ConfigureAwait(false);
            var page = Page.Create(name, text, directory);
            page.Index = pages.Select(candidate => candidate.Index).DefaultIfEmpty().Max() + 1;
            context.Pages.Add(page);
            pages.Add(page);
            NormalizePageIndexes(pages);

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return SetOwner(page, context.DataSource);
        }

        /// <inheritdoc/>
        public async Task<Page> UpdatePageAsync(
            string dataSource,
            Page page,
            CancellationToken token)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));

            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            await using var transaction = await context.Database
                .BeginTransactionAsync(token)
                .ConfigureAwait(false);
            var uuid = PageId.FromGuid(page.Guid).ToUuid();
            var persistedPage = await context.Pages
                .SingleOrDefaultAsync(candidate => candidate.Uuid == uuid, token)
                .ConfigureAwait(false);
            if (persistedPage == null)
                throw new KeyNotFoundException($"Page '{page.Guid:D}' was not found.");

            var beforeName = persistedPage.Name;
            var beforeIndex = persistedPage.Index;
            var sourcePages = await ReadTrackedNameGroupAsync(context, beforeName, token)
                .ConfigureAwait(false);
            var nameChanged = !string.Equals(page.Name, beforeName, StringComparison.Ordinal);
            var destinationPages = nameChanged
                ? await ReadTrackedNameGroupAsync(context, page.Name, token)
                    .ConfigureAwait(false)
                : sourcePages;

            CopyMutableValues(page, persistedPage);
            persistedPage.UpdateLastModified();

            if (nameChanged)
            {
                NormalizePageIndexes(
                    sourcePages.Where(candidate => candidate.Guid != persistedPage.Guid));
                NormalizePageIndexes(destinationPages);
                persistedPage.Index = destinationPages.Count + 1;
            }
            else
            {
                persistedPage.Index = beforeIndex;
                NormalizePageIndexes(sourcePages);
            }

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return SetOwner(persistedPage, context.DataSource);
        }

        /// <inheritdoc/>
        public Task DeletePageAsync(
            string dataSource,
            Guid pageId,
            CancellationToken token)
        {
            return DeletePageIgnoringAbsenceAsync(dataSource, pageId.ToUuid(), token);
        }

        /// <inheritdoc/>
        public Task DeletePageAsync(
            string dataSource,
            PageId pageId,
            CancellationToken token)
        {
            if (pageId == null)
                throw new ArgumentNullException(nameof(pageId));

            return DeletePageIgnoringAbsenceAsync(dataSource, pageId.ToUuid(), token);
        }

        /// <inheritdoc/>
        public Task<bool> TryDeletePageAsync(
            NoteId noteId,
            PageId pageId,
            CancellationToken token)
        {
            if (noteId == null)
                throw new ArgumentNullException(nameof(noteId));
            if (pageId == null)
                throw new ArgumentNullException(nameof(pageId));

            return TryDeletePageByUuidAsync(noteId.Locator, pageId.ToUuid(), token);
        }

        async Task DeletePageIgnoringAbsenceAsync(
            string dataSource,
            string uuid,
            CancellationToken token)
        {
            await TryDeletePageByUuidAsync(dataSource, uuid, token).ConfigureAwait(false);
        }

        async Task<bool> TryDeletePageByUuidAsync(
            string dataSource,
            string uuid,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
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

            var pages = await ReadTrackedNameGroupAsync(context, page.Name, token)
                .ConfigureAwait(false);
            context.Pages.Remove(page);
            NormalizePageIndexes(
                pages.Where(candidate => candidate.Guid != page.Guid));

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public async Task<int> CountAsync(string dataSource, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            return await context.Contents.CountAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<Content>> ReadContentsAsync(
            string dataSource,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            var summaries = await ReadPageSummariesAsync(
                    dataSource,
                    skipCount,
                    takeCount,
                    token)
                .ConfigureAwait(false);
            return summaries
                .Select(PageSummaryContentAdapter.ToContent)
                .ToList();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<PageSummary>> ReadPageSummariesAsync(
            string dataSource,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            var contents = await context.Contents
                .AsNoTracking()
                .OrderBy(content => content.Rowid)
                .Skip(skipCount)
                .Take(takeCount)
                .ToListAsync(token)
                .ConfigureAwait(false);
            var noteId = NoteId.FromDataSource(context.DataSource);
            return contents
                .Select(content => PageSummaryMapper.FromContent(noteId, content))
                .ToList()
                .AsReadOnly();
        }

        static Task<List<Page>> ReadTrackedNameGroupAsync(
            NoteDbContext context,
            string name,
            CancellationToken token)
        {
            return context.Pages
                .Where(page => page.Name == name)
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

        static void NormalizePageIndexes(IEnumerable<Page> pages)
        {
            int index = 1;
            foreach (var page in pages
                .OrderBy(page => page.Index)
                .ThenBy(page => page.Rowid))
            {
                page.Index = index;
                index++;
            }
        }

        static T SetOwner<T>(T content, string dataSource) where T : class, IContent
        {
            if (content != null)
                content.OwnerDataSource = dataSource;

            return content;
        }
    }
}
