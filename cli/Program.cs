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
        typeof(PostCommand))]
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
                Console.WriteLine("Editor was selected");

                // setup config
                Configuration.Instance = ConfigurationCli.Create<ConfigurationCli>();

                // generate view model
                var vm = new MemoriaNoteViewModel();
                vm.SearchEntry = Name;

                var sc = new ScreenController();
                sc.RequestHome();

                sc.Start(vm);
                
                // save config
                Configuration.Instance.Save();

                return 0;
            }
        }

        [Command("new", Description = "Create text command")]
        [HelpOption("--help")]
        class NewCommand
        {
            [Argument(0, Name = "name", Description = "text name")]
            public string Name { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                Console.WriteLine("New was selected");

                // setup config
                Configuration.Instance = ConfigurationCli.Create<ConfigurationCli>();

                // generate view model
                var vm = new MemoriaNoteViewModel();
                vm.EditingTitle = Name;
                vm.EditingState = EditingState.Create;

                var sc = new ScreenController();
                sc.RequestHome();
                sc.RequestEditor();

                sc.Start(vm);

                // save config
                Configuration.Instance.Save();
                
                return 0;
            }
        }

        [Command("config", Description = "Manage configuration options")]
        [HelpOption("--help")]
        class ConfigCommand
        {
            [Argument(0, Name = "word", Description = "search word")]
            public string Word { get; set; }

            [Argument(1, Name = "title", Description = "page title")]
            public string Title { get; set; }

            [Option("--uuid=<uuid>", Description = "page uuid")]
            public (bool hasValue, string value) Uuid { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                Console.WriteLine("Search was selected");


                return 0;
            }
        }

        [Command("work", "w", "branch", Description = "List, change and manage note options",
                AllowArgumentSeparator = true,
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
        [Subcommand(typeof(WorkListCommand))]
        [HelpOption("--help")]
        class WorkCommand
        {
            [Argument(0)]
            public string Note { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                Console.WriteLine("Work was selected");
                return 0;
            }

            [Command("list", "ls", Description = "List notes",
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
            private class WorkListCommand
            {
                [Option(Description = "Show all containers (default shows just running)")]
                public bool All { get; }

                protected IReadOnlyList<string> RemainingArguments { get; }

                protected void OnExecute(IConsole console)
                {
                    console.WriteLine(string.Join("\n",
                        "IMAGES",
                        "--------------------",
                        "microsoft/dotnet:2.0"));
                }
            }
        }

        [Command("list", "ls", Description = "List text")]
        [HelpOption("--help")]
        class ListCommand
        {
            protected int OnExecute(CommandLineApplication app)
            {
                Console.WriteLine("List was selected");
                return 0;
            }
        }

        [Command("get", "g", Description = "get item")]
        [HelpOption("--help")]
        class GetCommand
        {

            [Argument(1, "word", "search word")]
            public (bool hasValue, string value) Word { get; set; }

            [Option("--uuid=<uuid>", Description = "page uuid")]
            public (bool hasValue, string value) Uuid { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                Console.WriteLine("Get was selected");

                // setup config
                Configuration.Instance = Configuration.Create<ConfigurationCli>();

                // generate view model
                var vm = new MemoriaNoteViewModel();
                //vm.OnActivate();

                //ViewModel.Archive.Migrate();
                //ViewModel.Archive.Load();
                if (Word.hasValue)
                {
                    Console.WriteLine("word:" + Word.value);
                }
                else
                {
                    Console.Error.WriteLine("Error:No search word");
                    return -1;
                }

                int skipCount = 0;
                int takeCount = Configuration.Instance.Search.MaxViewResultCount;

                var wg = vm.Workgroup;
                var result = wg.SearchContents(Word.value, skipCount, takeCount, SearchRangeType.Note);

                Console.WriteLine(result.ToString());
                foreach (var content in result.Contents)
                {
                    Console.WriteLine(content.Title);
                    var page = wg.SelectedNote.Read(content.Guid);
                    Console.WriteLine(page.Text);
                    //Console.WriteLine(content.)
                    break;
                }

                Configuration.Instance.Save();

                return 0;
            }
        }

        [Command("post", "p", Description = "post item")]
        [HelpOption("--help")]
        class PostCommand
        {
            protected int OnExecute(CommandLineApplication app)
            {
                Console.WriteLine("Post was selected");
                return 0;
            }
        }
    }
}