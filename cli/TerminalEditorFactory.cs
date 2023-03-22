using System.Runtime.InteropServices;
using System.Diagnostics;

namespace MemoriaNote.Cli
{
    public static class TerminalEditorFactory
    {
        //
        // Windows:
        //   1. config selected editor
        //   2. Git for Windows selected editor
        //   3. start "<filepath>.txt"
        //   4. Terminal.Gui implements editor
        //
        // Linux:        
        //   1. config selected editor
        //   2. env $EDITOR ( nano or vim )
        //   3. xdg-open "file://<filepath>.txt"
        //   4. Terminal.Gui implements editor
        //

        static ProcessStartInfo LaunchShellCommand(string filePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new ProcessStartInfo()
                {
                    FileName = "start",
                    Arguments = $"\"{filePath}\""
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                       RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            {
                return new ProcessStartInfo()
                {
                    FileName = "open",
                    Arguments = $"\"{filePath}\""
                };
            }
            else
            {
                return new ProcessStartInfo()
                {
                    FileName = "xdg-open", // or gnome-open   
                    Arguments = $"\"file://{filePath}\""
                };
            }
        }

        static ProcessStartInfo ExecuteCommand(string exec, string filePath)
                         => new ProcessStartInfo() { FileName = $"{exec}", Arguments = $"\"{filePath}\"" };

        public static ITerminalEditor Create(string name = null)
        {
            return new TerminalEditor(name) {
                CreateCommand = (filePath) => ExecuteCommand("nano", filePath)
            };
        }
    }
}