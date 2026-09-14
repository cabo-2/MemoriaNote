using System;
using System.Text;
using System.IO;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli.Editors
{
    /// <summary>
    /// Class definition for a TerminalEditor implementing ITerminalEditor interface
    /// </summary>
    public class TerminalEditor : ITerminalEditor
    {
        readonly string _execPath;
        readonly ITemporaryFileStore _temporaryFileStore;
        readonly ILogger<TerminalEditor> _logger;

        /// <summary>Initializes an external terminal editor.</summary>
        /// <param name="execPath">The editor executable path.</param>
        /// <param name="temporaryFileStore">The store used for editor exchange files.</param>
        /// <param name="logger">The editor logger.</param>
        public TerminalEditor(
            string execPath,
            ITemporaryFileStore temporaryFileStore,
            ILogger<TerminalEditor> logger)
        {
            _execPath = execPath;
            _temporaryFileStore = temporaryFileStore ??
                throw new ArgumentNullException(nameof(temporaryFileStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Method to edit a file using an external editor
        /// </summary>
        /// <returns>Returns true if the file was successfully edited, otherwise false</returns>
        public bool Edit()
        {
            if (_execPath == null)
                throw new ArgumentNullException(nameof(Edit));

            using var temporaryFile = _temporaryFileStore.CreateFile(FileName);
            var filePath = temporaryFile.Path;
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
                _logger.LogError(e, "The external editor failed.");
                return false;
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
