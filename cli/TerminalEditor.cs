using System;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace MemoriaNote.Cli
{
    public class TerminalEditor : ITerminalEditor
    {
        public TerminalEditor() { }
        public TerminalEditor(string name) : this()
        {
            this.Name = name;
        }

        public bool Edit()
        {
            if (CreateCommand == null)
                throw new ArgumentNullException(nameof(CreateCommand));

            try
            {
                var filePath = Scratchpad.Singleton.GetFile(this.Name, true);                
                if (this.Text != null)
                    File.WriteAllText(filePath, this.Text);

                var startInfo = CreateCommand(filePath);

                Log.Logger.Debug("Start terminal editor");
                var process = Process.Start(startInfo);
                process.WaitForExit();
                Log.Logger.Debug("End terminal editor");

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(nameof(Edit));

                if (!File.Exists(filePath))
                    return false;

                // Save if empty text              
                this.Text = File.ReadAllText(filePath, Encoding.UTF8);
                Scratchpad.Singleton.Clear(filePath);

                return true;
            }
            catch (Exception e)
            {
                Log.Logger.Error(e.Message);
                Log.Logger.Error(e.StackTrace);

                return false;
            }
        }
        
        public CreateProcessCommand CreateCommand { get; set; }        
        public string Name { get; set; }
        public string Text { get; set; }
    }
}