using System;

namespace MemoriaNote.Cli.UserConfig
{
    internal sealed class UserConfigurationSnapshot
    {
        internal UserConfigurationSnapshot(
            string path,
            UserConfigurationLoadResult loaded,
            UserEditorResolution editor)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Loaded = loaded ?? throw new ArgumentNullException(nameof(loaded));
            Editor = editor ?? throw new ArgumentNullException(nameof(editor));
        }

        internal string Path { get; }

        internal UserConfigurationLoadResult Loaded { get; }

        internal UserEditorResolution Editor { get; }
    }

    internal sealed class UserConfigurationWorkflow
    {
        readonly IUserConfigurationStore _store;
        readonly UserEditorConfigurationResolver _editorResolver;

        internal UserConfigurationWorkflow(
            ApplicationPaths applicationPaths,
            IUserConfigurationStore store,
            UserEditorConfigurationResolver editorResolver)
        {
            if (applicationPaths == null)
                throw new ArgumentNullException(nameof(applicationPaths));

            Path = UserConfigurationContract.GetPath(applicationPaths);
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _editorResolver = editorResolver ??
                throw new ArgumentNullException(nameof(editorResolver));
        }

        internal string Path { get; }

        internal bool PathExists => System.IO.Path.Exists(Path);

        internal UserConfigurationLoadResult Validate()
        {
            return _store.Load();
        }

        internal UserConfigurationSnapshot Inspect()
        {
            var loaded = _store.Load();
            return new UserConfigurationSnapshot(
                Path,
                loaded,
                _editorResolver.Resolve(loaded));
        }

        internal UserEditorResolution PreviewEditor(UserEditorConfiguration editor)
        {
            return _editorResolver.Resolve(
                editor ?? throw new ArgumentNullException(nameof(editor)));
        }

        internal UserConfigurationSaveResult SaveEditor(
            UserEditorConfiguration editor,
            UserConfigurationRevision expectedRevision)
        {
            if (editor == null)
                throw new ArgumentNullException(nameof(editor));
            if (expectedRevision == null)
                throw new ArgumentNullException(nameof(expectedRevision));

            return _store.Save(
                new UserConfiguration(editor),
                expectedRevision);
        }
    }
}
