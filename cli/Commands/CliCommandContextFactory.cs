using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli
{
    internal sealed class CliCommandContextFactory : ICliCommandContextFactory
    {
        readonly INotebookMigrator _notebookMigrator;
        readonly IPageRepository _pageRepository;
        readonly IPageSearchRepository _pageSearchRepository;
        readonly INotebookMetadataRepository _metadataRepository;
        readonly ApplicationPaths _applicationPaths;
        readonly IConfigurationStore<ConfigurationCli> _configurationStore;
        readonly ICommandOutput _output;
        readonly ILoggerFactory _loggerFactory;
        readonly ILogger<CliCommandContextFactory> _logger;

        internal CliCommandContextFactory(
            INotebookMigrator notebookMigrator,
            IPageRepository pageRepository,
            IPageSearchRepository pageSearchRepository,
            INotebookMetadataRepository metadataRepository,
            ApplicationPaths applicationPaths,
            IConfigurationStore<ConfigurationCli> configurationStore,
            ICommandOutput output,
            ILoggerFactory loggerFactory)
        {
            _notebookMigrator = notebookMigrator ??
                throw new ArgumentNullException(nameof(notebookMigrator));
            _pageRepository = pageRepository ??
                throw new ArgumentNullException(nameof(pageRepository));
            _pageSearchRepository = pageSearchRepository ??
                throw new ArgumentNullException(nameof(pageSearchRepository));
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
            _applicationPaths = applicationPaths ??
                throw new ArgumentNullException(nameof(applicationPaths));
            _configurationStore = configurationStore ??
                throw new ArgumentNullException(nameof(configurationStore));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _loggerFactory = loggerFactory ??
                throw new ArgumentNullException(nameof(loggerFactory));
            _logger = _loggerFactory.CreateLogger<CliCommandContextFactory>();
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

        public MemoriaNoteViewModel CreateViewModel(ConfigurationCli configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var request = new ApplicationStartupRequest(
                configuration.DefaultNotebookName,
                configuration.DefaultNotebookTitle,
                _applicationPaths.DefaultNotebookDatabasePath,
                configuration.DataSources);
            var startupService = new ApplicationStartupService(
                _notebookMigrator,
                new FileNotebookDatabaseProbe(),
                new ConfiguredWorkspaceLoader(
                    configuration.Workspace,
                    _pageRepository,
                    _pageSearchRepository,
                    _metadataRepository));
            var session = startupService.StartAsync(request, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (session.DefaultNotebookCreated)
                _logger.LogInformation("Default note created");

            return new MemoriaNoteViewModel(
                configuration,
                session,
                _loggerFactory.CreateLogger<MemoriaNoteViewModel>());
        }

        public void SaveConfiguration(ConfigurationCli configuration)
        {
            _configurationStore.Save(configuration);
        }
    }
}
