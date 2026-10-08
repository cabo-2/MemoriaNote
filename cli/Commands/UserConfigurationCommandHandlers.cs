using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Cli.Editors;
using MemoriaNote.Cli.UserConfig;

namespace MemoriaNote.Cli
{
    internal sealed class UserConfigPathCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly UserConfigurationWorkflow _workflow;
        readonly ICommandOutput _output;

        internal UserConfigPathCommandHandler(
            CliCommandExecutor executor,
            UserConfigurationWorkflow workflow,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                _output.WriteLine($"Path: {_workflow.Path}");
                _output.WriteLine($"Status: {(_workflow.PathExists ? "present" : "missing")}");
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class UserConfigShowCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly UserConfigurationWorkflow _workflow;
        readonly ICommandOutput _output;

        internal UserConfigShowCommandHandler(
            CliCommandExecutor executor,
            UserConfigurationWorkflow workflow,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                _output.WriteLine($"Path: {_workflow.Path}");
                UserConfigurationSnapshot snapshot;
                try
                {
                    snapshot = _workflow.Inspect();
                }
                catch (UnsupportedUserConfigurationVersionException)
                {
                    _output.WriteLine("Status: unsupported");
                    throw;
                }
                catch (UserConfigurationFormatException)
                {
                    _output.WriteLine("Status: invalid");
                    throw;
                }
                _output.WriteLine(
                    $"Status: {UserConfigurationOutputFormatter.Status(snapshot.Loaded)}");
                if (snapshot.Loaded.Status == UserConfigurationLoadStatus.Loaded)
                {
                    _output.WriteLine(
                        $"Format version: {snapshot.Loaded.Configuration.FormatVersion}");
                }
                UserConfigurationOutputFormatter.WriteEditor(_output, snapshot.Editor);
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class UserConfigValidateCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly UserConfigurationWorkflow _workflow;
        readonly ICommandOutput _output;

        internal UserConfigValidateCommandHandler(
            CliCommandExecutor executor,
            UserConfigurationWorkflow workflow,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                UserConfigurationLoadResult loaded;
                try
                {
                    loaded = _workflow.Validate();
                }
                catch (UnsupportedUserConfigurationVersionException)
                {
                    _output.WriteLine("Status: unsupported");
                    throw;
                }
                catch (UserConfigurationFormatException)
                {
                    _output.WriteLine("Status: invalid");
                    throw;
                }
                if (loaded.Status == UserConfigurationLoadStatus.Missing)
                {
                    _output.WriteLine("Status: missing");
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "User configuration does not exist.");
                }

                _output.WriteLine("Status: valid");
                _output.WriteLine(
                    $"Format version: {loaded.Configuration.FormatVersion}");
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class UserConfigEditorShowCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly UserConfigurationWorkflow _workflow;
        readonly ICommandOutput _output;

        internal UserConfigEditorShowCommandHandler(
            CliCommandExecutor executor,
            UserConfigurationWorkflow workflow,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                UserConfigurationOutputFormatter.WriteEditor(
                    _output,
                    _workflow.Inspect().Editor);
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class UserConfigEditorUnsetCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly UserConfigurationWorkflow _workflow;
        readonly ICommandOutput _output;

        internal UserConfigEditorUnsetCommandHandler(
            CliCommandExecutor executor,
            UserConfigurationWorkflow workflow,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                var snapshot = _workflow.Inspect();
                if (snapshot.Editor.Configuration?.Selection == UserEditorSelection.Unset)
                {
                    _output.WriteLine("Editor is already unset.");
                    return CliCommandResult.Success();
                }

                var saved = _workflow.SaveEditor(
                    UserEditorConfiguration.Unset,
                    snapshot.Loaded.Revision);
                if (saved.Status == UserConfigurationSaveStatus.Conflict)
                    return UserConfigurationCommandResult.Conflict();

                _output.WriteLine("Editor configuration was unset.");
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class UserConfigEditorSetupCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly UserConfigurationWorkflow _workflow;
        readonly ICommandInput _input;
        readonly ICommandOutput _output;

        internal UserConfigEditorSetupCommandHandler(
            CliCommandExecutor executor,
            UserConfigurationWorkflow workflow,
            ICommandInput input,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                if (!_input.IsInteractive)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Editor setup requires interactive input.");
                }

                var snapshot = _workflow.Inspect();
                _output.WriteLine("Current editor configuration:");
                UserConfigurationOutputFormatter.WriteEditor(
                    _output,
                    snapshot.Editor);
                WriteSelectionPrompt();
                var selection = _input.ReadLine();
                if (selection == null)
                    return InputEnded();
                if (selection.Trim() == "0")
                    return Canceled();

                UserEditorConfiguration editor;
                switch (selection.Trim())
                {
                    case "1":
                        editor = ReadEnvironmentConfiguration();
                        break;
                    case "2":
                        editor = ReadProgramConfiguration();
                        break;
                    case "3":
                        editor = UserEditorConfiguration.Unset;
                        break;
                    default:
                        return CliCommandResult.Failure(
                            CliErrorKind.Validation,
                            "Editor selection must be 0, 1, 2, or 3.");
                }

                if (editor == null)
                    return InputEnded();

                _output.WriteLine("Proposed editor configuration:");
                UserConfigurationOutputFormatter.WriteEditor(
                    _output,
                    _workflow.PreviewEditor(editor));
                _output.Write("Save this editor configuration? [y/N] ");
                var confirmation = _input.ReadLine();
                if (!IsYes(confirmation))
                    return Canceled();

                var saved = _workflow.SaveEditor(editor, snapshot.Loaded.Revision);
                if (saved.Status == UserConfigurationSaveStatus.Conflict)
                    return UserConfigurationCommandResult.Conflict();

                _output.WriteLine("Editor configuration was updated.");
                return CliCommandResult.Success();
            }, cancellationToken);
        }

        UserEditorConfiguration ReadEnvironmentConfiguration()
        {
            _output.Write("Environment variable [EDITOR]: ");
            var variable = _input.ReadLine();
            if (variable == null)
                return null;
            if (variable.Length == 0)
                variable = "EDITOR";

            var arguments = ReadArguments();
            if (arguments == null)
                return null;

            try
            {
                return UserEditorConfiguration.FromEnvironment(variable, arguments);
            }
            catch (ArgumentException exception)
            {
                throw new UserConfigurationSetupInputException(exception.Message, exception);
            }
        }

        UserEditorConfiguration ReadProgramConfiguration()
        {
            _output.Write("Editor executable: ");
            var executable = _input.ReadLine();
            if (executable == null)
                return null;

            var arguments = ReadArguments();
            if (arguments == null)
                return null;

            try
            {
                return UserEditorConfiguration.FromProgram(executable, arguments);
            }
            catch (ArgumentException exception)
            {
                throw new UserConfigurationSetupInputException(exception.Message, exception);
            }
        }

        string[] ReadArguments()
        {
            _output.Write("Number of editor arguments [0]: ");
            var countText = _input.ReadLine();
            if (countText == null)
                return null;
            if (countText.Length == 0)
                countText = "0";
            if (!int.TryParse(
                countText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var count) || count < 0)
            {
                throw new UserConfigurationSetupInputException(
                    "The editor argument count must be a non-negative integer.");
            }

            var arguments = new string[count];
            for (var index = 0; index < count; index++)
            {
                _output.Write($"Argument {index + 1}: ");
                arguments[index] = _input.ReadLine();
                if (arguments[index] == null)
                    return null;
            }
            return arguments;
        }

        void WriteSelectionPrompt()
        {
            _output.WriteLine("Select how MemoriaNote should start an editor:");
            _output.WriteLine("1. Use an environment variable");
            _output.WriteLine("2. Specify an editor program");
            _output.WriteLine("3. Leave the editor unconfigured");
            _output.WriteLine("0. Cancel");
            _output.Write("Selection: ");
        }

        CliCommandResult InputEnded()
        {
            return CliCommandResult.Failure(
                CliErrorKind.Validation,
                "Input ended before editor setup was complete.");
        }

        CliCommandResult Canceled()
        {
            _output.WriteLine("Operation was canceled.");
            return CliCommandResult.Success();
        }

        static bool IsYes(string value)
        {
            return string.Equals(value?.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class UserConfigurationSetupInputException : Exception
    {
        internal UserConfigurationSetupInputException(string message)
            : base(message)
        {
        }

        internal UserConfigurationSetupInputException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal static class UserConfigurationCommandResult
    {
        internal static CliCommandResult Conflict()
        {
            return CliCommandResult.Failure(
                CliErrorKind.Conflict,
                "User configuration changed before it could be saved. Run the command again.");
        }
    }

    internal static class UserConfigurationOutputFormatter
    {
        internal static string Status(UserConfigurationLoadResult loaded)
        {
            return loaded.Status switch
            {
                UserConfigurationLoadStatus.Missing => "missing",
                UserConfigurationLoadStatus.Loaded => "valid",
                _ => throw new ArgumentOutOfRangeException(nameof(loaded))
            };
        }

        internal static void WriteEditor(
            ICommandOutput output,
            UserEditorResolution resolution)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            if (resolution == null)
                throw new ArgumentNullException(nameof(resolution));

            var editor = resolution.Configuration;
            if (editor == null)
                WriteMissingEditor(output);
            else
                WritePersistedEditor(output, editor);
            output.WriteLine($"Editor source: {resolution.Source}");
            WriteEffectiveCommand(output, resolution.EffectiveCommand);
        }

        static void WriteMissingEditor(ICommandOutput output)
        {
            output.WriteLine("Editor selection: (not configured)");
            output.WriteLine("Editor arguments: []");
        }

        static void WritePersistedEditor(
            ICommandOutput output,
            UserEditorConfiguration editor)
        {
            output.WriteLine(
                $"Editor selection: {UserConfigurationContract.GetSelectionValue(editor.Selection)}");
            if (editor.Selection == UserEditorSelection.Environment)
                output.WriteLine($"Editor variable: {editor.Variable}");
            if (editor.Selection == UserEditorSelection.Program)
                output.WriteLine($"Editor executable: {editor.Executable}");
            output.WriteLine($"Editor arguments: {FormatArguments(editor.Arguments)}");
        }

        static void WriteEffectiveCommand(
            ICommandOutput output,
            ExternalEditorCommand command)
        {
            if (command == null)
            {
                output.WriteLine("Effective executable: (not configured)");
                output.WriteLine("Effective arguments: []");
                return;
            }

            output.WriteLine($"Effective executable: {command.ExecutablePath}");
            output.WriteLine($"Effective arguments: {FormatArguments(command.Arguments)}");
        }

        static string FormatArguments(System.Collections.Generic.IEnumerable<string> arguments)
        {
            return JsonSerializer.Serialize(arguments?.ToArray() ?? Array.Empty<string>());
        }
    }
}
