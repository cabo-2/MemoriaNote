using System;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace MemoriaNote.Cli
{
    [Command("mn",
     Description = "A lightweight, cross-platform CLI for workspace notebooks")]
    [Subcommand(
        typeof(CreateCommand),
        typeof(UseCommand),
        typeof(StatusCommand),
        typeof(NotebooksCommand),
        typeof(EditCommand),
        typeof(NewCommand),
        typeof(ConfigCommand),
        typeof(ListCommand),
        typeof(CatCommand),
        typeof(RenameCommand),
        typeof(DeleteCommand))]
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

        protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
        {
            return CommandHandlers.Status.ExecuteAsync(
                Workspace,
                cancellationToken);
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
            "use",
            Description = "Select a notebook or return to the workspace root")]
        [HelpOption("--help")]
        class UseCommand
        {
            public Program Parent { get; set; }

            [Argument(
                0,
                Name = "notebook",
                Description = "workspace-root notebook leaf name; .mnote is optional")]
            public (bool hasValue, string value) Notebook { get; set; }

            [Option("--root", Description = "Select the virtual workspace root")]
            public bool Root { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.UseNotebook.ExecuteAsync(
                    Parent?.Workspace,
                    Notebook.hasValue ? Notebook.value : null,
                    Root,
                    cancellationToken);
            }
        }

        [Command(
            "status",
            Description = "Show the workspace location and selected notebook state")]
        [HelpOption("--help")]
        class StatusCommand
        {
            public Program Parent { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.Status.ExecuteAsync(
                    Parent?.Workspace,
                    cancellationToken);
            }
        }

        [Command(
            "notebooks",
            Description = "Inspect workspace-root notebooks")]
        [Subcommand(typeof(NotebooksListCommand),
                    typeof(NotebooksBackupCommand),
                    typeof(NotebooksRestoreCommand),
                    typeof(NotebooksImportCommand),
                    typeof(NotebooksExportCommand),
                    typeof(NotebooksMetadataCommand))]
        [HelpOption("--help")]
        class NotebooksCommand
        {
            public Program Parent { get; set; }

            protected Task<int> OnExecuteAsync(CommandLineApplication app)
            {
                app.ShowHelp();
                return Task.FromResult((int)CliExitCode.Success);
            }

            [Command(
                "list",
                Description = "List notebooks in stable file-name order")]
            [HelpOption("--help")]
            class NotebooksListCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Option("-l|--long", Description = "Show validation and file details")]
                public bool Long { get; set; }

                protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
                {
                    return CommandHandlers.NotebookList.ExecuteAsync(
                        Parent?.Parent?.Workspace,
                        Long,
                        cancellationToken);
                }
            }

            [Command(
                "backup",
                Description = "Create a validated archive v1 backup",
                ExtendedHelpText =
                    "Syntax: mn notebooks backup [<archive>] [--notebook <notebook>]" +
                    "\nOmit <archive> or use - to write the archive to redirected " +
                    "standard output.")]
            [HelpOption("--help")]
            class NotebooksBackupCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Argument(
                    0,
                    Name = "archive",
                    Description = "new archive file; omit or use - for standard output")]
                public (bool hasValue, string value) Archive { get; set; }

                [Option(
                    "--notebook <notebook>",
                    Description = "Use this workspace-root notebook leaf name for this invocation; .mnote is optional")]
                public string Notebook { get; set; }

                protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
                {
                    return CommandHandlers.NotebookBackup.ExecuteAsync(
                        Parent?.Parent?.Workspace,
                        Notebook,
                        Archive.hasValue ? Archive.value : null,
                        cancellationToken);
                }
            }

            [Command(
                "restore",
                Description = "Restore a validated archive v1 to a new notebook",
                ExtendedHelpText =
                    "Syntax: mn notebooks restore [<archive>] --target <notebook> [--dry-run]" +
                    "\nOmit <archive> or use - to read the archive from redirected " +
                    "standard input.")]
            [HelpOption("--help")]
            class NotebooksRestoreCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Argument(
                    0,
                    Name = "archive",
                    Description = "archive file; omit or use - for standard input")]
                public (bool hasValue, string value) Archive { get; set; }

                [Option(
                    "--target <notebook>",
                    Description = "new workspace-root notebook leaf name; .mnote is optional")]
                public string Target { get; set; }

                [Option(
                    "--dry-run",
                    Description = "fully validate the restore without creating a notebook")]
                public bool DryRun { get; set; }

                protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
                {
                    return CommandHandlers.NotebookRestore.ExecuteAsync(
                        Parent?.Parent?.Workspace,
                        Target,
                        Archive.hasValue ? Archive.value : null,
                        DryRun,
                        cancellationToken);
                }
            }

            [Command(
                "import",
                Description = "Import flat strict-UTF-8 text files into a notebook",
                ExtendedHelpText =
                    "Syntax: mn notebooks import <directory> [--notebook <notebook>] " +
                    "[--on-conflict fail|skip|replace] [--dry-run]")]
            [HelpOption("--help")]
            class NotebooksImportCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Argument(
                    0,
                    Name = "directory",
                    Description = "existing directory containing flat lowercase .txt files")]
                public (bool hasValue, string value) Directory { get; set; }

                [Option(
                    "--notebook <notebook>",
                    Description = "Use this workspace-root notebook leaf name for this invocation; .mnote is optional")]
                public string Notebook { get; set; }

                [Option(
                    "--on-conflict <policy>",
                    Description = "Existing-name policy: fail (default), skip, or replace")]
                public string ConflictPolicy { get; set; }

                [Option(
                    "--dry-run",
                    Description = "validate and summarize without changing the notebook")]
                public bool DryRun { get; set; }

                protected Task<int> OnExecuteAsync(
                    CommandLineApplication app,
                    CancellationToken cancellationToken)
                {
                    if (!Directory.hasValue)
                    {
                        app.ShowHelp();
                        return Task.FromResult((int)CliExitCode.Validation);
                    }

                    return CommandHandlers.NotebookImport.ExecuteAsync(
                        Parent?.Parent?.Workspace,
                        Notebook,
                        Directory.value,
                        ConflictPolicy,
                        DryRun,
                        cancellationToken);
                }
            }

            [Command(
                "export",
                Description = "Export notebook pages as flat UTF-8 text files",
                ExtendedHelpText =
                    "Syntax: mn notebooks export <directory> [--notebook <notebook>] " +
                    "[--name-conflict fail|id-suffix]")]
            [HelpOption("--help")]
            class NotebooksExportCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Argument(
                    0,
                    Name = "directory",
                    Description = "new destination directory for flat lowercase .txt files")]
                public (bool hasValue, string value) Directory { get; set; }

                [Option(
                    "--notebook <notebook>",
                    Description = "Use this workspace-root notebook leaf name for this invocation; .mnote is optional")]
                public string Notebook { get; set; }

                [Option(
                    "--name-conflict <policy>",
                    Description = "Output-name policy: fail (default) or id-suffix")]
                public string NameConflictPolicy { get; set; }

                protected Task<int> OnExecuteAsync(
                    CommandLineApplication app,
                    CancellationToken cancellationToken)
                {
                    if (!Directory.hasValue)
                    {
                        app.ShowHelp();
                        return Task.FromResult((int)CliExitCode.Validation);
                    }

                    return CommandHandlers.NotebookExport.ExecuteAsync(
                        Parent?.Parent?.Workspace,
                        Notebook,
                        Directory.value,
                        NameConflictPolicy,
                        cancellationToken);
                }
            }

            [Command(
                "metadata",
                Description = "Show or update one notebook metadata field",
                ExtendedHelpText =
                    "Syntax: mn notebooks metadata [--notebook <notebook>] " +
                    "[--name <value> | --title <value> | --description <value> | " +
                    "--author <value> | --tag <value> | --read-only <true|false>]" +
                    "\nUse - as a text value to read it from redirected standard input.")]
            [HelpOption("--help")]
            class NotebooksMetadataCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Option(
                    "--notebook <notebook>",
                    Description = "Use this workspace-root notebook leaf name for this invocation; .mnote is optional")]
                public string Notebook { get; set; }

                [Option("--name <value>", Description = "Set the notebook display name; use - for standard input")]
                public string Name { get; set; }

                [Option("--title <value>", Description = "Set the notebook display title; use - for standard input")]
                public string Title { get; set; }

                [Option("--description <value>", Description = "Set the notebook description; use - for standard input")]
                public string Description { get; set; }

                [Option("--author <value>", Description = "Set the notebook author; use - for standard input")]
                public string Author { get; set; }

                [Option("--tag <value>", Description = "Set the notebook tag; use - for standard input")]
                public string Tag { get; set; }

                [Option("--read-only <value>", Description = "Set logical page-write protection: true or false")]
                public string ReadOnly { get; set; }

                protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
                {
                    return CommandHandlers.NotebookMetadata.ExecuteAsync(
                        Parent?.Parent?.Workspace,
                        Notebook,
                        Name,
                        Title,
                        Description,
                        Author,
                        Tag,
                        ReadOnly,
                        cancellationToken);
                }
            }
        }

        [Command("edit", Description = "Edit one page with an external editor")]
        [HelpOption("--help")]
        class EditCommand
        {
            public Program Parent { get; set; }

            [Argument(
                0,
                Name = "page-name",
                Description = "exact page name; omit when using --id")]
            public string PageName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

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

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.Edit.ExecuteAsync(
                    Parent?.Workspace,
                    Notebook,
                    PageName,
                    PageId,
                    Editor,
                    EditorArguments,
                    cancellationToken);
            }
        }

        [Command("new", Description = "Create one page with an external editor")]
        [HelpOption("--help")]
        class NewCommand
        {
            public Program Parent { get; set; }

            [Argument(0, Name = "page-name", Description = "new page name")]
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
            Description = "Inspect and configure user settings")]
        [Subcommand(typeof(ConfigPathCommand),
                    typeof(ConfigShowCommand),
                    typeof(ConfigValidateCommand),
                    typeof(ConfigEditorCommand))]
        [HelpOption("--help")]
        class ConfigCommand
        {
            protected Task<int> OnExecuteAsync(CommandLineApplication app)
            {
                app.ShowHelp();
                return Task.FromResult((int)CliExitCode.Success);
            }

            [Command("path", Description = "Show the user configuration path")]
            [HelpOption("--help")]
            class ConfigPathCommand
            {
                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.UserConfigPath.ExecuteAsync(cancellationToken);
                }
            }

            [Command("show", Description = "Show the user configuration")]
            [HelpOption("--help")]
            class ConfigShowCommand
            {
                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.UserConfigShow.ExecuteAsync(cancellationToken);
                }
            }

            [Command("validate", Description = "Validate the user configuration")]
            [HelpOption("--help")]
            class ConfigValidateCommand
            {
                protected Task<int> OnExecuteAsync(
                    CancellationToken cancellationToken)
                {
                    return CommandHandlers.UserConfigValidate.ExecuteAsync(
                        cancellationToken);
                }
            }

            [Command("editor", Description = "Inspect and configure the external editor")]
            [Subcommand(typeof(ConfigEditorSetupCommand),
                        typeof(ConfigEditorShowCommand),
                        typeof(ConfigEditorUnsetCommand))]
            [HelpOption("--help")]
            class ConfigEditorCommand
            {
                protected Task<int> OnExecuteAsync(CommandLineApplication app)
                {
                    app.ShowHelp();
                    return Task.FromResult((int)CliExitCode.Success);
                }

                [Command("setup", Description = "Configure the external editor interactively")]
                [HelpOption("--help")]
                class ConfigEditorSetupCommand
                {
                    protected Task<int> OnExecuteAsync(
                        CancellationToken cancellationToken)
                    {
                        return CommandHandlers.UserConfigEditorSetup.ExecuteAsync(
                            cancellationToken);
                    }
                }

                [Command("show", Description = "Show the external editor configuration")]
                [HelpOption("--help")]
                class ConfigEditorShowCommand
                {
                    protected Task<int> OnExecuteAsync(
                        CancellationToken cancellationToken)
                    {
                        return CommandHandlers.UserConfigEditorShow.ExecuteAsync(
                            cancellationToken);
                    }
                }

                [Command("unset", Description = "Leave the external editor unconfigured")]
                [HelpOption("--help")]
                class ConfigEditorUnsetCommand
                {
                    protected Task<int> OnExecuteAsync(
                        CancellationToken cancellationToken)
                    {
                        return CommandHandlers.UserConfigEditorUnset.ExecuteAsync(
                            cancellationToken);
                    }
                }
            }
        }

        [Command("ls", Description = "List pages in stable page-name order")]
        [HelpOption("--help")]
        class ListCommand
        {
            public Program Parent { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = "Use this workspace-root notebook leaf name for this invocation; .mnote is optional")]
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

            [Argument(
                0,
                Name = "page-name",
                Description = "exact page name; omit when using --id")]
            public string PageName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = "Use this workspace-root notebook leaf name for this invocation; .mnote is optional")]
            public string Notebook { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.Cat.ExecuteAsync(
                    Parent?.Workspace,
                    Notebook,
                    PageName,
                    PageId,
                    cancellationToken);
            }
        }

        [Command("rename", Description = "Rename one page without changing its Page ID")]
        [HelpOption("--help")]
        class RenameCommand
        {
            public Program Parent { get; set; }

            [Argument(
                0,
                Name = "page-name",
                Description = "exact current page name; with --id, the replacement name")]
            public string PageName { get; set; }

            [Argument(
                1,
                Name = "new-name",
                Description = "replacement page name; omit when using --id")]
            public string NewName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = "Use this workspace-root notebook leaf name for this invocation; .mnote is optional")]
            public string Notebook { get; set; }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                var selectingById = PageId != null;
                return CommandHandlers.RenamePage.ExecuteAsync(
                    Parent?.Workspace,
                    Notebook,
                    selectingById && NewName == null
                        ? null
                        : PageName,
                    PageId,
                    selectingById && NewName == null
                        ? PageName
                        : NewName,
                    cancellationToken);
            }
        }

        [Command("delete", Description = "Delete one page after confirmation")]
        [HelpOption("--help")]
        class DeleteCommand
        {
            public Program Parent { get; set; }

            [Argument(
                0,
                Name = "page-name",
                Description = "exact page name; omit when using --id")]
            public string PageName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = "Use this workspace-root notebook leaf name for this invocation; .mnote is optional")]
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
                    PageName,
                    PageId,
                    Force,
                    DryRun,
                    cancellationToken);
            }
        }

    }
}
