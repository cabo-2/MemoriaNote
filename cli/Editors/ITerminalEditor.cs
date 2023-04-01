namespace MemoriaNote.Cli.Editors
{
    public interface ITerminalEditor
    {
        bool Edit();

        string FileName { get; set; }
        string TextData { get; set; }
    }
}