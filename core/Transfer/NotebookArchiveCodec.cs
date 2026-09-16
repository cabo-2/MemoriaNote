using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using MemoriaNote.Models;

namespace MemoriaNote.Transfer
{
    internal sealed class NotebookArchiveCodec
    {
        internal const string MetadataEntryName = "metadata.json";

        internal async Task WriteAsync(
            Stream output,
            IReadOnlyCollection<NoteKeyValue> metadata,
            IReadOnlyList<Page> pages,
            CancellationToken token)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));
            if (pages == null)
                throw new ArgumentNullException(nameof(pages));

            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var digits = pages.Count.ToString().Length;
            foreach (var page in pages)
            {
                token.ThrowIfCancellationRequested();
                var entryName = page.Rowid.ToString().PadLeft(digits, '0') + ".json";
                await WriteEntryAsync(archive.CreateEntry(entryName), page, token)
                    .ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            await WriteEntryAsync(
                    archive.CreateEntry(MetadataEntryName),
                    metadata,
                    token)
                .ConfigureAwait(false);
        }

        internal async Task<NotebookArchive> ReadAsync(
            Stream input,
            CancellationToken token)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            try
            {
                using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
                var metadataEntry = archive.GetEntry(MetadataEntryName);
                if (metadataEntry == null)
                    throw new InvalidDataException("The backup archive has no metadata entry.");

                var metadata = await ReadEntryAsync<List<NoteKeyValue>>(metadataEntry, token)
                    .ConfigureAwait(false);
                if (metadata == null)
                    throw new InvalidDataException("The backup metadata is empty.");

                var pages = new List<Page>();
                foreach (var entry in archive.Entries.Where(
                    entry => entry.FullName != MetadataEntryName))
                {
                    token.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        throw new InvalidDataException(
                            $"The backup entry '{entry.FullName}' is not a page document.");
                    }

                    var page = await ReadEntryAsync<Page>(entry, token).ConfigureAwait(false);
                    if (page == null)
                        throw new InvalidDataException($"The backup page '{entry.FullName}' is empty.");
                    pages.Add(page);
                }

                return new NotebookArchive(metadata, pages);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The backup archive contains invalid JSON.", exception);
            }
        }

        static async Task WriteEntryAsync<T>(
            ZipArchiveEntry entry,
            T value,
            CancellationToken token)
        {
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            var json = JsonConvert.SerializeObject(value, Formatting.Indented);
            token.ThrowIfCancellationRequested();
            await writer.WriteAsync(json).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }

        static async Task<T> ReadEntryAsync<T>(
            ZipArchiveEntry entry,
            CancellationToken token)
        {
            await using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            token.ThrowIfCancellationRequested();
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return JsonConvert.DeserializeObject<T>(json);
        }
    }

    internal sealed class NotebookArchive
    {
        internal NotebookArchive(
            IReadOnlyList<NoteKeyValue> metadata,
            IReadOnlyList<Page> pages)
        {
            Metadata = metadata;
            Pages = pages;
        }

        internal IReadOnlyList<NoteKeyValue> Metadata { get; }

        internal IReadOnlyList<Page> Pages { get; }
    }
}
