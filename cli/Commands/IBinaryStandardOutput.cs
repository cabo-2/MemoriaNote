using System.IO;

namespace MemoriaNote.Cli
{
    internal interface IBinaryStandardOutput
    {
        bool IsTerminal { get; }

        Stream Stream { get; }
    }

    internal sealed class ConsoleBinaryStandardOutput : IBinaryStandardOutput
    {
        internal ConsoleBinaryStandardOutput(Stream stream, bool isTerminal)
        {
            Stream = stream ?? throw new System.ArgumentNullException(nameof(stream));
            IsTerminal = isTerminal;
        }

        public bool IsTerminal { get; }

        public Stream Stream { get; }
    }
}
