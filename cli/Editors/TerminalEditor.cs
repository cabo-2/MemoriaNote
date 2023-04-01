using System;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace MemoriaNote.Cli.Editors
{
    public class TerminalEditor : ITerminalEditor
    {
        string _execPath;
        public TerminalEditor(string execPath)
        {
            _execPath = execPath;
        }

        public bool Edit()
        {
            if (_execPath == null)
                throw new ArgumentNullException(nameof(Edit));

            try
            {
                var filePath = Scratchpad.Singleton.GetFile(this.FileName, true);                
                if (this.TextData != null)
                    File.WriteAllText(filePath, this.TextData);

                var startInfo = new ProcessStartInfo() {
                    FileName = _execPath,
                    Arguments = $"\"{filePath}\""
                };

                Log.Logger.Debug("Start terminal editor");
                var process = Process.Start(startInfo);
                process.WaitForExit();
                Log.Logger.Debug("End terminal editor");

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(nameof(Edit));

                if (!File.Exists(filePath))
                    return false;

                // Save if empty text              
                this.TextData = File.ReadAllText(filePath, Encoding.UTF8);
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
   
        public string FileName { get; set; }
        public string TextData { get; set; }
    }
}