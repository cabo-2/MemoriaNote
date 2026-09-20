using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
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

        public void WritePageList(IReadOnlyList<PageSummary> pages, bool longFormat)
        {
            if (pages == null)
                throw new ArgumentNullException(nameof(pages));

            if (longFormat)
                WriteLongPageList(pages);
            else
                WriteNamePageList(pages);
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

        void WriteNamePageList(IReadOnlyList<PageSummary> pages)
        {
            foreach (var page in pages)
                WriteLine(EscapeCell(page.Name));
        }

        void WriteLongPageList(IReadOnlyList<PageSummary> pages)
        {
            if (pages.Count == 0)
                return;

            var rows = new List<string[]>
            {
                new[]
                {
                    "IDX",
                    "TYPE",
                    "CREATED-UTC",
                    "UPDATED-UTC",
                    "ERASED",
                    "TAGS",
                    "PAGE-ID",
                    "NAME"
                }
            };
            foreach (var page in pages)
            {
                rows.Add(new[]
                {
                    page.Index.ToString(CultureInfo.InvariantCulture),
                    EscapeCell(page.ContentType),
                    FormatUtc(page.CreateTime),
                    FormatUtc(page.UpdateTime),
                    page.IsErased ? "true" : "false",
                    FormatTags(page.Tags),
                    page.PageId.ToString(),
                    EscapeCell(page.Name)
                });
            }

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

        static string FormatUtc(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
            return utc.ToString("O", CultureInfo.InvariantCulture);
        }

        static string FormatTags(IReadOnlyDictionary<string, string> tags)
        {
            var ordered = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in tags)
                ordered.Add(tag.Key, tag.Value);
            return JsonSerializer.Serialize(ordered);
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
