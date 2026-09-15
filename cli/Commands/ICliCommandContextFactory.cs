using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli
{
    internal interface ICliCommandContextFactory
    {
        ConfigurationCli LoadConfiguration();

        Task<MemoriaNoteViewModel> CreateViewModelAsync(
            ConfigurationCli configuration,
            CancellationToken cancellationToken);

        void SaveConfiguration(ConfigurationCli configuration);
    }
}
