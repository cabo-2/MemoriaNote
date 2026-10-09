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

        /// <summary>
        /// Initializes application paths beneath the specified data directory.
        /// </summary>
        /// <param name="applicationDataDirectory">The application data directory.</param>
        public ApplicationPaths(string applicationDataDirectory)
        {
            if (string.IsNullOrWhiteSpace(applicationDataDirectory))
                throw new ArgumentException(
                    "An application data directory is required.",
                    nameof(applicationDataDirectory));

            ApplicationDataDirectory = applicationDataDirectory;
        }

        /// <summary>Gets the application name.</summary>
        public const string ApplicationName = "MemoriaNote";

        /// <summary>Gets the directory used for application data.</summary>
        public string ApplicationDataDirectory { get; }

        /// <summary>
        /// Creates paths from the environment override or operating-system default.
        /// </summary>
        /// <returns>The paths for the current process.</returns>
        public static ApplicationPaths CreateDefault()
        {
            var configuredDirectory = Environment.GetEnvironmentVariable(
                ApplicationDataDirectoryEnvironmentVariable);
            var applicationDataDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ApplicationName)
                : configuredDirectory;
            return new ApplicationPaths(applicationDataDirectory);
        }
    }
}
