using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Conventions;

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
        const string NotebookArgumentDescription =
            "Notebook file name in the workspace root; .mnote is optional";
        const string NotebookOptionDescription =
            "Notebook file to use from the workspace root; .mnote is optional";

        static Lazy<CliComposition> _composition;

        public static async Task<int> Main(string[] args)
        {
            _composition = new Lazy<CliComposition>(CliComposition.CreateDefault);
            var originalOutputEncoding = Console.OutputEncoding;
            Console.OutputEncoding = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false);
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                var app = new CommandLineApplication<Program>(
                    new CliHelpTextGenerator(),
                    PhysicalConsole.Singleton,
                    Directory.GetCurrentDirectory());
                app.Conventions.UseDefaultConventions();
                using var parserOutput = new StringWriter();
                ConfigureParserOutput(app, parserOutput);
                ConfigureValidationErrors(app);
                try
                {
                    var exitCode = await app.ExecuteAsync(args, cancellation.Token);
                    Console.Out.Write(parserOutput.ToString());
                    return exitCode;
                }
                catch (CommandParsingException exception)
                {
                    exception.Command.Error.WriteLine(
                        $"Error: {CliMessage.AsSentence(exception.Message)}");
                    exception.Command.Error.Write(parserOutput.ToString());
                    return (int)CliExitCode.Validation;
                }
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
                if (_composition.IsValueCreated)
                    _composition.Value.Dispose();
                _composition = null;
                Console.OutputEncoding = originalOutputEncoding;
            }
        }

        static CliComposition Composition =>
            (_composition ??
                throw new InvalidOperationException("CLI composition is not available."))
            .Value;

        static CliCommandHandlers CommandHandlers => Composition.CommandHandlers;

        static void ConfigureValidationErrors(CommandLineApplication command)
        {
            command.ValidationErrorHandler = result =>
            {
                command.Error.WriteLine($"Error: {result.ErrorMessage}");
                return (int)CliExitCode.Validation;
            };

            foreach (var child in command.Commands)
                ConfigureValidationErrors(child);
        }

        static void ConfigureParserOutput(
            CommandLineApplication command,
            TextWriter output)
        {
            command.Out = output;
            foreach (var child in command.Commands)
                ConfigureParserOutput(child, output);
        }

        [Option(
            "--workspace <directory>",
            Description = "Use this directory as the workspace for this invocation")]
        public string Workspace { get; set; }

        protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
        {
            return CommandHandlers.Status.ExecuteAsync(
                Workspace,
                cancellationToken);
        }

        abstract class WorkspaceCommand
        {
            public Program Parent { get; set; }

            [Option(
                "--workspace <directory>",
                Description = "Use this directory as the workspace for this invocation",
                Inherited = true)]
            public string Workspace { get; set; }

            protected string WorkspacePath => Workspace ?? Parent?.Workspace;
        }

        [Command(
            "create",
            Description = "Create a live notebook without overwriting an existing file")]
        [HelpOption("--help")]
        class CreateCommand : WorkspaceCommand
        {
            [Argument(
                0,
                Name = "notebook",
                Description = NotebookArgumentDescription)]
            public (bool hasValue, string value) NotebookFile { get; set; }

            protected ValidationResult OnValidate()
            {
                return NotebookFile.hasValue
                    ? ValidationResult.Success
                    : new ValidationResult("A notebook is required.");
            }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.CreateNotebook.ExecuteAsync(
                    WorkspacePath,
                    NotebookFile.hasValue ? NotebookFile.value : null,
                    cancellationToken);
            }
        }

        [Command(
            "use",
            Description = "Select a notebook or return to the workspace root")]
        [HelpOption("--help")]
        class UseCommand : WorkspaceCommand
        {
            [Argument(
                0,
                Name = "notebook",
                Description = NotebookArgumentDescription)]
            public (bool hasValue, string value) Notebook { get; set; }

            [Option("--root", Description = "Select the virtual workspace root")]
            public bool Root { get; set; }

            protected ValidationResult OnValidate()
            {
                return Notebook.hasValue == Root
                    ? new ValidationResult(
                        "Specify exactly one of <notebook> or --root.")
                    : ValidationResult.Success;
            }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.UseNotebook.ExecuteAsync(
                    WorkspacePath,
                    Notebook.hasValue ? Notebook.value : null,
                    Root,
                    cancellationToken);
            }
        }

        [Command(
            "status",
            Description = "Show the workspace location and selected notebook state")]
        [HelpOption("--help")]
        class StatusCommand : WorkspaceCommand
        {
            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.Status.ExecuteAsync(
                    WorkspacePath,
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
        class NotebooksCommand : WorkspaceCommand
        {
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
                        Parent?.WorkspacePath,
                        Long,
                        cancellationToken);
                }
            }

            [Command(
                "backup",
                Description = "Create a validated archive v1 backup",
                ExtendedHelpText =
                    "Omit <archive> or use - to write the archive to redirected " +
                    "standard output.")]
            [HelpOption("--help")]
            class NotebooksBackupCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Argument(
                    0,
                    Name = "archive",
                    Description = "New archive file; omit or use - for standard output")]
                public (bool hasValue, string value) Archive { get; set; }

                [Option(
                    "--notebook <notebook>",
                    Description = NotebookOptionDescription)]
                public string Notebook { get; set; }

                protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
                {
                    return CommandHandlers.NotebookBackup.ExecuteAsync(
                        Parent?.WorkspacePath,
                        Notebook,
                        Archive.hasValue ? Archive.value : null,
                        cancellationToken);
                }
            }

            [Command(
                "restore",
                Description = "Restore a validated archive v1 to a new notebook",
                ExtendedHelpText =
                    "Omit <archive> or use - to read the archive from redirected " +
                    "standard input.")]
            [HelpOption("--help")]
            class NotebooksRestoreCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Argument(
                    0,
                    Name = "archive",
                    Description = "Archive file; omit or use - for standard input")]
                public (bool hasValue, string value) Archive { get; set; }

                [Option(
                    "--target <notebook>",
                    Description = "New notebook file name in the workspace root; .mnote is optional")]
                public string Target { get; set; }

                [Option(
                    "--dry-run",
                    Description = "Fully validate the restore without creating a notebook")]
                public bool DryRun { get; set; }

                protected ValidationResult OnValidate()
                {
                    return string.IsNullOrWhiteSpace(Target)
                        ? new ValidationResult("Specify --target <notebook>.")
                        : ValidationResult.Success;
                }

                protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
                {
                    return CommandHandlers.NotebookRestore.ExecuteAsync(
                        Parent?.WorkspacePath,
                        Target,
                        Archive.hasValue ? Archive.value : null,
                        DryRun,
                        cancellationToken);
                }
            }

            [Command(
                "import",
                Description = "Import flat strict-UTF-8 text files into a notebook")]
            [HelpOption("--help")]
            class NotebooksImportCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Argument(
                    0,
                    Name = "directory",
                    Description = "Existing directory containing flat lowercase .txt files")]
                public (bool hasValue, string value) Directory { get; set; }

                [Option(
                    "--notebook <notebook>",
                    Description = NotebookOptionDescription)]
                public string Notebook { get; set; }

                [Option(
                    "--on-conflict <policy>",
                    Description = "Existing-name policy: fail (default), skip, or replace")]
                public string ConflictPolicy { get; set; }

                [Option(
                    "--dry-run",
                    Description = "Validate and summarize without changing the notebook")]
                public bool DryRun { get; set; }

                protected ValidationResult OnValidate()
                {
                    if (!Directory.hasValue)
                        return new ValidationResult("Specify an import directory.");
                    if (ConflictPolicy != null &&
                        ConflictPolicy != "fail" &&
                        ConflictPolicy != "skip" &&
                        ConflictPolicy != "replace")
                    {
                        return new ValidationResult(
                            "The conflict policy must be fail, skip, or replace.");
                    }

                    return ValidationResult.Success;
                }

                protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
                {
                    return CommandHandlers.NotebookImport.ExecuteAsync(
                        Parent?.WorkspacePath,
                        Notebook,
                        Directory.value,
                        ConflictPolicy,
                        DryRun,
                        cancellationToken);
                }
            }

            [Command(
                "export",
                Description = "Export notebook pages as flat UTF-8 text files")]
            [HelpOption("--help")]
            class NotebooksExportCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Argument(
                    0,
                    Name = "directory",
                    Description = "New destination directory for flat lowercase .txt files")]
                public (bool hasValue, string value) Directory { get; set; }

                [Option(
                    "--notebook <notebook>",
                    Description = NotebookOptionDescription)]
                public string Notebook { get; set; }

                [Option(
                    "--name-conflict <policy>",
                    Description = "Output-name policy: fail (default) or id-suffix")]
                public string NameConflictPolicy { get; set; }

                protected ValidationResult OnValidate()
                {
                    if (!Directory.hasValue)
                        return new ValidationResult("Specify an export directory.");
                    if (NameConflictPolicy != null &&
                        NameConflictPolicy != "fail" &&
                        NameConflictPolicy != "id-suffix")
                    {
                        return new ValidationResult(
                            "The name conflict policy must be fail or id-suffix.");
                    }

                    return ValidationResult.Success;
                }

                protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
                {
                    return CommandHandlers.NotebookExport.ExecuteAsync(
                        Parent?.WorkspacePath,
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
                    "Specify at most one metadata update option. Use - as a text " +
                    "value to read it from redirected standard input.")]
            [HelpOption("--help")]
            class NotebooksMetadataCommand
            {
                public NotebooksCommand Parent { get; set; }

                [Option(
                    "--notebook <notebook>",
                    Description = NotebookOptionDescription)]
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

                protected ValidationResult OnValidate()
                {
                    var updateCount = 0;
                    if (Name != null) updateCount++;
                    if (Title != null) updateCount++;
                    if (Description != null) updateCount++;
                    if (Author != null) updateCount++;
                    if (Tag != null) updateCount++;
                    if (ReadOnly != null) updateCount++;

                    if (updateCount > 1)
                    {
                        return new ValidationResult(
                            "Specify only one metadata update option at a time.");
                    }
                    if (ReadOnly != null &&
                        ReadOnly != "true" &&
                        ReadOnly != "false")
                    {
                        return new ValidationResult(
                            "Read-only must be true or false.");
                    }

                    return ValidationResult.Success;
                }

                protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
                {
                    return CommandHandlers.NotebookMetadata.ExecuteAsync(
                        Parent?.WorkspacePath,
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
        class EditCommand : WorkspaceCommand
        {
            [Argument(
                0,
                Name = "page-name",
                Description = "Exact page name; omit when using --id")]
            public string PageName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = NotebookOptionDescription)]
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

            protected ValidationResult OnValidate()
            {
                return ValidatePageTarget(PageName, PageId);
            }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.Edit.ExecuteAsync(
                    WorkspacePath,
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
        class NewCommand : WorkspaceCommand
        {
            [Argument(0, Name = "page-name", Description = "New page name")]
            public (bool hasValue, string value) Name { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = NotebookOptionDescription)]
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

            protected ValidationResult OnValidate()
            {
                return Name.hasValue
                    ? ValidationResult.Success
                    : new ValidationResult("A page name is required.");
            }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.CreatePage.ExecuteAsync(
                    WorkspacePath,
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
        class ListCommand : WorkspaceCommand
        {
            [Option(
                "--notebook <notebook>",
                Description = NotebookOptionDescription)]
            public string Notebook { get; set; }

            [Option("--limit <count>", Description = "Return at most this many pages; must be positive")]
            public int? Limit { get; set; }

            [Option("-l|--long", Description = "Show all page metadata columns")]
            public bool Long { get; set; }

            protected ValidationResult OnValidate()
            {
                return Limit <= 0
                    ? new ValidationResult("Limit must be greater than zero.")
                    : ValidationResult.Success;
            }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.List.ExecuteAsync(
                    WorkspacePath,
                    Notebook,
                    Limit,
                    Long,
                    cancellationToken);
            }
        }

        [Command("cat", Description = "Write one page body to standard output")]
        [HelpOption("--help")]
        class CatCommand : WorkspaceCommand
        {
            [Argument(
                0,
                Name = "page-name",
                Description = "Exact page name; omit when using --id")]
            public string PageName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = NotebookOptionDescription)]
            public string Notebook { get; set; }

            protected ValidationResult OnValidate()
            {
                return ValidatePageTarget(PageName, PageId);
            }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.Cat.ExecuteAsync(
                    WorkspacePath,
                    Notebook,
                    PageName,
                    PageId,
                    cancellationToken);
            }
        }

        [Command("rename", Description = "Rename one page without changing its Page ID")]
        [HelpOption("--help")]
        class RenameCommand : WorkspaceCommand
        {
            [Argument(
                0,
                Name = "page-name",
                Description = "Exact current page name; with --id, the replacement name")]
            public string PageName { get; set; }

            [Argument(
                1,
                Name = "new-name",
                Description = "Replacement page name; omit when using --id")]
            public string NewName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = NotebookOptionDescription)]
            public string Notebook { get; set; }

            protected ValidationResult OnValidate()
            {
                if (PageId == null && PageName != null && NewName != null)
                    return ValidationResult.Success;
                if (PageId != null && PageName != null && NewName == null)
                    return ValidationResult.Success;

                return new ValidationResult(
                    "Specify either <page-name> <new-name> or " +
                    "--id <uuid-or-prefix> <new-name>.");
            }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                var selectingById = PageId != null;
                return CommandHandlers.RenamePage.ExecuteAsync(
                    WorkspacePath,
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
        class DeleteCommand : WorkspaceCommand
        {
            [Argument(
                0,
                Name = "page-name",
                Description = "Exact page name; omit when using --id")]
            public string PageName { get; set; }

            [Option(
                "--id <uuid-or-prefix>",
                Description = "Select by a complete Page ID or a hexadecimal prefix")]
            public string PageId { get; set; }

            [Option(
                "--notebook <notebook>",
                Description = NotebookOptionDescription)]
            public string Notebook { get; set; }

            [Option("--force", Description = "Delete without an interactive confirmation")]
            public bool Force { get; set; }

            [Option("--dry-run", Description = "Validate and show the target without deleting")]
            public bool DryRun { get; set; }

            protected ValidationResult OnValidate()
            {
                return ValidatePageTarget(PageName, PageId);
            }

            protected Task<int> OnExecuteAsync(CancellationToken cancellationToken)
            {
                return CommandHandlers.DeletePage.ExecuteAsync(
                    WorkspacePath,
                    Notebook,
                    PageName,
                    PageId,
                    Force,
                    DryRun,
                    cancellationToken);
            }
        }

        static ValidationResult ValidatePageTarget(
            string pageName,
            string pageId)
        {
            return (pageName == null) == (pageId == null)
                ? new ValidationResult(
                    "Specify exactly one of <page-name> or --id <uuid-or-prefix>.")
                : ValidationResult.Success;
        }

    }
}
