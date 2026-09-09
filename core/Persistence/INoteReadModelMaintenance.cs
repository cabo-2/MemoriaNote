using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Checks and rebuilds the derived read models of a note database.
    /// </summary>
    public interface INoteReadModelMaintenance
    {
        /// <summary>
        /// Checks the read models against the authoritative Pages table.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>A report describing all detected inconsistencies.</returns>
        Task<ReadModelIntegrityReport> CheckIntegrityAsync(
            string dataSource,
            CancellationToken token);

        /// <summary>
        /// Rebuilds the read models from Pages and checks the rebuilt state.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>An integrity report describing the resulting persisted state.</returns>
        Task<ReadModelIntegrityReport> RebuildAsync(
            string dataSource,
            CancellationToken token);
    }
}
