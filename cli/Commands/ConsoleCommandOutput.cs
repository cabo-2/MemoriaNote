using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MemoriaNote.Application;
using MemoriaNote.Domain;

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

        public void WriteError(string value)
        {
            _standardError.Write(value);
        }

        public void WriteErrorLine(string value)
        {
            _standardError.WriteLine(value);
        }

        public void WritePageList(
            IReadOnlyList<PageListItem> pages,
            bool longFormat,
            bool includeHeader = true)
        {
            if (pages == null)
                throw new ArgumentNullException(nameof(pages));

            if (longFormat)
                WriteLongPageList(pages, includeHeader);
            else
                WriteNamePageList(pages);
        }

        public void WriteBodySearchHits(IReadOnlyList<BodySearchCommandHit> hits)
        {
            if (hits == null)
                throw new ArgumentNullException(nameof(hits));

            foreach (var hit in hits)
            {
                WriteLine($"{hit.PageId} {EscapeCell(hit.Name)}");
                if (hit.Snippet != null)
                    WriteLine($"  {FormatSnippet(hit.Snippet)}");
            }
        }

        public void WriteWorkspaceNotebookList(
            IReadOnlyList<WorkspaceNotebookListEntry> entries,
            bool longFormat)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            if (longFormat)
                WriteLongWorkspaceNotebookList(entries);
            else
                WriteShortWorkspaceNotebookList(entries);
        }

        void WriteNamePageList(IReadOnlyList<PageListItem> pages)
        {
            foreach (var page in pages)
                WriteLine(EscapeCell(page.Name));
        }

        void WriteShortWorkspaceNotebookList(
            IReadOnlyList<WorkspaceNotebookListEntry> entries)
        {
            foreach (var entry in entries)
            {
                var current = entry.IsCurrent ? "*" : " ";
                var status = entry.Status == WorkspaceNotebookStatusKind.Ready
                    ? string.Empty
                    : $" [{FormatNotebookStatus(entry.Status)}]";
                WriteLine($"{current} {EscapeCell(entry.FileName)}{status}");
            }
        }

        void WriteLongWorkspaceNotebookList(
            IReadOnlyList<WorkspaceNotebookListEntry> entries)
        {
            if (entries.Count == 0)
                return;

            var rows = new List<string[]>
            {
                new[] { "CURRENT", "STATUS", "TYPE", "FORMAT", "NOTEBOOK" }
            };
            foreach (var entry in entries)
            {
                rows.Add(new[]
                {
                    entry.IsCurrent ? "*" : string.Empty,
                    FormatNotebookStatus(entry.Status),
                    FormatNotebookEntryKind(entry.EntryKind),
                    entry.FormatVersion ?? "-",
                    EscapeCell(entry.FileName)
                });
            }

            WriteRows(rows);
        }

        void WriteLongPageList(
            IReadOnlyList<PageListItem> pages,
            bool includeHeader)
        {
            if (pages.Count == 0)
                return;

            var header = new[]
            {
                "IDX",
                "TYPE",
                "CREATED-UTC",
                "UPDATED-UTC",
                "ERASED",
                "TAGS",
                "PAGE-ID",
                "NAME"
            };
            var widths = new[] { 3, 4, 28, 28, 6, 4, 36 };
            if (includeHeader)
                WriteRow(header, widths);

            foreach (var page in pages)
            {
                WriteRow(new[]
                {
                    "1",
                    "T",
                    FormatUtc(page.CreateTimeUtc.UtcDateTime),
                    FormatUtc(page.UpdateTimeUtc.UtcDateTime),
                    "false",
                    "{}",
                    page.PageId.ToString(),
                    EscapeCell(page.Name)
                }, widths);
            }
        }

        void WriteRow(string[] row, int[] widths)
        {
            var buffer = new StringBuilder();
            for (var column = 0; column < widths.Length; column++)
            {
                buffer.Append(row[column].PadRight(widths[column]));
                buffer.Append(' ');
            }
            buffer.Append(row[^1]);
            WriteLine(buffer.ToString());
        }

        void WriteRows(IReadOnlyList<string[]> rows)
        {
            var widths = new int[rows[0].Length - 1];
            foreach (var row in rows)
            {
                for (var column = 0; column < widths.Length; column++)
                    widths[column] = Math.Max(widths[column], row[column].Length);
            }

            foreach (var row in rows)
            {
                var buffer = new StringBuilder();
                for (var column = 0; column < widths.Length; column++)
                {
                    buffer.Append(row[column].PadRight(widths[column]));
                    buffer.Append(' ');
                }
                buffer.Append(row[^1]);
                WriteLine(buffer.ToString());
            }
        }

        static string FormatNotebookStatus(WorkspaceNotebookStatusKind status)
        {
            return status switch
            {
                WorkspaceNotebookStatusKind.Ready => "ready",
                WorkspaceNotebookStatusKind.ReadOnly => "read-only",
                WorkspaceNotebookStatusKind.Missing => "missing",
                WorkspaceNotebookStatusKind.Invalid => "invalid",
                WorkspaceNotebookStatusKind.Unsupported => "unsupported",
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
        }

        static string FormatNotebookEntryKind(WorkspaceNotebookEntryKind? entryKind)
        {
            return entryKind switch
            {
                WorkspaceNotebookEntryKind.File => "file",
                WorkspaceNotebookEntryKind.SymbolicLink => "symlink",
                null => "-",
                _ => throw new ArgumentOutOfRangeException(nameof(entryKind))
            };
        }

        static string FormatUtc(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
            return utc.ToString("O", CultureInfo.InvariantCulture);
        }

        static string FormatSnippet(BodySearchCommandSnippet snippet)
        {
            var buffer = new StringBuilder(snippet.Text.Length +
                (snippet.Matches.Count * 4));
            var position = 0;
            foreach (var match in snippet.Matches)
            {
                buffer.Append(snippet.Text, position, match.Start - position);
                buffer.Append("[[");
                buffer.Append(snippet.Text, match.Start, match.Length);
                buffer.Append("]]");
                position = match.Start + match.Length;
            }
            buffer.Append(snippet.Text, position, snippet.Text.Length - position);
            return buffer.ToString();
        }

        static string EscapeCell(string value)
        {
            if (value == null)
                return string.Empty;

            var buffer = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\':
                        buffer.Append("\\\\");
                        break;
                    case '\r':
                        buffer.Append("\\r");
                        break;
                    case '\n':
                        buffer.Append("\\n");
                        break;
                    case '\t':
                        buffer.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(character))
                        {
                            buffer.Append("\\u");
                            buffer.Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            buffer.Append(character);
                        }
                        break;
                }
            }
            return buffer.ToString();
        }
    }
}
