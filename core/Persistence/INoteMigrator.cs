using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Creates note databases and migrates existing note databases.
    /// </summary>
    public interface INoteMigrator
    {
        /// <summary>
        /// Creates a new note database and initializes its required metadata.
        /// </summary>
        /// <param name="name">The name of the note.</param>
        /// <param name="title">The title of the note.</param>
        /// <param name="dataSource">The path of the new note database.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>The newly created note.</returns>
        Task<Note> CreateAsync(
            string name,
            string title,
            string dataSource,
            CancellationToken token);

        /// <summary>
        /// Migrates an existing note database and updates its stored version.
        /// </summary>
        /// <param name="dataSource">The path of the existing note database.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>A task representing the migration operation.</returns>
        Task MigrateAsync(string dataSource, CancellationToken token);
    }
}
