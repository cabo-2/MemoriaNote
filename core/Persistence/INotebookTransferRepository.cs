using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Provides bulk page persistence required by notebook transfer operations.
    /// </summary>
    public interface INotebookTransferRepository
    {
        /// <summary>
        /// Reads every page from a notebook in stable storage order.
        /// </summary>
        /// <param name="notebookId">The notebook to read.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The materialized pages.</returns>
        Task<IReadOnlyList<Page>> ListPagesAsync(
            NotebookId notebookId,
            CancellationToken token);

        /// <summary>
        /// Adds a complete set of transferred pages to a notebook atomically.
        /// </summary>
        /// <param name="notebookId">The notebook to update.</param>
        /// <param name="pages">The pages to add.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>A task representing the database operation.</returns>
        Task AddPagesAsync(
            NotebookId notebookId,
            IReadOnlyCollection<Page> pages,
            CancellationToken token);
    }
}
