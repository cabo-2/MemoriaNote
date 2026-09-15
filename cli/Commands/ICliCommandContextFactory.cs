namespace MemoriaNote.Cli
{
    internal interface ICliCommandContextFactory
    {
        ConfigurationCli LoadConfiguration();

        MemoriaNoteViewModel CreateViewModel(ConfigurationCli configuration);

        void SaveConfiguration(ConfigurationCli configuration);
    }
}
