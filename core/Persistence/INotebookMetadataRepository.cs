using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Loads and updates materialized metadata snapshots for notebook databases.
    /// </summary>
    public interface INotebookMetadataRepository
    {
        /// <summary>
        /// Loads all metadata values from a notebook database in one operation.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The snapshot and any classifiable value problems.</returns>
        Task<NotebookMetadataResult> LoadAsync(
            string databasePath,
            CancellationToken token);

        /// <summary>
        /// Persists all requested metadata fields atomically and returns the saved snapshot.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="patch">The fields to patch together.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The saved snapshot and any classifiable value problems.</returns>
        Task<NotebookMetadataResult> UpdateAsync(
            string databasePath,
            NotebookMetadataPatch patch,
            CancellationToken token);
    }
}
