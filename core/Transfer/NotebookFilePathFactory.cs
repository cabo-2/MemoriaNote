using System;
using System.Globalization;
using System.IO;

namespace MemoriaNote
{
    /// <summary>
    /// Creates file paths for notebook databases and backup archives.
    /// </summary>
    public sealed class NotebookFilePathFactory
    {
        readonly IClock _clock;

        /// <summary>Initializes a path factory using the system clock.</summary>
        public NotebookFilePathFactory() : this(SystemClock.Instance)
        {
        }

        /// <summary>Initializes a path factory using the specified clock.</summary>
        /// <param name="clock">The clock used in generated file names.</param>
        public NotebookFilePathFactory(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

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

            var timestamp = FormatTimestamp();
            return FindAvailablePath(
                directoryPath,
                notebookName + "_" + timestamp,
                ".db");
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

            return FindAvailablePath(
                directoryPath,
                notebookName + "_" + FormatTimestamp(),
                ".json.zip");
        }

        string FormatTimestamp()
        {
            return _clock.UtcNow.ToString(
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture);
        }

        static string FindAvailablePath(
            string directoryPath,
            string fileNameWithoutExtension,
            string extension)
        {
            var path = Path.Combine(
                directoryPath,
                fileNameWithoutExtension + extension);
            var sequence = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(
                    directoryPath,
                    fileNameWithoutExtension + "_" + sequence + extension);
                sequence++;
            }

            return path;
        }
    }
}
