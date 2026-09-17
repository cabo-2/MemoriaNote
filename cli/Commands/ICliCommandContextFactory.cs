using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;

namespace MemoriaNote.Cli
{
    internal interface ICliCommandContextFactory
    {
        ConfigurationCli LoadConfiguration();

        Task<ApplicationSession> CreateSessionAsync(
            ConfigurationCli configuration,
            CancellationToken cancellationToken);

        Task<MemoriaNoteViewModel> CreateViewModelAsync(
            ConfigurationCli configuration,
            CancellationToken cancellationToken);

        void SaveConfiguration(ConfigurationCli configuration);
    }
}
