using System;
using System.Collections.Generic;
using System.IO;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.HelpText;

namespace MemoriaNote.Cli
{
    /// <summary>Generates help text for command syntaxes with alternative inputs.</summary>
    sealed class CliHelpTextGenerator : IHelpTextGenerator
    {
        static readonly IReadOnlyDictionary<string, string> UsageByCommand =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mn use"] = "Usage: mn use [options] (<notebook> | --root)",
                ["mn notebooks backup"] =
                    "Usage: mn notebooks backup [<archive>] [options]",
                ["mn notebooks restore"] =
                    "Usage: mn notebooks restore [<archive>] --target <notebook> [options]",
                ["mn edit"] =
                    "Usage: mn edit [options] (<page-name> | --id <uuid-or-prefix>)",
                ["mn cat"] =
                    "Usage: mn cat [options] (<page-name> | --id <uuid-or-prefix>)",
                ["mn rename"] =
                    "Usage: mn rename [options] <page-name> <new-name>" +
                    "\n       mn rename [options] --id <uuid-or-prefix> <new-name>",
                ["mn delete"] =
                    "Usage: mn delete [options] (<page-name> | --id <uuid-or-prefix>)"
            };

        public void Generate(
            CommandLineApplication application,
            TextWriter output)
        {
            using var buffer = new StringWriter();
            DefaultHelpTextGenerator.Singleton.Generate(application, buffer);

            var commandPath = GetCommandPath(application);
            var helpText = buffer.ToString();
            if (UsageByCommand.TryGetValue(commandPath, out var usage))
                helpText = ReplaceUsage(helpText, usage, buffer.NewLine);

            helpText = helpText.Replace(
                $"Run '{application.Name} [command] --help'",
                $"Run '{commandPath} [command] --help'",
                StringComparison.Ordinal);
            output.Write(helpText);
        }

        static string GetCommandPath(CommandLineApplication application)
        {
            var names = new Stack<string>();
            for (var current = application; current != null; current = current.Parent)
                names.Push(current.Name);

            return string.Join(" ", names);
        }

        static string ReplaceUsage(
            string helpText,
            string usage,
            string newLine)
        {
            const string prefix = "Usage: ";
            var start = helpText.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
                return helpText;

            var end = helpText.IndexOf('\n', start);
            if (end < 0)
                return string.Concat(helpText.AsSpan(0, start), usage);

            if (end > start && helpText[end - 1] == '\r')
                end--;

            return string.Concat(
                helpText.AsSpan(0, start),
                usage.Replace("\n", newLine, StringComparison.Ordinal),
                helpText.AsSpan(end));
        }
    }
}
