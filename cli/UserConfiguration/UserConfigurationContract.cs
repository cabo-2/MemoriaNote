using System;
using System.IO;

namespace MemoriaNote.Cli.UserConfig
{
    internal static class UserConfigurationContract
    {
        internal const string FileName = "config.toml";
        internal const long CurrentFormatVersion = 1;

        internal const string EnvironmentSelection = "environment";
        internal const string ProgramSelection = "program";
        internal const string UnsetSelection = "unset";

        internal static string GetPath(ApplicationPaths applicationPaths)
        {
            if (applicationPaths == null)
                throw new ArgumentNullException(nameof(applicationPaths));

            return Path.Combine(
                applicationPaths.ApplicationDataDirectory,
                FileName);
        }

        internal static string GetSelectionValue(UserEditorSelection selection)
        {
            return selection switch
            {
                UserEditorSelection.Environment => EnvironmentSelection,
                UserEditorSelection.Program => ProgramSelection,
                UserEditorSelection.Unset => UnsetSelection,
                _ => throw new ArgumentOutOfRangeException(nameof(selection))
            };
        }
    }
}
