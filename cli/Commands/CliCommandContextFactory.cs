using System;

namespace MemoriaNote.Cli
{
    internal sealed class CliCommandContextFactory : ICliCommandContextFactory
    {
        readonly IConfigurationStore<ConfigurationCli> _configurationStore;
        readonly ICommandOutput _output;

        internal CliCommandContextFactory(
            IConfigurationStore<ConfigurationCli> configurationStore,
            ICommandOutput output)
        {
            _configurationStore = configurationStore ??
                throw new ArgumentNullException(nameof(configurationStore));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public ConfigurationCli LoadConfiguration()
        {
            var result = _configurationStore.Load();
            if (result.Status == ConfigurationLoadStatus.RecoveredInvalid)
            {
                _output.WriteErrorLine(
                    "Warning: The configuration file was invalid and was moved to " +
                    $"\"{result.RecoveryArtifactPath}\". " +
                    "Default configuration has been created.");
            }

            return result.Configuration;
        }

        public void SaveConfiguration(ConfigurationCli configuration)
        {
            _configurationStore.Save(configuration);
        }
    }
}
