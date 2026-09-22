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
            ConfigurationCli configuration,
            ExternalEditorCommand commandOverride)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            if (commandOverride != null)
                return commandOverride;

            var settings = configuration.Terminal ??
                throw new ExternalEditorConfigurationException(
                    "External editor settings are missing.");
            if (settings.EditorEnv)
            {
                var environmentEditor = _environmentVariables.Get(
                    ConfigurationCli.TerminalSetting.EditorEnvName);
                if (!string.IsNullOrWhiteSpace(environmentEditor))
                    return new ExternalEditorCommand(environmentEditor);
            }

            if (string.IsNullOrWhiteSpace(settings.EditorPath))
            {
                throw new ExternalEditorConfigurationException(
                    "External editor path is not configured.");
            }

            return new ExternalEditorCommand(settings.EditorPath);
        }
    }
}
