using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Archive
{
    /// <summary>Writes the canonical MemoriaNote archive v1 representation.</summary>
    public sealed class ArchiveV1Writer
    {
        static readonly JsonWriterOptions CompactWriterOptions = new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false
        };

        static readonly JsonWriterOptions IndentedWriterOptions = new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = true,
            IndentCharacter = ' ',
            IndentSize = 2,
            NewLine = "\n"
        };

        /// <summary>Writes one archive to the supplied stream without closing it.</summary>
        /// <param name="output">The empty writable output stream.</param>
        /// <param name="request">The archive records and creation information.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async Task WriteAsync(
            Stream output,
            ArchiveV1WriteRequest request,
            CancellationToken cancellationToken)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!output.CanWrite)
                throw new ArgumentException("The output stream must be writable.", nameof(output));

            cancellationToken.ThrowIfCancellationRequested();
            var metadata = new List<ArchiveV1MetadataRow>();
            foreach (var row in request.Metadata)
            {
                cancellationToken.ThrowIfCancellationRequested();
                metadata.Add(row);
                if (metadata.Count > ArchiveV1Format.MaximumMetadataRows)
                    break;
            }
            var initialIssues = ValidateRequest(request, metadata);
            if (initialIssues.Count > 0)
                throw CreateValidationException(initialIssues);

            var orderedMetadata = metadata
                .OrderBy(row => row.Key, StringComparer.Ordinal)
                .ToList();
            var metadataBytes = SerializeMetadata(orderedMetadata);
            if (metadataBytes.LongLength > ArchiveV1Format.MaximumMetadataLength)
            {
                throw CreateValidationException(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.EntryTooLarge,
                    ArchiveV1Format.MetadataEntryName,
                    ArchiveV1Format.MaximumMetadataLength.ToString(CultureInfo.InvariantCulture),
                    metadataBytes.LongLength.ToString(CultureInfo.InvariantCulture)));
            }

            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var metadataDescriptor = await WriteBytesEntryAsync(
                    archive,
                    ArchiveV1Format.MetadataEntryName,
                    metadataBytes,
                    orderedMetadata.Count,
                    cancellationToken)
                .ConfigureAwait(false);

            var pagesDescriptor = await WritePagesAsync(
                    archive,
                    request.Pages,
                    cancellationToken)
                .ConfigureAwait(false);

            var manifest = new ArchiveV1Manifest(
                request.SourceNotebookFormatVersion,
                request.Creator.Application,
                request.Creator.Version,
                ArchiveV1Format.FormatCreatedAt(request.CreatedAtUtc),
                metadataDescriptor,
                pagesDescriptor);
            var manifestBytes = SerializeManifest(manifest);
            await WriteBytesEntryAsync(
                    archive,
                    ArchiveV1Format.ManifestEntryName,
                    manifestBytes,
                    1,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        static List<ArchiveV1ValidationIssue> ValidateRequest(
            ArchiveV1WriteRequest request,
            IReadOnlyList<ArchiveV1MetadataRow> metadata)
        {
            var issues = new List<ArchiveV1ValidationIssue>();
            if (request.SourceNotebookFormatVersion !=
                ArchiveV1Format.SupportedSourceNotebookFormatVersion)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.UnsupportedVersion,
                    "manifest.json.sourceNotebookFormatVersion",
                    ArchiveV1Format.SupportedSourceNotebookFormatVersion,
                    request.SourceNotebookFormatVersion));
            }

            if (string.IsNullOrEmpty(request.Creator.Application) ||
                request.Creator.Application.Length > 128)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.InvalidPropertyValue,
                    "manifest.json.createdBy.application",
                    "1 to 128 characters"));
            }
            if (string.IsNullOrEmpty(request.Creator.Version) ||
                request.Creator.Version.Length > 128)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.InvalidPropertyValue,
                    "manifest.json.createdBy.version",
                    "1 to 128 characters"));
            }
            if (metadata.Count > ArchiveV1Format.MaximumMetadataRows)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.RecordLimitExceeded,
                    ArchiveV1Format.MetadataEntryName,
                    ArchiveV1Format.MaximumMetadataRows.ToString(CultureInfo.InvariantCulture),
                    metadata.Count.ToString(CultureInfo.InvariantCulture)));
            }
            issues.AddRange(ArchiveV1RecordValidator.ValidateMetadata(
                metadata,
                request.SourceNotebookFormatVersion));
            return issues;
        }

        static byte[] SerializeMetadata(IReadOnlyList<ArchiveV1MetadataRow> rows)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, IndentedWriterOptions))
            {
                writer.WriteStartArray();
                foreach (var row in rows)
                {
                    writer.WriteStartObject();
                    writer.WriteString("key", row.Key);
                    if (row.Value == null)
                        writer.WriteNull("value");
                    else
                        writer.WriteString("value", row.Value);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.Flush();
            }
            return AppendLf(buffer.WrittenSpan);
        }

        static async Task<ArchiveV1DataSetDescriptor> WritePagesAsync(
            ZipArchive archive,
            IAsyncEnumerable<ArchiveV1PageRow> pages,
            CancellationToken cancellationToken)
        {
            var entry = archive.CreateEntry(
                ArchiveV1Format.PagesEntryName,
                CompressionLevel.NoCompression);
            await using var destination = entry.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long byteLength = 0;
            long count = 0;
            var previousRowid = 0;
            var rowids = new HashSet<int>();
            var uuids = new HashSet<Guid>();

            await foreach (var row in pages.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var location = $"pages.ndjson:{count + 1}";
                var issues = ArchiveV1RecordValidator.ValidatePage(row, location);
                if (row != null && row.Rowid <= previousRowid)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.NonCanonicalOrder,
                        location + ".rowid",
                        "strictly ascending rowid"));
                }
                if (row != null && !rowids.Add(row.Rowid))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.DuplicatePageRowId,
                        location + ".rowid"));
                }
                if (row != null && Guid.TryParseExact(row.Uuid, "D", out var uuid) &&
                    !uuids.Add(uuid))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.DuplicatePageUuid,
                        location + ".uuid"));
                }
                if (issues.Count > 0)
                    throw CreateValidationException(issues);

                var record = SerializePage(row);
                if (record.Length > ArchiveV1Format.MaximumPageRecordLength)
                {
                    throw CreateValidationException(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.RecordTooLarge,
                        location,
                        ArchiveV1Format.MaximumPageRecordLength.ToString(CultureInfo.InvariantCulture),
                        record.Length.ToString(CultureInfo.InvariantCulture)));
                }
                if (count >= ArchiveV1Format.MaximumPageRecords)
                {
                    throw CreateValidationException(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.RecordLimitExceeded,
                        ArchiveV1Format.PagesEntryName,
                        ArchiveV1Format.MaximumPageRecords.ToString(CultureInfo.InvariantCulture)));
                }
                if (byteLength + record.Length + 1 > ArchiveV1Format.MaximumPagesLength)
                {
                    throw CreateValidationException(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.EntryTooLarge,
                        ArchiveV1Format.PagesEntryName,
                        ArchiveV1Format.MaximumPagesLength.ToString(CultureInfo.InvariantCulture)));
                }

                await destination.WriteAsync(record, cancellationToken).ConfigureAwait(false);
                await destination.WriteAsync(new byte[] { (byte)'\n' }, cancellationToken)
                    .ConfigureAwait(false);
                hash.AppendData(record);
                hash.AppendData(new byte[] { (byte)'\n' });
                byteLength += record.Length + 1;
                count++;
                previousRowid = row.Rowid;
            }

            return new ArchiveV1DataSetDescriptor(
                ArchiveV1Format.PagesEntryName,
                count,
                byteLength,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }

        static byte[] SerializePage(ArchiveV1PageRow row)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, CompactWriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteNumber("rowid", row.Rowid);
                writer.WriteString("uuid", row.Uuid);
                WriteNullableString(writer, "name", row.Name);
                writer.WriteNumber("index", row.Index);
                WriteNullableString(writer, "tags", row.Tags);
                WriteNullableString(writer, "contentType", row.ContentType);
                writer.WriteString("createTime", row.CreateTime);
                writer.WriteString("updateTime", row.UpdateTime);
                writer.WriteNumber("isErased", row.IsErased);
                WriteNullableString(writer, "text", row.Text);
                writer.WriteEndObject();
                writer.Flush();
            }
            return buffer.WrittenSpan.ToArray();
        }

        static byte[] SerializeManifest(ArchiveV1Manifest manifest)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, IndentedWriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("format", ArchiveV1Format.FormatIdentifier);
                writer.WriteNumber("archiveFormatVersion", ArchiveV1Format.ArchiveFormatVersion);
                writer.WriteNumber("contentModelVersion", ArchiveV1Format.ContentModelVersion);
                writer.WriteString("sourceNotebookFormatVersion", manifest.SourceNotebookFormatVersion);
                writer.WriteStartObject("createdBy");
                writer.WriteString("application", manifest.Application);
                writer.WriteString("version", manifest.ApplicationVersion);
                writer.WriteEndObject();
                writer.WriteString("createdAtUtc", manifest.CreatedAtUtc);
                writer.WriteStartObject("dataSets");
                WriteDescriptor(writer, "metadata", manifest.Metadata);
                WriteDescriptor(writer, "pages", manifest.Pages);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.Flush();
            }
            return AppendLf(buffer.WrittenSpan);
        }

        static void WriteDescriptor(
            Utf8JsonWriter writer,
            string propertyName,
            ArchiveV1DataSetDescriptor descriptor)
        {
            writer.WriteStartObject(propertyName);
            writer.WriteString("entry", descriptor.Entry);
            writer.WriteNumber("recordCount", descriptor.RecordCount);
            writer.WriteNumber("uncompressedByteLength", descriptor.UncompressedByteLength);
            writer.WriteString("sha256", descriptor.Sha256);
            writer.WriteEndObject();
        }

        static void WriteNullableString(Utf8JsonWriter writer, string name, string value)
        {
            if (value == null)
                writer.WriteNull(name);
            else
                writer.WriteString(name, value);
        }

        static byte[] AppendLf(ReadOnlySpan<byte> value)
        {
            var result = new byte[value.Length + 1];
            value.CopyTo(result);
            result[result.Length - 1] = (byte)'\n';
            return result;
        }

        static async Task<ArchiveV1DataSetDescriptor> WriteBytesEntryAsync(
            ZipArchive archive,
            string entryName,
            byte[] bytes,
            long recordCount,
            CancellationToken cancellationToken)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            await using var output = entry.Open();
            await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            return new ArchiveV1DataSetDescriptor(
                entryName,
                recordCount,
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        static ArchiveV1ValidationException CreateValidationException(
            ArchiveV1ValidationIssue issue)
        {
            return CreateValidationException(new[] { issue });
        }

        static ArchiveV1ValidationException CreateValidationException(
            IEnumerable<ArchiveV1ValidationIssue> issues)
        {
            return new ArchiveV1ValidationException(new ArchiveV1ValidationReport(
                ArchiveV1Classification.ArchiveV1,
                issues));
        }
    }
}
