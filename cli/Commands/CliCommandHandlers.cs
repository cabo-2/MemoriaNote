using System;

namespace MemoriaNote.Cli
{
    internal sealed class CliCommandHandlers
    {
        internal CliCommandHandlers(
            CreateNotebookCommandHandler createNotebook,
            FindCommandHandler find,
            EditCommandHandler edit,
            NewCommandHandler createPage,
            ConfigEditCommandHandler configEdit,
            ConfigShowCommandHandler configShow,
            ListCommandHandler list,
            CatCommandHandler cat,
            WorkSelectCommandHandler workSelect,
            WorkListCommandHandler workList,
            WorkCreateCommandHandler workCreate,
            WorkEditCommandHandler workEdit,
            WorkAddCommandHandler workAdd,
            WorkRemoveCommandHandler workRemove,
            WorkBackupCommandHandler workBackup,
            WorkRestoreCommandHandler workRestore,
            ImportCommandHandler import,
            ExportCommandHandler export)
        {
            CreateNotebook = createNotebook ??
                throw new ArgumentNullException(nameof(createNotebook));
            Find = find ?? throw new ArgumentNullException(nameof(find));
            Edit = edit ?? throw new ArgumentNullException(nameof(edit));
            CreatePage = createPage ??
                throw new ArgumentNullException(nameof(createPage));
            ConfigEdit = configEdit ??
                throw new ArgumentNullException(nameof(configEdit));
            ConfigShow = configShow ??
                throw new ArgumentNullException(nameof(configShow));
            List = list ?? throw new ArgumentNullException(nameof(list));
            Cat = cat ?? throw new ArgumentNullException(nameof(cat));
            WorkSelect = workSelect ??
                throw new ArgumentNullException(nameof(workSelect));
            WorkList = workList ?? throw new ArgumentNullException(nameof(workList));
            WorkCreate = workCreate ??
                throw new ArgumentNullException(nameof(workCreate));
            WorkEdit = workEdit ?? throw new ArgumentNullException(nameof(workEdit));
            WorkAdd = workAdd ?? throw new ArgumentNullException(nameof(workAdd));
            WorkRemove = workRemove ??
                throw new ArgumentNullException(nameof(workRemove));
            WorkBackup = workBackup ??
                throw new ArgumentNullException(nameof(workBackup));
            WorkRestore = workRestore ??
                throw new ArgumentNullException(nameof(workRestore));
            Import = import ?? throw new ArgumentNullException(nameof(import));
            Export = export ?? throw new ArgumentNullException(nameof(export));
        }

        internal CreateNotebookCommandHandler CreateNotebook { get; }

        internal FindCommandHandler Find { get; }

        internal EditCommandHandler Edit { get; }

        internal NewCommandHandler CreatePage { get; }

        internal ConfigEditCommandHandler ConfigEdit { get; }

        internal ConfigShowCommandHandler ConfigShow { get; }

        internal ListCommandHandler List { get; }

        internal CatCommandHandler Cat { get; }

        internal WorkSelectCommandHandler WorkSelect { get; }

        internal WorkListCommandHandler WorkList { get; }

        internal WorkCreateCommandHandler WorkCreate { get; }

        internal WorkEditCommandHandler WorkEdit { get; }

        internal WorkAddCommandHandler WorkAdd { get; }

        internal WorkRemoveCommandHandler WorkRemove { get; }

        internal WorkBackupCommandHandler WorkBackup { get; }

        internal WorkRestoreCommandHandler WorkRestore { get; }

        internal ImportCommandHandler Import { get; }

        internal ExportCommandHandler Export { get; }
    }
}
