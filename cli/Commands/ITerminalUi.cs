namespace MemoriaNote.Cli
{
    internal interface ITerminalUi
    {
        void RunHome(MemoriaNoteViewModel viewModel);

        void RunManage(MemoriaNoteViewModel viewModel, bool openEditor);
    }
}
