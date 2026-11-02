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

namespace MemoriaNote.Archive;

/// <summary>Writes the canonical archive v1 content model 2 representation.</summary>
public sealed class ArchiveV1ContentModel2Writer
{
    private static readonly JsonWriterOptions CompactWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false
    };

    private static readonly JsonWriterOptions IndentedWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n"
    };

    /// <summary>Writes one content model 2 archive without closing the supplied stream.</summary>
    public async Task WriteAsync(
        Stream output,
        ArchiveV1ContentModel2WriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(request);
        if (!output.CanWrite)
            throw new ArgumentException("The archive output stream must be writable.", nameof(output));

        cancellationToken.ThrowIfCancellationRequested();
        var metadata = request.Metadata
            .Take(ArchiveV1Format.MaximumMetadataRows + 1)
            .ToArray();
        var issues = ValidateRequest(request, metadata);
        if (issues.Count > 0)
            throw ValidationException(issues);

        var orderedMetadata = metadata.OrderBy(row => row.Key, StringComparer.Ordinal).ToArray();
        var metadataBytes = SerializeMetadata(orderedMetadata);
        if (metadataBytes.LongLength > ArchiveV1Format.MaximumMetadataLength)
        {
            throw ValidationException(new ArchiveV1ValidationIssue(
                ArchiveV1IssueCode.EntryTooLarge,
                ArchiveV1Format.MetadataEntryName));
        }

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var metadataDescriptor = await WriteBytesEntryAsync(
                archive,
                ArchiveV1Format.MetadataEntryName,
                metadataBytes,
                orderedMetadata.LongLength,
                cancellationToken)
            .ConfigureAwait(false);
        var pagesDescriptor = await WritePagesAsync(archive, request.Pages, cancellationToken)
            .ConfigureAwait(false);
        var manifestBytes = SerializeManifest(
            request,
            metadataDescriptor,
            pagesDescriptor);
        await WriteBytesEntryAsync(
                archive,
                ArchiveV1Format.ManifestEntryName,
                manifestBytes,
                1,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<ArchiveV1ValidationIssue> ValidateRequest(
        ArchiveV1ContentModel2WriteRequest request,
        IReadOnlyList<ArchiveV1MetadataRow> metadata)
    {
        var issues = ArchiveV1ContentModel2RecordValidator.ValidateMetadata(metadata);
        if (metadata.Count > ArchiveV1Format.MaximumMetadataRows)
        {
            issues.Add(new ArchiveV1ValidationIssue(
                ArchiveV1IssueCode.RecordLimitExceeded,
                ArchiveV1Format.MetadataEntryName));
        }
        if (string.IsNullOrEmpty(request.Creator.Application) || request.Creator.Application.Length > 128)
        {
            issues.Add(new ArchiveV1ValidationIssue(
                ArchiveV1IssueCode.InvalidPropertyValue,
                "manifest.json.createdBy.application"));
        }
        if (string.IsNullOrEmpty(request.Creator.Version) || request.Creator.Version.Length > 128)
        {
            issues.Add(new ArchiveV1ValidationIssue(
                ArchiveV1IssueCode.InvalidPropertyValue,
                "manifest.json.createdBy.version"));
        }
        return issues;
    }

    private static byte[] SerializeMetadata(IEnumerable<ArchiveV1MetadataRow> rows)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, IndentedWriterOptions))
        {
            writer.WriteStartArray();
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                writer.WriteString("key", row.Key);
                if (row.Value is null)
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

    private static async Task<ArchiveV1DataSetDescriptor> WritePagesAsync(
        ZipArchive archive,
        IAsyncEnumerable<ArchiveV1ContentModel2PageRow> pages,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(ArchiveV1Format.PagesEntryName, CompressionLevel.NoCompression);
        await using var output = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var internalIds = new HashSet<long>();
        var pageIds = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        long count = 0;
        long byteLength = 0;
        long previousInternalId = 0;
        byte[] newline = [(byte)'\n'];

        await foreach (var row in pages.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var location = $"pages.ndjson:{count + 1}";
            var issues = ArchiveV1ContentModel2RecordValidator.ValidatePage(row, location);
            if (row is not null)
            {
                if (row.SourceInternalId <= previousInternalId)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.NonCanonicalOrder,
                        location + ".sourceInternalId"));
                }
                if (!internalIds.Add(row.SourceInternalId))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.DuplicatePageInternalId,
                        location + ".sourceInternalId"));
                }
                if (Guid.TryParseExact(row.PageId, "D", out var pageId) && !pageIds.Add(pageId))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.DuplicatePageId,
                        location + ".pageId"));
                }
                if (row.Name is not null && !names.Add(row.Name))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.DuplicatePageName,
                        location + ".name"));
                }
            }
            if (issues.Count > 0)
                throw ValidationException(issues);

            var bytes = SerializePage(row);
            if (bytes.Length > ArchiveV1Format.MaximumPageRecordLength)
                throw ValidationException(new ArchiveV1ValidationIssue(ArchiveV1IssueCode.RecordTooLarge, location));
            if (count >= ArchiveV1Format.MaximumPageRecords)
                throw ValidationException(new ArchiveV1ValidationIssue(ArchiveV1IssueCode.RecordLimitExceeded, ArchiveV1Format.PagesEntryName));
            if (byteLength + bytes.Length + 1 > ArchiveV1Format.MaximumPagesLength)
                throw ValidationException(new ArchiveV1ValidationIssue(ArchiveV1IssueCode.EntryTooLarge, ArchiveV1Format.PagesEntryName));

            await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(newline, cancellationToken).ConfigureAwait(false);
            hash.AppendData(bytes);
            hash.AppendData(newline);
            count++;
            byteLength += bytes.Length + 1;
            previousInternalId = row.SourceInternalId;
        }

        return new ArchiveV1DataSetDescriptor(
            ArchiveV1Format.PagesEntryName,
            count,
            byteLength,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static byte[] SerializePage(ArchiveV1ContentModel2PageRow row)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, CompactWriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sourceInternalId", row.SourceInternalId);
            writer.WriteString("pageId", row.PageId);
            writer.WriteString("name", row.Name);
            writer.WriteString("createTime", row.CreateTime);
            writer.WriteString("updateTime", row.UpdateTime);
            writer.WriteNumber("revision", row.Revision);
            writer.WriteString("tags", row.Tags);
            writer.WriteString("text", row.Text);
            writer.WriteEndObject();
            writer.Flush();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] SerializeManifest(
        ArchiveV1ContentModel2WriteRequest request,
        ArchiveV1DataSetDescriptor metadata,
        ArchiveV1DataSetDescriptor pages)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, IndentedWriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", ArchiveV1Format.FormatIdentifier);
            writer.WriteNumber("archiveFormatVersion", ArchiveV1Format.ArchiveFormatVersion);
            writer.WriteNumber("contentModelVersion", ArchiveV1ContentModel2Format.ContentModelVersion);
            writer.WriteString("sourceNotebookFormatVersion", ArchiveV1ContentModel2Format.SourceNotebookFormatVersion);
            writer.WriteStartObject("createdBy");
            writer.WriteString("application", request.Creator.Application);
            writer.WriteString("version", request.Creator.Version);
            writer.WriteEndObject();
            writer.WriteString("createdAtUtc", ArchiveV1Format.FormatCreatedAt(request.CreatedAtUtc));
            writer.WriteStartObject("dataSets");
            WriteDescriptor(writer, "metadata", metadata);
            WriteDescriptor(writer, "pages", pages);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
        }
        return AppendLf(buffer.WrittenSpan);
    }

    private static void WriteDescriptor(
        Utf8JsonWriter writer,
        string name,
        ArchiveV1DataSetDescriptor descriptor)
    {
        writer.WriteStartObject(name);
        writer.WriteString("entry", descriptor.Entry);
        writer.WriteNumber("recordCount", descriptor.RecordCount);
        writer.WriteNumber("uncompressedByteLength", descriptor.UncompressedByteLength);
        writer.WriteString("sha256", descriptor.Sha256);
        writer.WriteEndObject();
    }

    private static async Task<ArchiveV1DataSetDescriptor> WriteBytesEntryAsync(
        ZipArchive archive,
        string name,
        byte[] bytes,
        long count,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        await using var output = entry.Open();
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        return new ArchiveV1DataSetDescriptor(
            name,
            count,
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static byte[] AppendLf(ReadOnlySpan<byte> bytes)
    {
        var result = new byte[bytes.Length + 1];
        bytes.CopyTo(result);
        result[^1] = (byte)'\n';
        return result;
    }

    private static ArchiveV1ValidationException ValidationException(
        ArchiveV1ValidationIssue issue) => ValidationException(new[] { issue });

    private static ArchiveV1ValidationException ValidationException(
        IEnumerable<ArchiveV1ValidationIssue> issues) =>
        new(new ArchiveV1ValidationReport(ArchiveV1Classification.ArchiveV1, issues));
}
