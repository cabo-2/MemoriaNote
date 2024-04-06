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
        /// <returns></returns>
        bool Edit();

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