using System.Collections.Generic;
using System.Linq;
using MemoriaNote.Cli.Editors;

namespace MemoriaNote.Cli
{
    internal static class EditorCommandOptionParser
    {
        internal static bool TryCreate(
            string editorPath,
            IEnumerable<string> editorArguments,
            out ExternalEditorCommand command,
            out string error)
        {
            var arguments = (editorArguments ?? Enumerable.Empty<string>()).ToArray();
            if (editorPath == null)
            {
                command = null;
                error = arguments.Length == 0
                    ? null
                    : "--editor-arg requires --editor.";
                return error == null;
            }

            if (string.IsNullOrWhiteSpace(editorPath))
            {
                command = null;
                error = "The external editor executable cannot be empty or whitespace.";
                return false;
            }

            command = new ExternalEditorCommand(editorPath, arguments);
            error = null;
            return true;
        }
    }
}
