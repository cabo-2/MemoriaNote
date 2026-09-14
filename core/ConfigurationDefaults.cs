using System;

namespace MemoriaNote
{
    /// <summary>Creates default persisted configuration values.</summary>
    public static class ConfigurationDefaults
    {
        /// <summary>Creates default settings of the requested configuration type.</summary>
        /// <typeparam name="TConfiguration">The configuration type to create.</typeparam>
        /// <param name="paths">The application paths used by default values.</param>
        /// <returns>A new default configuration.</returns>
        public static TConfiguration Create<TConfiguration>(ApplicationPaths paths)
            where TConfiguration : Configuration, new()
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));

            var configuration = new TConfiguration();
            configuration.DataSources.Add(paths.DefaultNotebookDatabasePath);
            configuration.Workspace = WorkspaceSettings.CreateDefault(
                configuration.DefaultWorkspaceName,
                configuration.DataSources);
            return configuration;
        }
    }
}
