using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// Describes the database paths and default notebook required to start the application.
    /// </summary>
    public sealed class ApplicationStartupRequest
    {
        readonly IReadOnlyList<string> _notebookDatabasePaths;

        /// <summary>
        /// Initializes an application startup request.
        /// </summary>
        /// <param name="defaultNotebookName">The default notebook name.</param>
        /// <param name="defaultNotebookTitle">The default notebook title.</param>
        /// <param name="defaultNotebookDatabasePath">The default notebook database path.</param>
        /// <param name="notebookDatabasePaths">The notebook database paths to migrate in order.</param>
        public ApplicationStartupRequest(
            string defaultNotebookName,
            string defaultNotebookTitle,
            string defaultNotebookDatabasePath,
            IEnumerable<string> notebookDatabasePaths)
        {
            if (string.IsNullOrWhiteSpace(defaultNotebookName))
            {
                throw new ArgumentException(
                    "The default note name is required.",
                    nameof(defaultNotebookName));
            }
            if (string.IsNullOrWhiteSpace(defaultNotebookTitle))
            {
                throw new ArgumentException(
                    "The default note title is required.",
                    nameof(defaultNotebookTitle));
            }
            if (string.IsNullOrWhiteSpace(defaultNotebookDatabasePath))
            {
                throw new ArgumentException(
                    "The default data source is required.",
                    nameof(defaultNotebookDatabasePath));
            }
            if (notebookDatabasePaths == null)
                throw new ArgumentNullException(nameof(notebookDatabasePaths));

            var copiedDatabasePaths = notebookDatabasePaths.ToArray();
            if (copiedDatabasePaths.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "Migration data sources cannot contain empty values.",
                    nameof(notebookDatabasePaths));
            }

            DefaultNotebookName = defaultNotebookName;
            DefaultNotebookTitle = defaultNotebookTitle;
            DefaultNotebookDatabasePath = defaultNotebookDatabasePath;
            _notebookDatabasePaths = new ReadOnlyCollection<string>(copiedDatabasePaths);
        }

        /// <summary>Gets the default notebook name.</summary>
        public string DefaultNotebookName { get; }

        /// <summary>Gets the default notebook title.</summary>
        public string DefaultNotebookTitle { get; }

        /// <summary>Gets the default notebook database path.</summary>
        public string DefaultNotebookDatabasePath { get; }

        /// <summary>Gets the notebook database paths to migrate in order.</summary>
        public IReadOnlyList<string> NotebookDatabasePaths => _notebookDatabasePaths;
    }
}
