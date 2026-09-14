using System;
using System.IO;

namespace MemoriaNote
{
    /// <summary>
    /// Creates file paths for notebook databases and backup archives.
    /// </summary>
    public sealed class NotebookFilePathFactory
    {
        /// <summary>
        /// Creates an available database path, adding a timestamp when the default path exists.
        /// </summary>
        /// <param name="directoryPath">The destination directory.</param>
        /// <param name="notebookName">The notebook name.</param>
        /// <returns>An available database path.</returns>
        public string CreateDatabasePath(string directoryPath, string notebookName)
        {
            if (directoryPath == null)
                throw new ArgumentNullException(nameof(directoryPath));
            if (notebookName == null)
                throw new ArgumentNullException(nameof(notebookName));

            var path = Path.Combine(directoryPath, notebookName + ".db");
            if (!File.Exists(path))
                return path;

            path = Path.Combine(
                directoryPath,
                notebookName + "_" + DateTime.Now.ToString("yyyyMMddhhmmss") + ".db");
            if (File.Exists(path))
                throw new ArgumentException(path, nameof(notebookName));

            return path;
        }

        /// <summary>
        /// Creates a timestamped path for a JSON backup archive.
        /// </summary>
        /// <param name="directoryPath">The destination directory.</param>
        /// <param name="notebookName">The notebook name.</param>
        /// <returns>The timestamped archive path.</returns>
        public string CreateBackupPath(string directoryPath, string notebookName)
        {
            if (directoryPath == null)
                throw new ArgumentNullException(nameof(directoryPath));
            if (notebookName == null)
                throw new ArgumentNullException(nameof(notebookName));

            return Path.Combine(
                directoryPath,
                $"{notebookName}_{DateTime.Now:yyyyMMddhhmmss}.json.zip");
        }
    }
}
