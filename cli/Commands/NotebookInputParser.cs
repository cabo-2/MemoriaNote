using System;
using System.IO;
using MemoriaNote.Domain;

namespace MemoriaNote.Cli
{
    internal static class NotebookInputParser
    {
        internal static NotebookFileName Parse(string input)
        {
            try
            {
                return NotebookFileName.FromInput(input);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(exception.Message, exception);
            }
        }
    }
}
