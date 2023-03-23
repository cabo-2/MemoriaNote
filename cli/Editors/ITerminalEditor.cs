namespace MemoriaNote.Cli.Editors
{
    public interface ITerminalEditor
    {
        bool Edit();

        string Name { get; set; }
        string Text { get; set; }
    }
}