using System.Diagnostics;

namespace MemoriaNote.Cli
{
    public interface ITerminalEditor
    {
        bool Edit();

        public string Name { get; set; }
        public string Text { get; set; }
    }

    public delegate ProcessStartInfo CreateProcessCommand(string filePath);
}