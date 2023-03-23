namespace MemoriaNote.Cli
{
    public interface ITerminalScreen
    {
        ScreenController Controller { get; set; }
        MemoriaNoteViewModel ViewModel { get; set; }
    }
}