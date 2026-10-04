using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli
{
    internal interface ICommandInput
    {
        bool IsInteractive { get; }

        string ReadLine();

        Task<string> ReadToEndAsync(CancellationToken cancellationToken);
    }
}
