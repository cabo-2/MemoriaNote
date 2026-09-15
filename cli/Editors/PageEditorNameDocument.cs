using System;
using System.IO;
using System.Text;

namespace MemoriaNote.Cli.Editors
{
    internal static class PageEditorNameDocument
    {
        internal static string Create(string name, EditorMode mode)
        {
            var buffer = new StringBuilder();
            buffer.AppendLine(name);
            buffer.AppendLine();
            buffer.AppendLine(mode switch
            {
                EditorMode.Rename => "#### Enter the name to be renamed ####",
                EditorMode.Delete => "#### Enter the name to be deleted ####",
                EditorMode.Create => "#### Enter a name to be created ####",
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            });
            return buffer.ToString();
        }

        internal static string ReadName(string document)
        {
            using var reader = new StringReader(document);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    break;
            }

            return line != null && line.Length > 0 && line[0] != '#'
                ? line
                : string.Empty;
        }
    }
}
