using System;
using System.Linq;
using System.Reflection;
using System.Reactive.Concurrency;
using ReactiveUI;
using System.Collections.Generic;
using Terminal.Gui;
using McMaster.Extensions.CommandLineUtils;

namespace MemoriaNote.Cli
{
    [Command("mn",
     Description = "Memoria Note CLI - A simple, .NET Terminal.Gui based, Text viewer and editor")]
    [Subcommand(
        typeof(EditCommand),
        typeof(NewCommand),
        typeof(ConfigCommand),
        typeof(WorkCommand),
        typeof(ListCommand),
        typeof(GetCommand),
        typeof(ImportCommand),
        typeof(ExportCommand))]
    [HelpOption("--help")]
    class Program
    {
        public static void Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        protected int OnExecute(CommandLineApplication app)
        {
            app.ShowHint();
            return 0;
        }

        [Command("edit", "e", Description = "Edit, browse and search text commands")]
        [HelpOption("--help")]
        class EditCommand
        {
            [Argument(0, Name = "name", Description = "text name")]
            public string Name { get; set; }

            [Option("--uuid=<uuid>", Description = "uuid")]
            public (bool hasValue, string value) Uuid { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                return new CommandCenter().Edit(Name);
            }
        }

        [Command("new", Description = "Create text command")]
        [HelpOption("--help")]
        class NewCommand
        {
            [Argument(0, Name = "name", Description = "text name")]
            public (bool hasValue, string value) Name { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                if (!Name.hasValue)
                {
                    Console.Error.WriteLine("Error: No name");
                    return -1;
                }

                return new CommandCenter().New(Name.value);
            }
        }

        [Command("config", Description = "Manage configuration options")]
        [Subcommand(typeof(ConfigEditCommand),
                    typeof(ConfigShowCommand))]
        [HelpOption("--help")]
        class ConfigCommand
        {
            protected int OnExecute(CommandLineApplication app)
            {
                app.ShowHelp();
                return 0;
            }

            [Command("edit", Description = "Edit config")]
            class ConfigEditCommand
            {
                protected int OnExecute(CommandLineApplication app)
                {
                    return new CommandCenter().ConfigEdit();
                }
            }

            [Command("show", Description = "Show config")]
            class ConfigShowCommand
            {
                protected int OnExecute(CommandLineApplication app)
                {
                    return new CommandCenter().ConfigShow();
                }
            }
        }

        [Command("work", "w", "branch", Description = "List, change and manage note options",
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
            [Argument(0, "name", "select note")]
            public (bool hasValue, string value) Name { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                if (Name.hasValue)
                    return new CommandCenter().WorkSelect(Name.value);
                else
                    return new CommandCenter().WorkList();
            }

            [Command("select", "curr", Description = "select note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkSelectCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                protected IReadOnlyList<string> RemainingArguments { get; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkSelect(Name.value);
                }
            }

            [Command("list", "ls", Description = "List notes",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkListCommand
            {
                protected IReadOnlyList<string> RemainingArguments { get; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkList();
                }
            }

            [Command("create", Description = "Create note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkCreateCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                [Argument(1, "title")]
                public (bool hasValue, string value) Title { get; set; }

                protected IReadOnlyList<string> RemainingArguments { get; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkCreate(Name.value, Title.value);
                }
            }

            [Command("edit", Description = "Edit note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkEditCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                protected IReadOnlyList<string> RemainingArguments { get; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkEdit();
                }
            }
            [Command("add", Description = "Add note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkAddCommand
            {
                [Argument(0, "path")]
                public (bool hasValue, string value) Path { get; set; }

                protected IReadOnlyList<string> RemainingArguments { get; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkAdd(Path.value);
                }
            }
            [Command("remove", Description = "Remove note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkRemoveCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                [Option("--purge", Description = "The note PURGE option *** WARN ***")]
                public bool IsPurge { get; set; }

                protected IReadOnlyList<string> RemainingArguments { get; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkRemove(Name.value);
                }
            }

            [Command("backup", Description = "Backup note")]
            private class WorkBackupCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                [Option("--outputFile", Description = "")]
                public (bool hasValue, string value) OutputPath { get; set; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkBackup(Name.value, OutputPath.value);
                }
            }

            [Command("restore", Description = "Restore note")]
            private class WorkRestoreCommand
            {
                [Argument(0, "zipfile")]
                public (bool hasValue, string value) InputPath { get; set; }

                [Option("--outputDir", Description = "")]
                public (bool hasValue, string value) OutputDir { get; set; }

                protected int OnExecute(IConsole console)
                {
                    if (!InputPath.hasValue)
                    {
                        Console.Error.WriteLine("Error: No input file");
                        return -1;
                    }

                    return new CommandCenter().WorkRestore(InputPath.value, OutputDir.value);
                }
            }
        }

        [Command("list", "ls", Description = "List text")]
        [HelpOption("--help")]
        class ListCommand
        {
            protected int OnExecute(CommandLineApplication app)
            {
                new CommandCenter().List();
                return 0;
            }
        }

        [Command("get", "g", Description = "get item")]
        [HelpOption("--help")]
        class GetCommand
        {

            [Argument(1, "name", "text name")]
            public (bool hasValue, string value) Name { get; set; }

            [Option("--uuid=<uuid>", Description = "text uuid")]
            public (bool hasValue, string value) Uuid { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                if (!Name.hasValue)
                {
                    Console.Error.WriteLine("Error: No name");
                    return -1;
                }

                return new CommandCenter().Get(Name.value);
            }
        }

        [Command("import", Description = "Import text files")]
        [HelpOption("--help")]
        class ImportCommand
        {
            [Argument(0, "importDir")]
            public (bool hasValue, string value) ImportDir { get; set; }

            [Option("--work=<work>", Description = "Note")]
            public (bool hasValue, string value) Work { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                if (!ImportDir.hasValue)
                {
                    app.ShowHelp();
                    return -1;
                }

                return new CommandCenter().Import(ImportDir.value);
            }
        }

        [Command("export", Description = "Export text files")]
        [HelpOption("--help")]
        class ExportCommand
        {
            [Argument(0, "exportDir")]
            public (bool hasValue, string value) ExportDir { get; set; }

            [Option("--work=<work>", Description = "Note")]
            public (bool hasValue, string value) Work { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                if (!ExportDir.hasValue)
                {
                    app.ShowHelp();
                    return -1;
                }

                return new CommandCenter().Export(ExportDir.value);
            }
        }
    }
}