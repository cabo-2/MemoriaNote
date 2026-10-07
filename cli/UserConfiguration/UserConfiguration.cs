using System;

namespace MemoriaNote.Cli.UserConfig
{
    internal sealed class UserConfiguration
    {
        internal UserConfiguration(UserEditorConfiguration editor)
        {
            Editor = editor ?? throw new ArgumentNullException(nameof(editor));
        }

        internal long FormatVersion => UserConfigurationContract.CurrentFormatVersion;

        internal UserEditorConfiguration Editor { get; }
    }
}
