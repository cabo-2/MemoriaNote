using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli.Editors
{
    /// <summary>
    /// Interface for a terminal editor
    /// </summary>
    public interface ITerminalEditor
    {
        /// <summary>
        /// Method to perform editing operation
        /// </summary>
        /// <param name="cancellationToken">Stops the editor process when requested.</param>
        /// <returns>True when edited text was read successfully; otherwise, false.</returns>
        Task<bool> EditAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Property to get or set the file name
        /// </summary>
        string FileName { get; set; }

        /// <summary>
        /// Property to get or set the text data
        /// </summary>
        string TextData { get; set; }
    }
}
