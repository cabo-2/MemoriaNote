namespace MemoriaNote.Cli
{
    internal interface ICliCommandContextFactory
    {
        ConfigurationCli LoadConfiguration();

        void SaveConfiguration(ConfigurationCli configuration);
    }
}
