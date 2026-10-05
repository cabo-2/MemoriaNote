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
            ConfigEditCommandHandler configEdit,
            ConfigShowCommandHandler configShow,
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
            ConfigEdit = configEdit ??
                throw new ArgumentNullException(nameof(configEdit));
            ConfigShow = configShow ??
                throw new ArgumentNullException(nameof(configShow));
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

        internal ConfigEditCommandHandler ConfigEdit { get; }

        internal ConfigShowCommandHandler ConfigShow { get; }

        internal ListCommandHandler List { get; }

        internal CatCommandHandler Cat { get; }

        internal RenamePageCommandHandler RenamePage { get; }

        internal DeletePageCommandHandler DeletePage { get; }
    }
}
