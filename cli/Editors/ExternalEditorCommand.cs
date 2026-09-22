using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote.Cli.Editors
{
    /// <summary>Describes an external editor executable and its argument list.</summary>
    public sealed class ExternalEditorCommand
    {
        /// <summary>Initializes an external editor command.</summary>
        /// <param name="executablePath">The executable path or PATH-resolved command name.</param>
        /// <param name="arguments">Arguments passed without shell interpretation.</param>
        public ExternalEditorCommand(
            string executablePath,
            IEnumerable<string> arguments = null)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException(
                    "An external editor executable is required.",
                    nameof(executablePath));
            }

            ExecutablePath = executablePath;
            Arguments = Array.AsReadOnly(
                (arguments ?? Enumerable.Empty<string>())
                    .Select(argument => argument ?? throw new ArgumentException(
                        "External editor arguments cannot contain null.",
                        nameof(arguments)))
                    .ToArray());
        }

        /// <summary>Gets the executable path or PATH-resolved command name.</summary>
        public string ExecutablePath { get; }

        /// <summary>Gets arguments passed without shell interpretation.</summary>
        public IReadOnlyList<string> Arguments { get; }
    }
}
