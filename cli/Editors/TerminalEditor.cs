using System;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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
        /// <param name="cancellationToken">Stops the editor process when requested.</param>
        /// <returns>Returns true if the file was successfully edited, otherwise false</returns>
        public async Task<bool> EditAsync(CancellationToken cancellationToken)
        {
            if (_execPath == null)
                throw new ArgumentNullException(nameof(EditAsync));

            using var temporaryFile = _temporaryFileStore.CreateFile(FileName);
            var filePath = temporaryFile.Path;
            Process process = null;
            try
            {                              
                // Write text data to file if it is not null
                if (this.TextData != null)
                    await File.WriteAllTextAsync(
                        filePath,
                        this.TextData,
                        cancellationToken);

                // Start a new process with the specified editor and file path
                var startInfo = new ProcessStartInfo() {
                    FileName = _execPath,
                    Arguments = $"\"{filePath}\""
                };

                process = Process.Start(startInfo) ??
                    throw new InvalidOperationException(nameof(EditAsync));
                // Wait for the process to exit
                await process.WaitForExitAsync(cancellationToken);
                // If the process exits with a non-zero code, throw an exception
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(nameof(EditAsync));

                if (!File.Exists(filePath))
                    return false;

                // Save the edited data if the text data is different from the original
                var editData = await File.ReadAllTextAsync(
                    filePath,
                    Encoding.UTF8,
                    cancellationToken);
                if (this.TextData == editData)
                    return false;
                else
                {
                    this.TextData = editData;
                    return true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (process != null && !process.HasExited)
                    process.Kill(entireProcessTree: true);
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "The external editor failed.");
                return false;
            }
            finally
            {
                process?.Dispose();
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
