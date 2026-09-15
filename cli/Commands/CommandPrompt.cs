using System;

namespace MemoriaNote.Cli
{
    internal sealed class CommandPrompt
    {
        readonly ICommandInput _input;
        readonly ICommandOutput _output;

        internal CommandPrompt(ICommandInput input, ICommandOutput output)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal bool ReadTryAgain()
        {
            _output.Write("Try again?(y/n)_");
            return _input.ReadLine().ToLower() == "y";
        }

        internal string ReadNotebookName()
        {
            _output.Write("What is the name?_");
            return _input.ReadLine();
        }

        internal string ReadNotebookTitle()
        {
            _output.Write("What is the title?_");
            return _input.ReadLine();
        }
    }
}
