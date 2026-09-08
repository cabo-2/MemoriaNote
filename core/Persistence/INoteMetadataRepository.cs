using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Loads and updates materialized metadata snapshots for note databases.
    /// </summary>
    public interface INoteMetadataRepository
    {
        /// <summary>
        /// Loads all metadata values from a note database in one operation.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The snapshot and any classifiable value problems.</returns>
        Task<MetadataLoadResult> LoadAsync(
            string dataSource,
            CancellationToken token);

        /// <summary>
        /// Persists all requested metadata fields atomically and returns the saved snapshot.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="update">The fields to update together.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The saved snapshot and any classifiable value problems.</returns>
        Task<MetadataLoadResult> UpdateAsync(
            string dataSource,
            NoteMetadataUpdate update,
            CancellationToken token);
    }
}
