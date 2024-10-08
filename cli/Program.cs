﻿using System;
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
        public static void Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        protected int OnExecute(CommandLineApplication app)
        {
            app.ShowHint();
            return 0;
        }

        [Command("find", Description = "Find and browse text commands")]
        [HelpOption("--help")]
        class FindCommand
        {
            [Argument(0, Name = "name", Description = "text name")]
            public string Name { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                return new CommandCenter().Find(Name);
            }
        }

        [Command("edit", Description = "Edit and manage text commands")]
        [HelpOption("--help")]
        class EditCommand
        {
            [Argument(0, Name = "name", Description = "text name")]
            public string Name { get; set; }

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

            protected int OnExecute(CommandLineApplication app)
            {
                if (Name.hasValue)
                    return new CommandCenter().WorkSelect(Name.value);
                else
                    return new CommandCenter().WorkList();
            }

            [Command("select", "curr", Description = "Choose and display a specific note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkSelectCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkSelect(Name.value);
                }
            }

            [Command("list", "ls", Description = "Display a list of all notes",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkListCommand
            {
                [Option("--completion", Description = "Completion option")]
                public bool Completion { get; set; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkList(Completion);
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

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkCreate(Name.value, Title.value);
                }
            }

            [Command("edit", Description = "Modify the content of the selected note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkEditCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkEdit();
                }
            }
            [Command("add", Description = "Add a new note to the work list",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkAddCommand
            {
                [Argument(0, "path")]
                public (bool hasValue, string value) Path { get; set; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkAdd(Path.value);
                }
            }
            [Command("remove", Description = "Delete the selected note",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkRemoveCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkRemove(Name.value);
                }
            }

            [Command("backup", Description = "Create a backup of the selected note")]
            private class WorkBackupCommand
            {
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                [Option("--output", Description = "Output file path")]
                public (bool hasValue, string value) OutputPath { get; set; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkBackup(Name.value, OutputPath.value);
                }
            }

            [Command("restore", Description = "Restore a previously backed up note")]
            private class WorkRestoreCommand
            {
                [Argument(0, "zip-file")]
                public (bool hasValue, string value) InputPath { get; set; }

                [Option("--output-dir", Description = "Output directory")]
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
            [Argument(0, "name")]
            public (bool hasValue, string value) Name { get; set; }

            [Option("--completion", Description = "Completion option")]
            public bool Completion { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                new CommandCenter().List(Name.value, Completion);
                return 0;
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

            protected int OnExecute(CommandLineApplication app)
            {
                if (!ImportDir.hasValue)
                {
                    app.ShowHelp();
                    return -1;
                }

                return new CommandCenter().Import(ImportDir.value, Recursive);
            }
        }

        [Command("export", Description = "Export text files")]
        [HelpOption("--help")]
        class ExportCommand
        {
            [Argument(0, "export-dir")]
            public (bool hasValue, string value) ExportDir { get; set; }

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