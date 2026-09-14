using System;
using System.IO;
using System.Linq;

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
            var notebooks = _settings.NotebookDatabasePaths
                .Select(databasePath => new Notebook(databasePath))
                .ToList();
            var selectedNotebook = _settings.SelectedNotebookName == null
                ? notebooks.FirstOrDefault()
                : notebooks.FirstOrDefault(
                    notebook =>
                        _settings.SelectedNotebookName == notebook.Metadata.Name) ??
                    notebooks.FirstOrDefault();
            return new Workspace(_settings.Name, notebooks, selectedNotebook);
        }
    }
}
