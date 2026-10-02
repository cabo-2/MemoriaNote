using System.IO;

namespace MemoriaNote.Cli
{
    internal interface IBinaryStandardInput
    {
        bool IsTerminal { get; }

        Stream Stream { get; }
    }

    internal sealed class ConsoleBinaryStandardInput : IBinaryStandardInput
    {
        internal ConsoleBinaryStandardInput(Stream stream, bool isTerminal)
        {
            Stream = stream ?? throw new System.ArgumentNullException(nameof(stream));
            IsTerminal = isTerminal;
        }

        public bool IsTerminal { get; }

        public Stream Stream { get; }
    }
}
