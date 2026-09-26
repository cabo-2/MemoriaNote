using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Models;

namespace MemoriaNote.Persistence
{
    /// <summary>
    /// Represents a notebook created with a format version newer than this application.
    /// </summary>
    public sealed class UnsupportedNotebookFormatVersionException : Exception
    {
        /// <summary>Initializes an unsupported notebook format exception.</summary>
        /// <param name="formatVersion">The unsupported notebook format version.</param>
        public UnsupportedNotebookFormatVersionException(string formatVersion)
            : base($"The notebook format version '{formatVersion}' is not supported.")
        {
            FormatVersion = formatVersion ??
                throw new ArgumentNullException(nameof(formatVersion));
        }

        /// <summary>Gets the unsupported notebook format version.</summary>
        public string FormatVersion { get; }
    }

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
