using System;
using System.IO;

namespace MemoriaNote
{
    /// <summary>
    /// Checks file-backed note data sources for the compatibility composition root.
    /// </summary>
    internal sealed class FileNoteDataSourceProbe : INoteDataSourceProbe
    {
        /// <inheritdoc/>
        public bool Exists(string dataSource)
        {
            return File.Exists(dataSource);
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
