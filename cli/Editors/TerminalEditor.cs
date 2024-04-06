using System;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace MemoriaNote.Cli.Editors
{
    /// <summary>
    /// Class definition for a TerminalEditor implementing ITerminalEditor interface
    /// </summary>
    public class TerminalEditor : ITerminalEditor
    {
        string _execPath;
        public TerminalEditor(string execPath)
        {
            _execPath = execPath;
        }

        /// <summary>
        /// Method to edit a file using an external editor
        /// </summary>
        /// <returns>Returns true if the file was successfully edited, otherwise false</returns>
        public bool Edit()
        {
            if (_execPath == null)
                throw new ArgumentNullException(nameof(Edit));

            // Get the file path from the scratchpad
            var filePath = Scratchpad.Singleton.GetFile(this.FileName, true);  
            try
            {                              
                // Write text data to file if it is not null
                if (this.TextData != null)
                    File.WriteAllText(filePath, this.TextData);

                // Start a new process with the specified editor and file path
                var startInfo = new ProcessStartInfo() {
                    FileName = _execPath,
                    Arguments = $"\"{filePath}\""
                };

                var process = Process.Start(startInfo);
                // Wait for the process to exit
                process.WaitForExit();
                // If the process exits with a non-zero code, throw an exception
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(nameof(Edit));

                if (!File.Exists(filePath))
                    return false;

                // Save the edited data if the text data is different from the original
                var editData = File.ReadAllText(filePath, Encoding.UTF8);
                if (this.TextData == editData)
                    return false;
                else
                {
                    this.TextData = editData;
                    return true;
                }
            }
            catch (Exception e)
            {
                // Log any exceptions that occur
                Log.Logger.Error(e.Message);
                Log.Logger.Error(e.StackTrace);
                return false;
            }
            finally
            {
                // Clear the file from the scratchpad
                Scratchpad.Singleton.Clear(filePath);
            }
        }
   
        /// <summary>
        /// Gets or sets the file name being edited
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the text data of the file being edited
        /// </summary>
        public string TextData { get; set; }
    }
}