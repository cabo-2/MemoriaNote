using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote
{
    /// <summary>
    /// Persists bulk notebook transfers in SQLite databases.
    /// </summary>
    public sealed class SqliteNotebookTransferRepository : INotebookTransferRepository
    {
        readonly INotebookDbContextFactory _databaseFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteNotebookTransferRepository"/> class.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        public SqliteNotebookTransferRepository(INotebookDbContextFactory databaseFactory)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
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
    }
}
