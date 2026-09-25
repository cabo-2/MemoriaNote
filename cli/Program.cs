using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace MemoriaNote.Cli
{
    [Command("mn",
     Description = "A lightweight, cross-platform CLI for creating, organizing, and editing workspace-based notes")]
    [Subcommand(
        typeof(CreateCommand),
        typeof(FindCommand),
        typeof(EditCommand),
        typeof(NewCommand),
        typeof(ConfigCommand),
        typeof(WorkCommand),
        typeof(ListCommand),
        typeof(CatCommand),
        typeof(RenameCommand),
        typeof(DeleteCommand),
        typeof(ImportCommand),
        typeof(ExportCommand))]
    [HelpOption("--help")]
    class Program
    {
        static Lazy<CliComposition> _composition;

        public static async Task<int> Main(string[] args)
        {
            _composition = new Lazy<CliComposition>(CliComposition.CreateDefault);
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                return await CommandLineApplication.ExecuteAsync<Program>(
                    args,
                    cancellation.Token);
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
                if (_composition.IsValueCreated)
                    _composition.Value.Dispose();
                _composition = null;
            }
        }

        static CliComposition Composition =>
            (_composition ??
                throw new InvalidOperationException("CLI composition is not available."))
            .Value;

        static CliCommandHandlers CommandHandlers => Composition.CommandHandlers;

        [Option(
            "--workspace <directory>",
            Description = "Use this directory as the workspace for this invocation",
            Inherited = true)]
        public string Workspace { get; set; }

        protected Task<int> OnExecuteAsync(CommandLineApplication app)
        {
            app.ShowHint();
            return Task.FromResult((int)CliExitCode.Success);
        }

        [Command(
            "create",
            Description = "Create a live notebook without overwriting an existing file")]
        [HelpOption("--help")]
        class CreateCommand
        {
            public Program Parent { get; set; }

            [Argument(
                0,
                Name = "notebook",
                Description = "workspace-root notebook leaf name; .mnote is optional")]
            public (bool hasValue, string value) NotebookFile { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.CreateNotebook.ExecuteAsync(
                    Parent?.Workspace,
                    NotebookFile.hasValue ? NotebookFile.value : null,
                    cancellationToken);
            }
        }

        [Command(
            "find",
            Description = "Temporarily unavailable; use 'mn ls' for page names",
            ShowInHelpText = false)]
        [HelpOption("--help")]
        class FindCommand
        {
            [Argument(0, Name = "query", Description = "search query")]
            public string Query { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.Find.ExecuteAsync(Query, cancellationToken);
            }
        }

        [Command("edit", Description = "Edit text with an external editor")]
        [HelpOption("--help")]
        class EditCommand
        {
            public Program Parent { get; set; }

            [Argument(0, Name = "page-name", Description = "exact page name")]
            public (bool hasValue, string value) PageName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = "Use this workspace-root notebook leaf name; .mnote is optional")]
            public string Notebook { get; set; }

            [Option(
                "--editor <executable>",
                Description = "Use this editor for this invocation")]
            public string Editor { get; set; }

            [Option(
                "--editor-arg <argument>",
                CommandOptionType.MultipleValue,
                Description = "Pass an argument to --editor; may be repeated")]
            public string[] EditorArguments { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.Edit.ExecuteAsync(
                    Parent?.Workspace,
                    Notebook,
                    PageName.hasValue ? PageName.value : null,
                    PageId,
                    Editor,
                    EditorArguments,
                    cancellationToken);
            }
        }

        [Command("new", Description = "Create text command")]
        [HelpOption("--help")]
        class NewCommand
        {
            public Program Parent { get; set; }

            [Argument(0, Name = "name", Description = "text name")]
            public (bool hasValue, string value) Name { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = "Use this workspace-root notebook leaf name for this invocation; .mnote is optional")]
            public string Notebook { get; set; }

            [Option(
                "--editor <executable>",
                Description = "Use this editor for this invocation")]
            public string Editor { get; set; }

            [Option(
                "--editor-arg <argument>",
                CommandOptionType.MultipleValue,
                Description = "Pass an argument to --editor; may be repeated")]
            public string[] EditorArguments { get; set; }

            protected Task<int> OnExecuteAsync(
                CommandLineApplication app,
                CancellationToken cancellationToken)
            {
                if (!Name.hasValue)
                {
                    app.Error.WriteLine("Error: No name");
                    return Task.FromResult((int)CliExitCode.Validation);
                }

                return CommandHandlers.CreatePage.ExecuteAsync(
                    Parent?.Workspace,
                    Notebook,
                    Name.value,
                    Editor,
                    EditorArguments,
                    cancellationToken);
            }
        }

        [Command(
            "config",
            Description = "Manage configuration options",
            ShowInHelpText = false)]
        [Subcommand(typeof(ConfigEditCommand),
                    typeof(ConfigShowCommand))]
        [HelpOption("--help")]
        class ConfigCommand
        {
            protected Task<int> OnExecuteAsync(CommandLineApplication app)
            {
                app.ShowHelp();
                return Task.FromResult((int)CliExitCode.Success);
            }

            [Command("edit", Description = "Edit config")]
            class ConfigEditCommand
            {
                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.ConfigEdit.ExecuteAsync(cancellationToken);
                }
            }

            [Command("show", Description = "Show config")]
            class ConfigShowCommand
            {
                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.ConfigShow.ExecuteAsync(cancellationToken);
                }
            }
        }

        [Command("work", Description = "List, select and manage note options",
                ShowInHelpText = false,
                AllowArgumentSeparator = true,
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
        [Subcommand(typeof(WorkSelectCommand),
                    typeof(WorkListCommand),
                    typeof(WorkCreateCommand),
                    typeof(WorkEditCommand),
                    typeof(WorkAddCommand),
                    typeof(WorkRemoveCommand),
                    typeof(WorkBackupCommand),
                    typeof(WorkRestoreCommand))]
        [HelpOption("--help")]
        class WorkCommand
        {
            [Argument(0, "name", "note name")]
            public (bool hasValue, string value) Name { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                if (Name.hasValue)
                {
                    return CommandHandlers.WorkSelect.ExecuteAsync(
                        Name.value,
                        cancellationToken);
                }
                else
                {
                    return CommandHandlers.WorkList.ExecuteAsync(
                        completion: false,
                        cancellationToken);
                }
            }

            [Command("select", "curr", Description = "Choose and display a specific note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkSelectCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.WorkSelect.ExecuteAsync(
                        Name.value,
                        cancellationToken);
                }
            }

            [Command("list", "ls", Description = "Display a list of all notes",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkListCommand
            {
                [Option("--completion", Description = "Completion option")]
                public bool Completion { get; set; }

                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.WorkList.ExecuteAsync(
                        Completion,
                        cancellationToken);
                }
            }

            [Command("create", Description = "Create a new note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkCreateCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                [Argument(1, "title")]
                public (bool hasValue, string value) Title { get; set; }

                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.WorkCreate.ExecuteAsync(
                        Name.value,
                        Title.value,
                        cancellationToken);
                }
            }

            [Command("edit", Description = "Modify the content of the selected note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkEditCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.WorkEdit.ExecuteAsync(cancellationToken);
                }
            }
            [Command("add", Description = "Add a new note to the work list",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkAddCommand
            {
                [Argument(0, "path")]
                public (bool hasValue, string value) Path { get; set; }

                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.WorkAdd.ExecuteAsync(
                        Path.value,
                        cancellationToken);
                }
            }
            [Command("remove", Description = "Delete the selected note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkRemoveCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.WorkRemove.ExecuteAsync(
                        Name.value,
                        cancellationToken);
                }
            }

            [Command("backup", Description = "Create a backup of the selected note")]
            private class WorkBackupCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                [Option("--output <path>", Description = "Output file path")]
                public string OutputPath { get; set; }

                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.WorkBackup.ExecuteAsync(
                        Name.value,
                        OutputPath,
                        cancellationToken);
                }
            }

            [Command("restore", Description = "Restore a previously backed up note")]
            private class WorkRestoreCommand
            {
                [Argument(0, "zip-file")]
                public (bool hasValue, string value) InputPath { get; set; }

                [Option("--output-dir <dir>", Description = "Output directory")]
                public string OutputDir { get; set; }

                protected Task<int> OnExecuteAsync(
                    CommandLineApplication app,
                    CancellationToken cancellationToken)
                {
                    if (!InputPath.hasValue)
                    {
                        app.Error.WriteLine("Error: No input file");
                        return Task.FromResult((int)CliExitCode.Validation);
                    }

                    return CommandHandlers.WorkRestore.ExecuteAsync(
                        InputPath.value,
                        OutputDir,
                        cancellationToken);
                }
            }
        }

        [Command("ls", "list", Description = "List pages in stable page-name order")]
        [HelpOption("--help")]
        class ListCommand
        {
            public Program Parent { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = "Use this workspace-root notebook leaf name; .mnote is optional")]
            public string Notebook { get; set; }

            [Option("--limit <count>", Description = "Return at most this many pages")]
            public int? Limit { get; set; }

            [Option("-l|--long", Description = "Show all page metadata columns")]
            public bool Long { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.List.ExecuteAsync(
                    Parent?.Workspace,
                    Notebook,
                    Limit,
                    Long,
                    cancellationToken);
            }
        }

        [Command("cat", Description = "Write one page body to standard output")]
        [HelpOption("--help")]
        class CatCommand
        {
            public Program Parent { get; set; }

            [Argument(0, Name = "page-name", Description = "exact page name")]
            public (bool hasValue, string value) PageName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = "Use this workspace-root notebook leaf name; .mnote is optional")]
            public string Notebook { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.Cat.ExecuteAsync(
                    Parent?.Workspace,
                    Notebook,
                    PageName.hasValue ? PageName.value : null,
                    PageId,
                    cancellationToken);
            }
        }

        [Command("rename", Description = "Rename one page without changing its Page ID")]
        [HelpOption("--help")]
        class RenameCommand
        {
            public Program Parent { get; set; }

            [Argument(0, Name = "page-name", Description = "exact current page name")]
            public (bool hasValue, string value) PageName { get; set; }

            [Argument(1, Name = "new-name", Description = "replacement page name")]
            public (bool hasValue, string value) NewName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = "Use this workspace-root notebook leaf name; .mnote is optional")]
            public string Notebook { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                var selectingById = PageId != null;
                return CommandHandlers.RenamePage.ExecuteAsync(
                    Parent?.Workspace,
                    Notebook,
                    selectingById && !NewName.hasValue
                        ? null
                        : PageName.hasValue ? PageName.value : null,
                    PageId,
                    selectingById && !NewName.hasValue
                        ? PageName.hasValue ? PageName.value : null
                        : NewName.hasValue ? NewName.value : null,
                    cancellationToken);
            }
        }

        [Command("delete", Description = "Delete one page after confirmation")]
        [HelpOption("--help")]
        class DeleteCommand
        {
            public Program Parent { get; set; }

            [Argument(0, Name = "page-name", Description = "exact page name")]
            public (bool hasValue, string value) PageName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = "Use this workspace-root notebook leaf name; .mnote is optional")]
            public string Notebook { get; set; }

            [Option("--force", Description = "Delete without an interactive confirmation")]
            public bool Force { get; set; }

            [Option("--dry-run", Description = "Validate and show the target without deleting")]
            public bool DryRun { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.DeletePage.ExecuteAsync(
                    Parent?.Workspace,
                    Notebook,
                    PageName.hasValue ? PageName.value : null,
                    PageId,
                    Force,
                    DryRun,
                    cancellationToken);
            }
        }

        [Command(
            "import",
            Description = "Import text files",
            ShowInHelpText = false)]
        [HelpOption("--help")]
        class ImportCommand
        {
            [Argument(0, "import-dir")]
            public (bool hasValue, string value) ImportDir { get; set; }

            [Option("-r, --recursive", Description = "Sub directories recursively")]
            public bool Recursive { get; set; }

            protected Task<int> OnExecuteAsync(
                CommandLineApplication app,
                CancellationToken cancellationToken)
            {
                if (!ImportDir.hasValue)
                {
                    app.ShowHelp();
                    return Task.FromResult((int)CliExitCode.Validation);
                }

                return CommandHandlers.Import.ExecuteAsync(
                    ImportDir.value,
                    Recursive,
                    cancellationToken);
            }
        }

        [Command(
            "export",
            Description = "Export text files",
            ShowInHelpText = false)]
        [HelpOption("--help")]
        class ExportCommand
        {
            [Argument(0, "export-dir")]
            public (bool hasValue, string value) ExportDir { get; set; }

            protected Task<int> OnExecuteAsync(
                CommandLineApplication app,
                CancellationToken cancellationToken)
            {
                if (!ExportDir.hasValue)
                {
                    app.ShowHelp();
                    return Task.FromResult((int)CliExitCode.Validation);
                }

                return CommandHandlers.Export.ExecuteAsync(
                    ExportDir.value,
                    cancellationToken);
            }
        }
    }
}
