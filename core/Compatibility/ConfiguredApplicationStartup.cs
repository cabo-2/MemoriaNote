using System;
using System.IO;
using System.Linq;
using MemoriaNote;
using MemoriaNote.Application;
using MemoriaNote.Models;
using MemoriaNote.Persistence;

namespace MemoriaNote.Compatibility
{
    /// <summary>
    /// Checks file-backed notebook databases for the compatibility composition root.
    /// </summary>
    public sealed class FileNotebookDatabaseProbe : INotebookDatabaseProbe
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
    public sealed class ConfiguredWorkspaceLoader : IWorkspaceLoader
    {
        readonly WorkspaceSettings _settings;
        readonly IPageRepository _pageRepository;
        readonly IPageSearchRepository _pageSearchRepository;
        readonly INotebookMetadataRepository _metadataRepository;

        /// <summary>Initializes a loader using compatibility persistence defaults.</summary>
        /// <param name="settings">The persisted workspace settings.</param>
        public ConfiguredWorkspaceLoader(WorkspaceSettings settings)
            : this(settings, null, null, null)
        {
        }

        /// <summary>Initializes a loader with explicitly composed persistence services.</summary>
        /// <param name="settings">The persisted workspace settings.</param>
        /// <param name="pageRepository">The page persistence service.</param>
        /// <param name="pageSearchRepository">The page search persistence service.</param>
        /// <param name="metadataRepository">The metadata persistence service.</param>
        public ConfiguredWorkspaceLoader(
            WorkspaceSettings settings,
            IPageRepository pageRepository,
            IPageSearchRepository pageSearchRepository,
            INotebookMetadataRepository metadataRepository)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _pageRepository = pageRepository;
            _pageSearchRepository = pageSearchRepository;
            _metadataRepository = metadataRepository;
        }

        /// <inheritdoc/>
        public Workspace Load()
        {
            var notebooks = _settings.NotebookDatabasePaths
                .Select(CreateNotebook)
                .ToList();
            var selectedNotebook = _settings.SelectedNotebookName == null
                ? notebooks.FirstOrDefault()
                : notebooks.FirstOrDefault(
                    notebook =>
                        _settings.SelectedNotebookName == notebook.Metadata.Name) ??
                    notebooks.FirstOrDefault();
            return new Workspace(_settings.Name, notebooks, selectedNotebook);
        }

        Notebook CreateNotebook(string databasePath)
        {
            if (_pageRepository == null ||
                _pageSearchRepository == null ||
                _metadataRepository == null)
            {
                return new Notebook(databasePath);
            }

            return new Notebook(
                databasePath,
                _pageRepository,
                _pageSearchRepository,
                _metadataRepository);
        }
    }
}
