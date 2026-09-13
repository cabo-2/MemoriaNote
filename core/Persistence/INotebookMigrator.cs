using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Creates notebook databases and migrates existing notebook databases.
    /// </summary>
    public interface INotebookMigrator
    {
        /// <summary>
        /// Creates a new notebook database and initializes its required metadata.
        /// </summary>
        /// <param name="name">The name of the notebook.</param>
        /// <param name="title">The title of the notebook.</param>
        /// <param name="databasePath">The path of the new notebook database.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>The newly created notebook.</returns>
        Task<Notebook> CreateAsync(
            string name,
            string title,
            string databasePath,
            CancellationToken token);

        /// <summary>
        /// Migrates an existing notebook database and updates its stored version.
        /// </summary>
        /// <param name="databasePath">The path of the existing notebook database.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>A task representing the migration operation.</returns>
        Task MigrateAsync(string databasePath, CancellationToken token);
    }
}
