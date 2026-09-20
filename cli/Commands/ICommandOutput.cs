using System.Collections.Generic;
using MemoriaNote.Domain;
using MemoriaNote.Models;

namespace MemoriaNote.Cli
{
    internal interface ICommandOutput
    {
        void Write(string value);

        void WriteLine(string value);

        void WriteErrorLine(string value);

        void WritePageList(IReadOnlyList<PageSummary> pages, bool longFormat);

        void WriteNotebookList(IEnumerable<Notebook> notebooks, Notebook selectedNotebook);

        void WriteNotebookCompletion(IEnumerable<Notebook> notebooks);
    }
}
