using System;

namespace MemoriaNote.Cli
{
    internal sealed class CliCommandHandlers
    {
        internal CliCommandHandlers(
            CreateNotebookCommandHandler createNotebook,
            UseNotebookCommandHandler useNotebook,
            StatusCommandHandler status,
            NotebookListCommandHandler notebookList,
            NotebookBackupCommandHandler notebookBackup,
            NotebookRestoreCommandHandler notebookRestore,
            NotebookImportCommandHandler notebookImport,
            NotebookExportCommandHandler notebookExport,
            NotebookMetadataCommandHandler notebookMetadata,
            EditCommandHandler edit,
            NewCommandHandler createPage,
            UserConfigPathCommandHandler userConfigPath,
            UserConfigShowCommandHandler userConfigShow,
            UserConfigValidateCommandHandler userConfigValidate,
            UserConfigEditorSetupCommandHandler userConfigEditorSetup,
            UserConfigEditorShowCommandHandler userConfigEditorShow,
            UserConfigEditorUnsetCommandHandler userConfigEditorUnset,
            ListCommandHandler list,
            CatCommandHandler cat,
            RenamePageCommandHandler renamePage,
            DeletePageCommandHandler deletePage)
        {
            CreateNotebook = createNotebook ??
                throw new ArgumentNullException(nameof(createNotebook));
            UseNotebook = useNotebook ??
                throw new ArgumentNullException(nameof(useNotebook));
            Status = status ?? throw new ArgumentNullException(nameof(status));
            NotebookList = notebookList ??
                throw new ArgumentNullException(nameof(notebookList));
            NotebookBackup = notebookBackup ??
                throw new ArgumentNullException(nameof(notebookBackup));
            NotebookRestore = notebookRestore ??
                throw new ArgumentNullException(nameof(notebookRestore));
            NotebookImport = notebookImport ??
                throw new ArgumentNullException(nameof(notebookImport));
            NotebookExport = notebookExport ??
                throw new ArgumentNullException(nameof(notebookExport));
            NotebookMetadata = notebookMetadata ??
                throw new ArgumentNullException(nameof(notebookMetadata));
            Edit = edit ?? throw new ArgumentNullException(nameof(edit));
            CreatePage = createPage ??
                throw new ArgumentNullException(nameof(createPage));
            UserConfigPath = userConfigPath ??
                throw new ArgumentNullException(nameof(userConfigPath));
            UserConfigShow = userConfigShow ??
                throw new ArgumentNullException(nameof(userConfigShow));
            UserConfigValidate = userConfigValidate ??
                throw new ArgumentNullException(nameof(userConfigValidate));
            UserConfigEditorSetup = userConfigEditorSetup ??
                throw new ArgumentNullException(nameof(userConfigEditorSetup));
            UserConfigEditorShow = userConfigEditorShow ??
                throw new ArgumentNullException(nameof(userConfigEditorShow));
            UserConfigEditorUnset = userConfigEditorUnset ??
                throw new ArgumentNullException(nameof(userConfigEditorUnset));
            List = list ?? throw new ArgumentNullException(nameof(list));
            Cat = cat ?? throw new ArgumentNullException(nameof(cat));
            RenamePage = renamePage ??
                throw new ArgumentNullException(nameof(renamePage));
            DeletePage = deletePage ??
                throw new ArgumentNullException(nameof(deletePage));
        }

        internal CreateNotebookCommandHandler CreateNotebook { get; }

        internal UseNotebookCommandHandler UseNotebook { get; }

        internal StatusCommandHandler Status { get; }

        internal NotebookListCommandHandler NotebookList { get; }

        internal NotebookBackupCommandHandler NotebookBackup { get; }

        internal NotebookRestoreCommandHandler NotebookRestore { get; }

        internal NotebookImportCommandHandler NotebookImport { get; }

        internal NotebookExportCommandHandler NotebookExport { get; }

        internal NotebookMetadataCommandHandler NotebookMetadata { get; }

        internal EditCommandHandler Edit { get; }

        internal NewCommandHandler CreatePage { get; }

        internal UserConfigPathCommandHandler UserConfigPath { get; }

        internal UserConfigShowCommandHandler UserConfigShow { get; }

        internal UserConfigValidateCommandHandler UserConfigValidate { get; }

        internal UserConfigEditorSetupCommandHandler UserConfigEditorSetup { get; }

        internal UserConfigEditorShowCommandHandler UserConfigEditorShow { get; }

        internal UserConfigEditorUnsetCommandHandler UserConfigEditorUnset { get; }

        internal ListCommandHandler List { get; }

        internal CatCommandHandler Cat { get; }

        internal RenamePageCommandHandler RenamePage { get; }

        internal DeletePageCommandHandler DeletePage { get; }
    }
}
