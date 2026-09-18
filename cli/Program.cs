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
        typeof(FindCommand),
        typeof(EditCommand),
        typeof(NewCommand),
        typeof(ConfigCommand),
        typeof(WorkCommand),
        typeof(ListCommand),
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

        protected Task<int> OnExecuteAsync(CommandLineApplication app)
        {
            app.ShowHint();
            return Task.FromResult((int)CliExitCode.Success);
        }

        [Command(
            "find",
            Description = "Temporarily unavailable after TUI removal; use 'mn list [name]' for page names, not full-text search. " +
                "Full-text search will be redesigned as a separate follow-up.")]
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
            [Argument(0, Name = "name", Description = "text name")]
            public (bool hasValue, string value) Name { get; set; }

            protected Task<int> OnExecuteAsync(
                CommandLineApplication app,
                CancellationToken cancellationToken)
            {
                if (!Name.hasValue)
                {
                    app.Error.WriteLine("Error: No name");
                    return Task.FromResult((int)CliExitCode.Validation);
                }

                return CommandHandlers.Edit.ExecuteAsync(
                    Name.value,
                    cancellationToken);
            }
        }

        [Command("new", Description = "Create text command")]
        [HelpOption("--help")]
        class NewCommand
        {
            [Argument(0, Name = "name", Description = "text name")]
            public (bool hasValue, string value) Name { get; set; }

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
                    Name.value,
                    cancellationToken);
            }
        }

        [Command("config", Description = "Manage configuration options")]
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

        [Command("list", "ls", Description = "List text")]
        [HelpOption("--help")]
        class ListCommand
        {
            [Argument(0, "name")]
            public (bool hasValue, string value) Name { get; set; }

            [Option("--completion", Description = "Completion option")]
            public bool Completion { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.List.ExecuteAsync(
                    Name.value,
                    Completion,
                    cancellationToken);
            }
        }

        [Command("import", Description = "Import text files")]
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

        [Command("export", Description = "Export text files")]
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
