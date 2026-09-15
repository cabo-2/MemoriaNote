namespace MemoriaNote.Cli.Terminal
{
    /// <summary>
    /// Interface for a terminal screen
    /// </summary>
    public interface ITerminalScreen
    {
        /// <summary>
        /// Property to get or set the screen controller
        /// </summary>
        ScreenController Controller { get; set; }
        
        /// <summary>
        /// Property to get or set the view model for the screen
        /// </summary>
        MemoriaNoteViewModel ViewModel { get; set; }
    }
}
