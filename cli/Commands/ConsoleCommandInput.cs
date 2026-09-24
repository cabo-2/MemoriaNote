using System;
using System.IO;

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
    }
}
