using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MemoriaNote.Domain;
using MemoriaNote.Models;

namespace MemoriaNote.Cli
{
    internal sealed class ConsoleCommandOutput : ICommandOutput
    {
        readonly TextWriter _standardOutput;
        readonly TextWriter _standardError;

        internal ConsoleCommandOutput(TextWriter standardOutput, TextWriter standardError)
        {
            _standardOutput = standardOutput ??
                throw new ArgumentNullException(nameof(standardOutput));
            _standardError = standardError ??
                throw new ArgumentNullException(nameof(standardError));
        }

        public void Write(string value)
        {
            _standardOutput.Write(value);
        }

        public void WriteLine(string value)
        {
            _standardOutput.WriteLine(value);
        }

        public void WriteErrorLine(string value)
        {
            _standardError.WriteLine(value);
        }

        public void WritePageList(IReadOnlyList<PageSummary> pages, int totalCount)
        {
            if (pages == null)
                throw new ArgumentNullException(nameof(pages));
            if (totalCount < 0)
                throw new ArgumentException(nameof(totalCount));

            var indexWidth = GetIndexWidth(pages.Count);
            var nameWidth = GetMaxNameLength(pages);
            WritePageBorder(indexWidth, nameWidth);

            for (var index = 0; index < pages.Count; index++)
            {
                var page = pages[index];
                var buffer = new StringBuilder();
                buffer.Append("|");
                buffer.Append((index + 1).ToString().PadLeft(indexWidth, '0'));
                buffer.Append(" | ");
                buffer.Append(page.PageId.Value.ToHashId());
                buffer.Append(" | ");
                var name = page.Name;
                buffer.Append(name.Substring(0, Math.Min(name.Length, nameWidth)));
                buffer.Append(' ', nameWidth - Math.Min(name.Length, nameWidth));
                buffer.Append(" |");
                WriteLine(buffer.ToString());
            }

            WritePageBorder(indexWidth, nameWidth);
            if (pages.Count < totalCount)
                WriteLine("Number of text messages exceeds 1000");

            WriteLine("Total count: " + totalCount.ToString());
        }

        public void WritePageCompletion(
            IReadOnlyList<PageSummary> pages,
            int totalCount)
        {
            if (pages == null)
                throw new ArgumentNullException(nameof(pages));
            if (totalCount < 0)
                throw new ArgumentException(nameof(totalCount));

            foreach (var name in pages
                .Select(page => GetFirstWord(page.Name).ToLower())
                .OrderBy(name => name)
                .Distinct())
            {
                WriteLine(name);
            }
        }

        public void WriteNotebookList(
            IEnumerable<Notebook> notebooks,
            Notebook selectedNotebook)
        {
            if (notebooks == null)
                throw new ArgumentNullException(nameof(notebooks));

            foreach (var notebook in notebooks)
            {
                var mark = notebook == selectedNotebook ? "*" : " ";
                WriteLine($"{mark} {notebook}");
            }
        }

        public void WriteNotebookCompletion(IEnumerable<Notebook> notebooks)
        {
            if (notebooks == null)
                throw new ArgumentNullException(nameof(notebooks));

            foreach (var notebook in notebooks)
                WriteLine(notebook.Metadata.Name);
        }

        static int GetIndexWidth(int viewCount)
        {
            var indexWidth = 0;
            while (viewCount > 0)
            {
                viewCount /= 10;
                indexWidth++;
            }
            return indexWidth;
        }

        static int GetMaxNameLength(IReadOnlyList<PageSummary> pages)
        {
            var textWidth = 0;
            foreach (var page in pages)
            {
                if (page.Name.Length > textWidth)
                    textWidth = page.Name.Length;
            }
            return Math.Min(textWidth, 64);
        }

        void WritePageBorder(int indexWidth, int textWidth)
        {
            var buffer = new StringBuilder();
            buffer.Append("+");
            buffer.Append('-', indexWidth);
            buffer.Append("-+-");
            buffer.Append('-', 7);
            buffer.Append("-+-");
            buffer.Append('-', textWidth);
            buffer.Append("-+");
            WriteLine(buffer.ToString());
        }

        static string GetFirstWord(string name)
        {
            return name.Split(' ').FirstOrDefault();
        }
    }
}
