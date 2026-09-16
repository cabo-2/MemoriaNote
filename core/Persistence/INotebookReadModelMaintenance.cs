using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Persistence
{
    /// <summary>
    /// Checks and rebuilds the derived read models of a notebook database.
    /// </summary>
    public interface INotebookReadModelMaintenance
    {
        /// <summary>
        /// Checks the read models against the authoritative Pages table.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>A report describing all detected inconsistencies.</returns>
        Task<ReadModelIntegrityReport> CheckIntegrityAsync(
            string databasePath,
            CancellationToken token);

        /// <summary>
        /// Rebuilds the read models from Pages and checks the rebuilt state.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>An integrity report describing the resulting persisted state.</returns>
        Task<ReadModelIntegrityReport> RebuildReadModelsAsync(
            string databasePath,
            CancellationToken token);
    }
}
