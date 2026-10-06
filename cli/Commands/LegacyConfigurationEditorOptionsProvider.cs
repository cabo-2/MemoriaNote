using System;
using MemoriaNote.Cli.Editors;

namespace MemoriaNote.Cli
{
    internal interface IEditorOptionsProvider
    {
        EditorOptions Load();
    }

    internal sealed class LegacyConfigurationEditorOptionsProvider : IEditorOptionsProvider
    {
        readonly ICliCommandContextFactory _contextFactory;

        internal LegacyConfigurationEditorOptionsProvider(
            ICliCommandContextFactory contextFactory)
        {
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
        }

        public EditorOptions Load()
        {
            return FromConfiguration(_contextFactory.LoadConfiguration());
        }

        internal static EditorOptions FromConfiguration(ConfigurationCli configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var settings = configuration.Terminal;
            if (settings == null)
                return EditorOptions.Missing;

            return new EditorOptions(
                settings.EditorEnv,
                ConfigurationCli.TerminalSetting.EditorEnvName,
                settings.EditorPath);
        }
    }
}
