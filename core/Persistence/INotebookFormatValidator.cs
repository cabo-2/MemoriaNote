using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Models;

namespace MemoriaNote.Persistence
{
    /// <summary>Validates live notebook files without migrating or modifying them.</summary>
    public interface INotebookFormatValidator
    {
        /// <summary>Validates and loads a notebook in the current storage format.</summary>
        /// <param name="databasePath">The notebook database path.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>The validated notebook metadata.</returns>
        Task<NotebookMetadataResult> ValidateCurrentAsync(
            string databasePath,
            CancellationToken token);
    }
}
