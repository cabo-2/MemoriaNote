using System;

namespace MemoriaNote.Cli.Editors
{
    internal interface IEnvironmentVariableSource
    {
        string Get(string name);
    }

    internal sealed class ProcessEnvironmentVariableSource : IEnvironmentVariableSource
    {
        public string Get(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }
    }

    internal sealed class EditorExecutableResolver
    {
        readonly IEnvironmentVariableSource _environmentVariables;

        internal EditorExecutableResolver(IEnvironmentVariableSource environmentVariables)
        {
            _environmentVariables = environmentVariables ??
                throw new ArgumentNullException(nameof(environmentVariables));
        }

        internal ExternalEditorCommand Resolve(
            EditorOptions options,
            ExternalEditorCommand commandOverride)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (commandOverride != null)
                return commandOverride;

            if (!options.HasSettings)
            {
                throw new ExternalEditorConfigurationException(
                    "External editor settings are missing.");
            }

            if (options.UseEnvironmentVariable)
            {
                var environmentEditor = _environmentVariables.Get(
                    options.EnvironmentVariableName);
                if (!string.IsNullOrWhiteSpace(environmentEditor))
                    return new ExternalEditorCommand(environmentEditor);
            }

            if (string.IsNullOrWhiteSpace(options.ExecutablePath))
            {
                throw new ExternalEditorConfigurationException(
                    "External editor path is not configured.");
            }

            return new ExternalEditorCommand(options.ExecutablePath);
        }
    }
}
