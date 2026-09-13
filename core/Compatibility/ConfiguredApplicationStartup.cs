using System;
using System.IO;

namespace MemoriaNote
{
    /// <summary>
    /// Checks file-backed notebook databases for the compatibility composition root.
    /// </summary>
    internal sealed class FileNotebookDatabaseProbe : INotebookDatabaseProbe
    {
        /// <inheritdoc/>
        public bool Exists(string databasePath)
        {
            return File.Exists(databasePath);
        }
    }

    /// <summary>
    /// Builds a workspace from the current compatibility configuration.
    /// </summary>
    internal sealed class ConfiguredWorkspaceLoader : IWorkspaceLoader
    {
        readonly WorkspaceSettings _settings;

        internal ConfiguredWorkspaceLoader(WorkspaceSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <inheritdoc/>
        public Workspace Load()
        {
            return _settings.CreateWorkspace();
        }
    }
}
