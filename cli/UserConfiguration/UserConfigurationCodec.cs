using System;
using System.Collections.Generic;
using System.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace MemoriaNote.Cli.UserConfig
{
    internal static class UserConfigurationCodec
    {
        const string FormatVersionKey = "format_version";
        const string EditorKey = "editor";
        const string SelectionKey = "selection";
        const string VariableKey = "variable";
        const string ExecutableKey = "executable";
        const string ArgumentsKey = "arguments";

        internal static UserConfiguration Deserialize(string content)
        {
            TomlTable root;
            try
            {
                root = TomlSerializer.Deserialize<TomlTable>(content) ??
                    throw new UserConfigurationFormatException(
                        "The user configuration did not contain a TOML document.");
            }
            catch (TomlException exception)
            {
                throw new UserConfigurationFormatException(
                    "The user configuration TOML is invalid.",
                    exception);
            }

            var formatVersion = RequiredInteger(root, FormatVersionKey, "user configuration");
            if (formatVersion > UserConfigurationContract.CurrentFormatVersion)
                throw new UnsupportedUserConfigurationVersionException(formatVersion);
            if (formatVersion != UserConfigurationContract.CurrentFormatVersion)
            {
                throw new UserConfigurationFormatException(
                    $"The user configuration format version '{formatVersion}' is invalid.");
            }

            var editor = RequiredTable(root, EditorKey, "user configuration");
            var selection = RequiredString(editor, SelectionKey, "editor");
            try
            {
                return new UserConfiguration(selection switch
                {
                    UserConfigurationContract.EnvironmentSelection =>
                        ReadEnvironment(editor),
                    UserConfigurationContract.ProgramSelection =>
                        ReadProgram(editor),
                    UserConfigurationContract.UnsetSelection =>
                        ReadUnset(editor),
                    _ => throw new UserConfigurationFormatException(
                        $"The user configuration editor selection '{selection}' is invalid.")
                });
            }
            catch (ArgumentException exception)
            {
                throw new UserConfigurationFormatException(
                    $"The user configuration editor selection '{selection}' is invalid: " +
                    exception.Message,
                    exception);
            }
        }

        internal static string Serialize(UserConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var editor = new TomlTable
            {
                [SelectionKey] = UserConfigurationContract.GetSelectionValue(
                    configuration.Editor.Selection)
            };
            switch (configuration.Editor.Selection)
            {
                case UserEditorSelection.Environment:
                    editor[VariableKey] = configuration.Editor.Variable;
                    editor[ArgumentsKey] = ToTomlArray(configuration.Editor.Arguments);
                    break;
                case UserEditorSelection.Program:
                    editor[ExecutableKey] = configuration.Editor.Executable;
                    editor[ArgumentsKey] = ToTomlArray(configuration.Editor.Arguments);
                    break;
                case UserEditorSelection.Unset:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(configuration),
                        "The user configuration contains an unknown editor selection.");
            }

            var root = new TomlTable
            {
                [FormatVersionKey] = configuration.FormatVersion,
                [EditorKey] = editor
            };
            var serialized = TomlSerializer.Serialize(root)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\n[editor]\n", "\n\n[editor]\n", StringComparison.Ordinal)
                .TrimEnd('\n');
            return serialized + "\n";
        }

        static UserEditorConfiguration ReadEnvironment(TomlTable editor)
        {
            RejectField(editor, ExecutableKey, UserConfigurationContract.EnvironmentSelection);
            return UserEditorConfiguration.FromEnvironment(
                RequiredString(editor, VariableKey, "editor"),
                ReadArguments(editor));
        }

        static UserEditorConfiguration ReadProgram(TomlTable editor)
        {
            RejectField(editor, VariableKey, UserConfigurationContract.ProgramSelection);
            return UserEditorConfiguration.FromProgram(
                RequiredString(editor, ExecutableKey, "editor"),
                ReadArguments(editor));
        }

        static UserEditorConfiguration ReadUnset(TomlTable editor)
        {
            RejectField(editor, VariableKey, UserConfigurationContract.UnsetSelection);
            RejectField(editor, ExecutableKey, UserConfigurationContract.UnsetSelection);
            RejectField(editor, ArgumentsKey, UserConfigurationContract.UnsetSelection);
            return UserEditorConfiguration.Unset;
        }

        static IReadOnlyList<string> ReadArguments(TomlTable editor)
        {
            if (!editor.TryGetValue(ArgumentsKey, out var value))
                return Array.Empty<string>();
            if (value is not TomlArray array)
            {
                throw new UserConfigurationFormatException(
                    $"The user configuration editor field '{ArgumentsKey}' must be an array.");
            }

            var arguments = new List<string>(array.Count);
            foreach (var item in array)
            {
                if (item is not string argument)
                {
                    throw new UserConfigurationFormatException(
                        $"The user configuration editor field '{ArgumentsKey}' " +
                        "must contain only strings.");
                }
                arguments.Add(argument);
            }
            return arguments;
        }

        static long RequiredInteger(TomlTable table, string key, string location)
        {
            if (!table.TryGetValue(key, out var value) || value is not long integer)
            {
                throw new UserConfigurationFormatException(
                    $"The {location} requires an integer '{key}'.");
            }
            return integer;
        }

        static string RequiredString(TomlTable table, string key, string location)
        {
            if (!table.TryGetValue(key, out var value) || value is not string text)
            {
                throw new UserConfigurationFormatException(
                    $"The user configuration {location} requires a string '{key}'.");
            }
            return text;
        }

        static TomlTable RequiredTable(TomlTable table, string key, string location)
        {
            if (!table.TryGetValue(key, out var value) || value is not TomlTable nested)
            {
                throw new UserConfigurationFormatException(
                    $"The {location} requires a table '[{key}]'.");
            }
            return nested;
        }

        static void RejectField(TomlTable table, string key, string selection)
        {
            if (table.ContainsKey(key))
            {
                throw new UserConfigurationFormatException(
                    $"The user configuration editor field '{key}' is not allowed " +
                    $"for selection '{selection}'.");
            }
        }

        static TomlArray ToTomlArray(IEnumerable<string> arguments)
        {
            var array = new TomlArray();
            foreach (var argument in arguments ?? Enumerable.Empty<string>())
                array.Add(argument);
            return array;
        }
    }
}
