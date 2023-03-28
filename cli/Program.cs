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
            Configuration.Instance = ConfigurationCli.Create<ConfigurationCli>();

            var vm = new MemoriaNoteViewModel();
            var sc = new ScreenController();

            Configuration.Instance.Save();
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
                    typeof(ConfigShowCommand),
                    typeof(ConfigInitCommand))]
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

            [Command("init", Description = "Init config")]
            class ConfigInitCommand
            {
                protected int OnExecute(CommandLineApplication app)
                {
                    return new CommandCenter().ConfigInit();
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
                    typeof(WorkRemoveCommand))]
        [HelpOption("--help")]
        class WorkCommand
        {
            [Argument(0, "name", "select note")]
            public (bool hasValue, string value) Name { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                return new CommandCenter().Work();
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
                    return new CommandCenter().WorkSelect();
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

                protected IReadOnlyList<string> RemainingArguments { get; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkCreate();
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
                [Argument(0, "name")]
                public (bool hasValue, string value) Name { get; set; }

                protected IReadOnlyList<string> RemainingArguments { get; }

                protected int OnExecute(IConsole console)
                {
                    return new CommandCenter().WorkAdd();
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
                    return new CommandCenter().WorkRemove();
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