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
        typeof(SearchCommand),
        typeof(BranchCommand),
        typeof(ListCommand),
        typeof(GetCommand),
        typeof(PostCommand))]
    class Program : CommandBase
    {
        public static void Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        public MemoriaNoteViewModel ViewModel { get; set; }

        [Option("--conf <path>", Description = "config json path" )]
        [FileExists]
        public string ConfigJsonPath { get; set; }

        protected override int OnExecute(CommandLineApplication app)
        {
            Console.WriteLine("No selected"); 
            app.ShowHint();               
            return 0;
        }
    }

    [Command("edit", "e", Description = "edit item")]
    class EditCommand : SubcommandBase
    {
        [Argument(0, Name = "title", Description = "page title")]
        public string Title { get; set; }

        [Option("--uuid=<uuid>", Description = "page uuid" )]
        public (bool hasValue, string value) Uuid { get; set; }

        protected override int OnExecute(CommandLineApplication app)
        {
            Console.WriteLine("Editor was selected");    
            // config setup

            // generate view model
            Parent.ViewModel = new MemoriaNoteViewModel();   

            Application.Init();
            RxApp.MainThreadScheduler = TerminalScheduler.Default;
            RxApp.TaskpoolScheduler = TaskPoolScheduler.Default;
            //Application.Run(new LoginView(new LoginViewModel()));
            Application.Run(new MemoriaNoteWindow());
            Application.Shutdown();

            return 0;
        }
    }

    [Command("search", "s", Description = "search items")]
    class SearchCommand : SubcommandBase
    {
        [Argument(0, Name = "title", Description = "page title")]
        public string Title { get; set; }

        [Option("--uuid=<uuid>", Description = "page uuid" )]
        public (bool hasValue, string value) Uuid { get; set; }

        protected override int OnExecute(CommandLineApplication app)
        {
            Console.WriteLine("Search was selected");

            return 0;
        }
    }

    [Command("branch", "b", Description = "list and change note")]
    class BranchCommand : SubcommandBase
    {
        [Argument(0)]
        public string Note { get; set; }

        protected override int OnExecute(CommandLineApplication app)
        {
            Console.WriteLine("Change was selected");
            return 0;
        }
    }

    [Command("list", "ls", Description = "list items")]
    class ListCommand : SubcommandBase
    {
        protected override int OnExecute(CommandLineApplication app)
        {
            Console.WriteLine("List was selected");
            return 0;
        }
    }

    [Command("get", "g", Description = "get item")]
    class GetCommand : SubcommandBase
    {
        protected override int OnExecute(CommandLineApplication app)
        {
            Console.WriteLine("Get was selected");
            return 0;
        }
    }

    [Command("post", "p", Description = "post item")]
    class PostCommand : SubcommandBase
    {
        protected override int OnExecute(CommandLineApplication app)
        {
            Console.WriteLine("Post was selected");
            return 0;
        }
    }

    abstract class SubcommandBase : CommandBase
    {
        protected Program Parent { get; set; }
    }

    [HelpOption("--help")]
    abstract class CommandBase
    {
        protected abstract int OnExecute(CommandLineApplication app);
    }
}