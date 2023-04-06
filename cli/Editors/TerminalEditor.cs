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

            var filePath = Scratchpad.Singleton.GetFile(this.FileName, true);  
            try
            {                              
                if (this.TextData != null)
                    File.WriteAllText(filePath, this.TextData);

                var startInfo = new ProcessStartInfo() {
                    FileName = _execPath,
                    Arguments = $"\"{filePath}\""
                };

                var process = Process.Start(startInfo);
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(nameof(Edit));

                if (!File.Exists(filePath))
                    return false;

                // Save if empty text              
                var editData = File.ReadAllText(filePath, Encoding.UTF8);
                if (this.TextData == editData)
                    return false;
                else
                    return true;
            }
            catch (Exception e)
            {
                Log.Logger.Error(e.Message);
                Log.Logger.Error(e.StackTrace);
                return false;
            }
            finally
            {
                Scratchpad.Singleton.Clear(filePath);
            }
        }
   
        public string FileName { get; set; }
        public string TextData { get; set; }
    }
}