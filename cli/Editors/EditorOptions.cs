using System;

namespace MemoriaNote.Cli.Editors
{
    /// <summary>Describes persisted options used to select an external editor.</summary>
    public sealed class EditorOptions
    {
        EditorOptions()
        {
        }

        /// <summary>Initializes external editor selection options.</summary>
        /// <param name="useEnvironmentVariable">
        /// Whether to try the configured environment variable before the executable path.
        /// </param>
        /// <param name="environmentVariableName">
        /// The environment variable used to select an editor.
        /// </param>
        /// <param name="executablePath">
        /// The configured executable path used when environment selection is disabled or empty.
        /// </param>
        public EditorOptions(
            bool useEnvironmentVariable,
            string environmentVariableName,
            string executablePath)
        {
            if (string.IsNullOrWhiteSpace(environmentVariableName))
            {
                throw new ArgumentException(
                    "An editor environment variable name is required.",
                    nameof(environmentVariableName));
            }

            UseEnvironmentVariable = useEnvironmentVariable;
            EnvironmentVariableName = environmentVariableName;
            ExecutablePath = executablePath;
            HasSettings = true;
        }

        internal static EditorOptions Missing { get; } = new EditorOptions();

        internal EditorOptions(ExternalEditorCommand resolvedCommand)
        {
            ResolvedCommand = resolvedCommand ??
                throw new ArgumentNullException(nameof(resolvedCommand));
            HasSettings = true;
        }

        internal bool HasSettings { get; }

        internal ExternalEditorCommand ResolvedCommand { get; }

        /// <summary>Gets whether the environment variable should be tried first.</summary>
        public bool UseEnvironmentVariable { get; }

        /// <summary>Gets the environment variable used to select an editor.</summary>
        public string EnvironmentVariableName { get; }

        /// <summary>Gets the configured fallback executable path.</summary>
        public string ExecutablePath { get; }
    }
}
