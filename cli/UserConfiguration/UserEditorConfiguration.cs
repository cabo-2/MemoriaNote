using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MemoriaNote.Cli.UserConfig
{
    internal enum UserEditorSelection
    {
        Environment,
        Program,
        Unset
    }

    internal sealed class UserEditorConfiguration
    {
        static readonly IReadOnlyList<string> NoArguments = Array.Empty<string>();

        UserEditorConfiguration(
            UserEditorSelection selection,
            string variable,
            string executable,
            IReadOnlyList<string> arguments)
        {
            Selection = selection;
            Variable = variable;
            Executable = executable;
            Arguments = arguments;
        }

        internal UserEditorSelection Selection { get; }

        internal string Variable { get; }

        internal string Executable { get; }

        internal IReadOnlyList<string> Arguments { get; }

        internal static UserEditorConfiguration FromEnvironment(
            string variable,
            IEnumerable<string> arguments = null)
        {
            ValidateRequiredValue(variable, nameof(variable));
            if (variable.Contains('=', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "An editor environment variable name cannot contain '='.",
                    nameof(variable));
            }

            return new UserEditorConfiguration(
                UserEditorSelection.Environment,
                variable,
                null,
                CopyArguments(arguments));
        }

        internal static UserEditorConfiguration FromProgram(
            string executable,
            IEnumerable<string> arguments = null)
        {
            ValidateRequiredValue(executable, nameof(executable));
            return new UserEditorConfiguration(
                UserEditorSelection.Program,
                null,
                executable,
                CopyArguments(arguments));
        }

        internal static UserEditorConfiguration Unset { get; } =
            new UserEditorConfiguration(
                UserEditorSelection.Unset,
                null,
                null,
                NoArguments);

        static IReadOnlyList<string> CopyArguments(IEnumerable<string> arguments)
        {
            if (arguments == null)
                return NoArguments;

            var copied = arguments.Select(argument =>
            {
                if (argument == null)
                {
                    throw new ArgumentException(
                        "Editor arguments cannot contain null.",
                        nameof(arguments));
                }
                if (argument.Contains('\0'))
                {
                    throw new ArgumentException(
                        "Editor arguments cannot contain NUL.",
                        nameof(arguments));
                }
                return argument;
            }).ToArray();
            return new ReadOnlyCollection<string>(copied);
        }

        static void ValidateRequiredValue(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "An editor configuration value cannot be empty or whitespace.",
                    parameterName);
            }
            if (value.Contains('\0'))
            {
                throw new ArgumentException(
                    "An editor configuration value cannot contain NUL.",
                    parameterName);
            }
        }
    }
}
