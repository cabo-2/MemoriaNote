using System;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Represents the configuration specific to the CLI application.
    /// </summary>
    [DataContract]
    public class ConfigurationCli : Configuration
    {
        /// <summary>
        /// Represents terminal settings for the CLI.
        /// </summary>
        [DataMember] public TerminalSetting Terminal { get; set; } = new TerminalSetting();

        /// <summary>Represents persisted terminal options.</summary>
        [DataContract]
        public class TerminalSetting
        {
            /// <summary>
            /// Gets the environment variable name for the editor.
            /// </summary>
            public static string EditorEnvName => "EDITOR";

            /// <summary>Gets or sets whether the editor environment variable is used.</summary>
            [DataMember] public bool EditorEnv { get; set; } = true;

            /// <summary>Gets or sets the configured editor path.</summary>
            [DataMember] public string EditorPath { get; set; }

            /// <summary>Gets or sets the shell completion mode.</summary>
            [DataMember] public CompletionType Completion { get; set; } = CompletionType.Word;
        }

        /// <summary>
        /// Represents state settings for the CLI.
        /// </summary>
        [DataMember] public StateSetting State { get; set; } = new StateSetting();

        /// <summary>
        /// Represents state setting configurations.
        /// </summary>
        [DataContract]
        public class StateSetting
        {
            /// <summary>Gets or sets the last selected search range.</summary>
            [DataMember] public SearchRangeType SearchRange { get; set; }

            /// <summary>Gets or sets the last selected search method.</summary>
            [DataMember] public SearchMethodType SearchMethod { get; set; }
        }

        /// <summary>
        /// Creates default CLI configuration values.
        /// </summary>
        /// <param name="paths">The application paths used by default values.</param>
        /// <returns>A new default CLI configuration.</returns>
        public static ConfigurationCli CreateDefault(ApplicationPaths paths)
        {
            var configuration = ConfigurationDefaults.Create<ConfigurationCli>(paths);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                configuration.Terminal.EditorPath = @"C:\Program Files\Git\usr\bin\nano.exe";
            else
                configuration.Terminal.EditorPath = "nano";

            return configuration;
        }
    }

    /// <summary>
    /// Represents completion types for the CLI.
    /// </summary>
    public enum CompletionType
    {
        None,
        Word
    }
}
