using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace MemoriaNote.Cli.Editors
{
    /// <summary>
    /// Factory class for creating instances of terminal editors based on configuration settings.
    /// </summary>
    public static class TerminalEditorFactory
    {
        static ProcessStartInfo LaunchShellCommand(string filePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new ProcessStartInfo()
                {
                    FileName = "start",
                    Arguments = $"\"{filePath}\""
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                       RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            {
                return new ProcessStartInfo()
                {
                    FileName = "open",
                    Arguments = $"\"{filePath}\""
                };
            }
            else
            {
                return new ProcessStartInfo()
                {
                    FileName = "xdg-open", // or gnome-open   
                    Arguments = $"\"file://{filePath}\""
                };
            }
        }

        static ProcessStartInfo ExecuteCommand(string exec, string filePath)
                         => new ProcessStartInfo() { FileName = $"{exec}", Arguments = $"\"{filePath}\"" };

        /// <summary>
        /// Creates an instance of ITerminalEditor based on configuration settings.
        /// </summary>
        /// <param name="configuration">The current CLI configuration.</param>
        /// <returns>An instance of ITerminalEditor.</returns>
        public static ITerminalEditor Create(ConfigurationCli configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            // Check if the environment variable for the terminal editor exists and use it if specified
            if (configuration.Terminal.EditorEnv)
            {
                var envs = Environment.GetEnvironmentVariables();
                if (envs.Contains(ConfigurationCli.TerminalSetting.EditorEnvName))
                    return new TerminalEditor((string)envs[ConfigurationCli.TerminalSetting.EditorEnvName]);
            }

            // Throw an exception if the editor path is not specified in configuration
            if (string.IsNullOrWhiteSpace(configuration.Terminal.EditorPath))
                throw new ApplicationException("editor path");

            // Create a new TerminalEditor instance with the specified editor path
            return new TerminalEditor(configuration.Terminal.EditorPath);
        }
    }
}
