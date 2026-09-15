using System;
using System.IO;

namespace MemoriaNote
{
    /// <summary>
    /// Provides file-system locations used by the application.
    /// </summary>
    public sealed class ApplicationPaths
    {
        const string ApplicationDataDirectoryEnvironmentVariable =
            "MEMORIA_NOTE_APPLICATION_DATA_DIRECTORY";

        readonly string _configurationFileName;

        /// <summary>
        /// Initializes application paths beneath the specified data directory.
        /// </summary>
        /// <param name="applicationDataDirectory">The application data directory.</param>
        /// <param name="configurationFileName">The configuration file name.</param>
        public ApplicationPaths(
            string applicationDataDirectory,
            string configurationFileName = "configuration.json")
        {
            if (string.IsNullOrWhiteSpace(applicationDataDirectory))
                throw new ArgumentException(
                    "An application data directory is required.",
                    nameof(applicationDataDirectory));

            if (string.IsNullOrWhiteSpace(configurationFileName))
                throw new ArgumentException(
                    "A configuration file name is required.",
                    nameof(configurationFileName));
            if (Path.IsPathRooted(configurationFileName) ||
                Path.GetFileName(configurationFileName) != configurationFileName)
            {
                throw new ArgumentException(
                    "The configuration file name cannot contain a directory.",
                    nameof(configurationFileName));
            }

            ApplicationDataDirectory = applicationDataDirectory;
            _configurationFileName = configurationFileName;
        }

        /// <summary>Gets the application name.</summary>
        public const string ApplicationName = "MemoriaNote";

        /// <summary>Gets the directory used for application data.</summary>
        public string ApplicationDataDirectory { get; }

        /// <summary>Gets the current configuration file path.</summary>
        public string ConfigurationPath => Path.Combine(
            ApplicationDataDirectory,
            _configurationFileName);

        /// <summary>Gets the default notebook database path.</summary>
        public string DefaultNotebookDatabasePath => Path.Combine(
            ApplicationDataDirectory,
            "Notepad.db");

        /// <summary>
        /// Creates paths from the environment override or operating-system default.
        /// </summary>
        /// <param name="configurationFileName">The selected format's file name.</param>
        /// <returns>The paths for the current process.</returns>
        public static ApplicationPaths CreateDefault(
            string configurationFileName = "configuration.json")
        {
            var configuredDirectory = Environment.GetEnvironmentVariable(
                ApplicationDataDirectoryEnvironmentVariable);
            var applicationDataDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ApplicationName)
                : configuredDirectory;
            return new ApplicationPaths(
                applicationDataDirectory,
                configurationFileName);
        }
    }
}
