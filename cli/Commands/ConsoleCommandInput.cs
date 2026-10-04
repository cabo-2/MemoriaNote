using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli
{
    internal sealed class ConsoleCommandInput : ICommandInput
    {
        readonly TextReader _standardInput;

        internal ConsoleCommandInput(TextReader standardInput, bool isInteractive)
        {
            _standardInput = standardInput ??
                throw new ArgumentNullException(nameof(standardInput));
            IsInteractive = isInteractive;
        }

        public bool IsInteractive { get; }

        public string ReadLine()
        {
            return _standardInput.ReadLine();
        }

        public Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            return _standardInput.ReadToEndAsync(cancellationToken);
        }
    }
}
