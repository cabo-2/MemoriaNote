using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using MemoriaNote.Domain;
using MemoriaNote.Models;
using MemoriaNote.Transfer;

namespace MemoriaNote.Persistence
{
    /// <summary>
    /// Persists bulk notebook transfers in SQLite databases.
    /// </summary>
    public sealed class SqliteNotebookTransferRepository :
        INotebookTransferRepository,
        ITextPageImportRepository
    {
        readonly INotebookDbContextFactory _databaseFactory;
        readonly IClock _clock;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteNotebookTransferRepository"/> class.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        public SqliteNotebookTransferRepository(INotebookDbContextFactory databaseFactory)
            : this(databaseFactory, SystemClock.Instance)
        {
        }

        /// <summary>
        /// Initializes a notebook transfer repository with an explicit clock.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        /// <param name="clock">The clock used for imported page timestamps.</param>
        public SqliteNotebookTransferRepository(
            INotebookDbContextFactory databaseFactory,
            IClock clock)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<Page>> ListPagesAsync(
            NotebookId notebookId,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));

            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(notebookId.Locator);
            return await context.Pages
                .AsNoTracking()
                .OrderBy(page => page.Rowid)
                .ToListAsync(token)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task AddPagesAsync(
            NotebookId notebookId,
            IReadOnlyCollection<Page> pages,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (pages == null)
                throw new ArgumentNullException(nameof(pages));

            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(notebookId.Locator);
            await using var transaction = await context.Database
                .BeginTransactionAsync(token)
                .ConfigureAwait(false);
            context.Pages.AddRange(pages);
            await context.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<TextPageImportResult> ImportAsync(
            NotebookId notebookId,
            IReadOnlyCollection<TextPageImportItem> items,
            TextPageImportConflictPolicy conflictPolicy,
            bool dryRun,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(notebookId.Locator);
            await context.Database.OpenConnectionAsync(token).ConfigureAwait(false);
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: dryRun);
            context.Database.UseTransaction(transaction);

            var readOnlyValue = await context.Metadata
                .AsNoTracking()
                .Where(entry => entry.Key == NoteKeyValue.ReadOnly)
                .Select(entry => entry.Value)
                .SingleOrDefaultAsync(token)
                .ConfigureAwait(false);
            if (bool.TryParse(readOnlyValue, out var isReadOnly) && isReadOnly)
            {
                return TextPageImportResult.Failed(
                    TextPageImportErrorCode.ReadOnly);
            }

            var names = items.Select(item => item.Name).ToList();
            var existingPages = new List<Page>();
            foreach (var nameBatch in names.Chunk(500))
            {
                existingPages.AddRange(await context.Pages
                    .Where(page => nameBatch.Contains(page.Name))
                    .ToListAsync(token)
                    .ConfigureAwait(false));
            }
            var existingByName = existingPages
                .GroupBy(page => page.Name, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.Ordinal);

            var createdCount = 0;
            var replacedCount = 0;
            var skippedCount = 0;
            var changedAt = _clock.UtcNow.UtcDateTime;
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                if (!existingByName.TryGetValue(item.Name, out var matches))
                {
                    context.Pages.Add(Page.CreateAt(item.Name, item.Text, changedAt));
                    createdCount++;
                    continue;
                }

                switch (conflictPolicy)
                {
                    case TextPageImportConflictPolicy.Fail:
                        return TextPageImportResult.Failed(
                            TextPageImportErrorCode.ExistingPageConflict);
                    case TextPageImportConflictPolicy.Skip:
                        skippedCount++;
                        break;
                    case TextPageImportConflictPolicy.Replace:
                        if (matches.Count != 1)
                        {
                            return TextPageImportResult.Failed(
                                TextPageImportErrorCode.AmbiguousPageConflict);
                        }

                        matches[0].Text = item.Text;
                        matches[0].UpdateLastModified(changedAt);
                        replacedCount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(conflictPolicy));
                }
            }

            if (!dryRun)
            {
                await context.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
            }

            return TextPageImportResult.Succeeded(
                createdCount,
                replacedCount,
                skippedCount);
        }
    }
}
