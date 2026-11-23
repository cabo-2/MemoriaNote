using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Transfer;

namespace MemoriaNote.Persistence
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
        Task<IReadOnlyList<TextPageExportItem>> ListPagesAsync(
            NotebookId notebookId,
            CancellationToken token);
    }
}
