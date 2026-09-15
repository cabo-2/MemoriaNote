using System;
using System.IO;

namespace MemoriaNote.Cli
{
    internal sealed class ConsoleCommandInput : ICommandInput
    {
        readonly TextReader _standardInput;

        internal ConsoleCommandInput(TextReader standardInput)
        {
            _standardInput = standardInput ??
                throw new ArgumentNullException(nameof(standardInput));
        }

        public string ReadLine()
        {
            return _standardInput.ReadLine();
        }
    }
}
