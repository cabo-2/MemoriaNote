using System;
using MemoriaNote.Cli.Editors;
using MemoriaNote.Cli.UserConfig;

namespace MemoriaNote.Cli
{
    internal interface IEditorOptionsProvider
    {
        EditorOptions Load();
    }

    internal sealed class UserConfigurationEditorOptionsProvider :
        IEditorOptionsProvider
    {
        readonly UserConfigurationWorkflow _workflow;

        internal UserConfigurationEditorOptionsProvider(
            UserConfigurationWorkflow workflow)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        public EditorOptions Load()
        {
            var resolution = _workflow.Inspect().Editor;
            if (!resolution.IsConfigured)
            {
                throw new ExternalEditorConfigurationException(
                    "External editor is not configured." + Environment.NewLine +
                    "Run 'mn config editor setup' to configure one.");
            }

            return new EditorOptions(resolution.EffectiveCommand);
        }
    }
}
