using System;
using MemoriaNote.Cli.Editors;

namespace MemoriaNote.Cli.UserConfig
{
    internal sealed class UserEditorResolution
    {
        internal UserEditorResolution(
            UserEditorConfiguration configuration,
            string source,
            ExternalEditorCommand effectiveCommand)
        {
            Configuration = configuration;
            Source = source ?? throw new ArgumentNullException(nameof(source));
            EffectiveCommand = effectiveCommand;
        }

        internal UserEditorConfiguration Configuration { get; }

        internal string Source { get; }

        internal ExternalEditorCommand EffectiveCommand { get; }

        internal bool IsConfigured => EffectiveCommand != null;
    }

    internal sealed class UserEditorConfigurationResolver
    {
        readonly IEnvironmentVariableSource _environmentVariables;

        internal UserEditorConfigurationResolver(
            IEnvironmentVariableSource environmentVariables)
        {
            _environmentVariables = environmentVariables ??
                throw new ArgumentNullException(nameof(environmentVariables));
        }

        internal UserEditorResolution Resolve(UserConfigurationLoadResult loaded)
        {
            if (loaded == null)
                throw new ArgumentNullException(nameof(loaded));

            if (loaded.Status == UserConfigurationLoadStatus.Missing)
            {
                return new UserEditorResolution(
                    null,
                    "none",
                    null);
            }

            var editor = loaded.Configuration?.Editor ??
                throw new InvalidOperationException(
                    "A loaded user configuration did not contain editor settings.");
            return Resolve(editor);
        }

        internal UserEditorResolution Resolve(UserEditorConfiguration editor)
        {
            if (editor == null)
                throw new ArgumentNullException(nameof(editor));

            return editor.Selection switch
            {
                UserEditorSelection.Environment => ResolveEnvironment(editor),
                UserEditorSelection.Program => new UserEditorResolution(
                    editor,
                    "user configuration",
                    new ExternalEditorCommand(editor.Executable, editor.Arguments)),
                UserEditorSelection.Unset => new UserEditorResolution(
                    editor,
                    "user configuration",
                    null),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(editor),
                    "The user configuration contains an unknown editor selection.")
            };
        }

        UserEditorResolution ResolveEnvironment(UserEditorConfiguration editor)
        {
            var executable = _environmentVariables.Get(editor.Variable);
            var command = string.IsNullOrWhiteSpace(executable)
                ? null
                : new ExternalEditorCommand(executable, editor.Arguments);
            return new UserEditorResolution(
                editor,
                $"environment variable {editor.Variable}",
                command);
        }
    }
}
