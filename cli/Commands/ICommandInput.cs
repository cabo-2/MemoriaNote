namespace MemoriaNote.Cli
{
    internal interface ICommandInput
    {
        bool IsInteractive { get; }

        string ReadLine();
    }
}
