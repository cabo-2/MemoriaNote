using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli
{
    internal interface ITerminalUi
    {
        Task RunHomeAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken);

        Task RunManageAsync(
            MemoriaNoteViewModel viewModel,
            bool openEditor,
            CancellationToken cancellationToken);
    }
}
