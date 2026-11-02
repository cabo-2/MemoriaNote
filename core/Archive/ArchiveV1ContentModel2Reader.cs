using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Archive;

/// <summary>Classifies, validates, and streams archive v1 content model 2 records.</summary>
public sealed class ArchiveV1ContentModel2Reader
{
    /// <summary>Validates an archive completely without materializing all page records.</summary>
    public Task<ArchiveV1ValidationReport> ValidateAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        ValidateInput(input);
        return ProcessAsync(input, null, cancellationToken);
    }

    /// <summary>Validates an archive completely, then streams its records to a sink.</summary>
    public async Task<ArchiveV1ValidationReport> ReadAsync(
        Stream input,
        IArchiveV1ContentModel2RecordSink sink,
        CancellationToken cancellationToken)
    {
        ValidateInput(input);
        ArgumentNullException.ThrowIfNull(sink);
        var preflight = await ProcessAsync(input, null, cancellationToken).ConfigureAwait(false);
        if (!preflight.IsValid)
            return preflight;
        return await ReadPrevalidatedAsync(input, sink, cancellationToken).ConfigureAwait(false);
    }

    internal Task<ArchiveV1ValidationReport> ReadPrevalidatedAsync(
        Stream input,
        IArchiveV1ContentModel2RecordSink sink,
        CancellationToken cancellationToken)
    {
        ValidateInput(input);
        ArgumentNullException.ThrowIfNull(sink);
        return ProcessAsync(input, sink, cancellationToken);
    }

    private static void ValidateInput(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException(
                "The archive input stream must be readable and seekable.",
                nameof(input));
        }
    }

    private static async Task<ArchiveV1ValidationReport> ProcessAsync(
        Stream input,
        IArchiveV1ContentModel2RecordSink sink,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issues = new List<ArchiveV1ValidationIssue>();
        if (input.Length > ArchiveV1Format.MaximumArchiveLength)
        {
            issues.Add(Issue(ArchiveV1IssueCode.ArchiveTooLarge, "$archive"));
            return Report(ArchiveV1Classification.MalformedZip, issues);
        }

        input.Position = 0;
        try
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries.ToArray();
            var classification = Classify(entries);
            if (classification != ArchiveV1Classification.ArchiveV1)
            {
                issues.Add(Issue(
                    classification == ArchiveV1Classification.LegacyArchiveCandidate
                        ? ArchiveV1IssueCode.LegacyArchiveUnsupported
                        : ArchiveV1IssueCode.UnrecognizedZip,
                    "$archive"));
                return Report(classification, issues);
            }

            ValidateContainer(entries, issues);
            if (issues.Count > 0)
                return Report(classification, issues);

            var manifestBytes = await ReadEntryAsync(
                    entries.Single(entry => entry.FullName == ArchiveV1Format.ManifestEntryName),
                    ArchiveV1Format.MaximumManifestLength,
                    issues,
                    cancellationToken)
                .ConfigureAwait(false);
            if (manifestBytes is null)
                return Report(classification, issues);
            var manifest = ParseManifest(manifestBytes, issues);
            if (manifest is null || issues.Count > 0)
                return Report(classification, issues);

            var metadataBytes = await ReadEntryAsync(
                    entries.Single(entry => entry.FullName == ArchiveV1Format.MetadataEntryName),
                    ArchiveV1Format.MaximumMetadataLength,
                    issues,
                    cancellationToken)
                .ConfigureAwait(false);
            if (metadataBytes is null)
                return Report(classification, issues);
            var metadata = ParseMetadata(metadataBytes, issues);
            ValidateDescriptor(
                manifest.Metadata,
                metadataBytes.LongLength,
                CountMetadataRecords(metadataBytes),
                Sha256(metadataBytes),
                issues);
            if (metadata is not null && sink is not null && issues.Count == 0)
            {
                foreach (var row in metadata)
                    await sink.WriteMetadataAsync(row, cancellationToken).ConfigureAwait(false);
            }

            await ProcessPagesAsync(
                    entries.Single(entry => entry.FullName == ArchiveV1Format.PagesEntryName),
                    manifest.Pages,
                    sink,
                    issues,
                    cancellationToken)
                .ConfigureAwait(false);
            return Report(classification, issues);
        }
        catch (InvalidDataException)
        {
            issues.Add(Issue(ArchiveV1IssueCode.MalformedZip, "$archive"));
            return Report(ArchiveV1Classification.MalformedZip, issues);
        }
    }

    private static ArchiveV1Classification Classify(IReadOnlyList<ZipArchiveEntry> entries)
    {
        if (entries.Any(entry => entry.FullName == ArchiveV1Format.ManifestEntryName))
            return ArchiveV1Classification.ArchiveV1;
        var metadata = entries.Count(entry => entry.FullName == ArchiveV1Format.MetadataEntryName);
        var remaining = entries.Where(entry => entry.FullName != ArchiveV1Format.MetadataEntryName);
        if (metadata == 1 && remaining.All(entry =>
                entry.Name == entry.FullName &&
                entry.FullName.EndsWith(".json", StringComparison.Ordinal)))
        {
            return ArchiveV1Classification.LegacyArchiveCandidate;
        }
        return ArchiveV1Classification.UnrecognizedZip;
    }

    private static void ValidateContainer(
        IReadOnlyList<ZipArchiveEntry> entries,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        if (entries.Count != ArchiveV1Format.EntryNames.Length)
            issues.Add(Issue(ArchiveV1IssueCode.InvalidEntryCount, "$archive"));
        foreach (var group in entries.GroupBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
                issues.Add(Issue(ArchiveV1IssueCode.DuplicateEntry, group.Key));
        }
        foreach (var required in ArchiveV1Format.EntryNames)
        {
            if (!entries.Any(entry => entry.FullName == required))
                issues.Add(Issue(ArchiveV1IssueCode.MissingEntry, required));
        }

        long totalLength = 0;
        long totalCompressed = 0;
        foreach (var entry in entries)
        {
            if (entry.Name != entry.FullName ||
                entry.FullName.Contains('/') ||
                entry.FullName.Contains('\\'))
            {
                issues.Add(Issue(ArchiveV1IssueCode.InvalidEntryPath, entry.FullName));
            }
            if (!ArchiveV1Format.EntryNames.Contains(entry.FullName, StringComparer.Ordinal))
                issues.Add(Issue(ArchiveV1IssueCode.UnexpectedEntry, entry.FullName));
            var limit = EntryLimit(entry.FullName);
            if (limit.HasValue && entry.Length > limit.Value)
                issues.Add(Issue(ArchiveV1IssueCode.EntryTooLarge, entry.FullName));
            if (CompressionRatio(entry.Length, entry.CompressedLength) > ArchiveV1Format.MaximumCompressionRatio)
                issues.Add(Issue(ArchiveV1IssueCode.CompressionRatioExceeded, entry.FullName));
            totalLength = SaturatingAdd(totalLength, entry.Length);
            totalCompressed = SaturatingAdd(totalCompressed, entry.CompressedLength);
        }
        if (CompressionRatio(totalLength, totalCompressed) > ArchiveV1Format.MaximumCompressionRatio)
            issues.Add(Issue(ArchiveV1IssueCode.CompressionRatioExceeded, "$archive"));
    }

    private static long? EntryLimit(string name) => name switch
    {
        ArchiveV1Format.ManifestEntryName => ArchiveV1Format.MaximumManifestLength,
        ArchiveV1Format.MetadataEntryName => ArchiveV1Format.MaximumMetadataLength,
        ArchiveV1Format.PagesEntryName => ArchiveV1Format.MaximumPagesLength,
        _ => null
    };

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        long maximumLength,
        ICollection<ArchiveV1ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var input = entry.Open();
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (output.Length + read > maximumLength)
                {
                    issues.Add(Issue(ArchiveV1IssueCode.EntryTooLarge, entry.FullName));
                    return null;
                }
                output.Write(buffer, 0, read);
            }
            var bytes = output.ToArray();
            if (CompressionRatio(bytes.LongLength, entry.CompressedLength) > ArchiveV1Format.MaximumCompressionRatio)
            {
                issues.Add(Issue(ArchiveV1IssueCode.CompressionRatioExceeded, entry.FullName));
                return null;
            }
            return bytes;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            issues.Add(Issue(ArchiveV1IssueCode.UnreadableEntry, entry.FullName));
            return null;
        }
    }

    private static Cm2Manifest ParseManifest(
        byte[] bytes,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        if (!ValidateJsonBytes(bytes, ArchiveV1Format.ManifestEntryName, issues))
            return null;
        try
        {
            using var document = JsonDocument.Parse(bytes, ArchiveV1Format.StrictDocumentOptions);
            var root = document.RootElement;
            ValidateObject(
                root,
                ["format", "archiveFormatVersion", "contentModelVersion", "sourceNotebookFormatVersion", "createdBy", "createdAtUtc", "dataSets"],
                "manifest.json",
                issues);
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            var format = RequiredString(root, "format", "manifest.json", issues);
            var archiveVersion = RequiredInteger(root, "archiveFormatVersion", 0, int.MaxValue, "manifest.json", issues);
            var contentVersion = RequiredInteger(root, "contentModelVersion", 0, int.MaxValue, "manifest.json", issues);
            var sourceVersion = RequiredString(root, "sourceNotebookFormatVersion", "manifest.json", issues);
            var createdAt = RequiredString(root, "createdAtUtc", "manifest.json", issues);
            if (format is not null && format != ArchiveV1Format.FormatIdentifier)
                issues.Add(Issue(ArchiveV1IssueCode.InvalidFormatIdentifier, "manifest.json.format"));
            if (archiveVersion.HasValue && contentVersion.HasValue && sourceVersion is not null &&
                (archiveVersion != ArchiveV1Format.ArchiveFormatVersion ||
                 contentVersion != ArchiveV1ContentModel2Format.ContentModelVersion ||
                 sourceVersion != ArchiveV1ContentModel2Format.SourceNotebookFormatVersion))
            {
                issues.Add(Issue(ArchiveV1IssueCode.UnsupportedVersion, "manifest.json"));
            }
            if (createdAt is not null && !ArchiveV1Format.IsValidCreatedAt(createdAt))
                InvalidValue(issues, "manifest.json.createdAtUtc", "canonical UTC timestamp");
            ParseCreator(root, issues);
            var descriptors = ParseDataSets(root, issues);
            if (descriptors is null || issues.Count > 0)
                return null;
            return new Cm2Manifest(descriptors.Value.Metadata, descriptors.Value.Pages);
        }
        catch (JsonException)
        {
            issues.Add(Issue(ArchiveV1IssueCode.MalformedJson, ArchiveV1Format.ManifestEntryName));
            return null;
        }
    }

    private static void ParseCreator(JsonElement root, ICollection<ArchiveV1ValidationIssue> issues)
    {
        if (!root.TryGetProperty("createdBy", out var creator))
            return;
        ValidateObject(creator, ["application", "version"], "manifest.json.createdBy", issues);
        if (creator.ValueKind != JsonValueKind.Object)
            return;
        var application = RequiredString(creator, "application", "manifest.json.createdBy", issues);
        var version = RequiredString(creator, "version", "manifest.json.createdBy", issues);
        if (application is not null && application.Length is < 1 or > 128)
            InvalidValue(issues, "manifest.json.createdBy.application", "1 to 128 characters");
        if (version is not null && version.Length is < 1 or > 128)
            InvalidValue(issues, "manifest.json.createdBy.version", "1 to 128 characters");
    }

    private static (ArchiveV1DataSetDescriptor Metadata, ArchiveV1DataSetDescriptor Pages)? ParseDataSets(
        JsonElement root,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        if (!root.TryGetProperty("dataSets", out var dataSets))
            return null;
        ValidateObject(dataSets, ["metadata", "pages"], "manifest.json.dataSets", issues);
        if (dataSets.ValueKind != JsonValueKind.Object)
            return null;
        if (!dataSets.TryGetProperty("metadata", out var metadata) ||
            !dataSets.TryGetProperty("pages", out var pages))
        {
            return null;
        }
        var metadataDescriptor = ParseDescriptor(
            metadata,
            "manifest.json.dataSets.metadata",
            ArchiveV1Format.MetadataEntryName,
            ArchiveV1Format.MaximumMetadataRows,
            ArchiveV1Format.MaximumMetadataLength,
            issues);
        var pagesDescriptor = ParseDescriptor(
            pages,
            "manifest.json.dataSets.pages",
            ArchiveV1Format.PagesEntryName,
            ArchiveV1Format.MaximumPageRecords,
            ArchiveV1Format.MaximumPagesLength,
            issues);
        return metadataDescriptor is null || pagesDescriptor is null
            ? null
            : (metadataDescriptor, pagesDescriptor);
    }

    private static ArchiveV1DataSetDescriptor ParseDescriptor(
        JsonElement element,
        string location,
        string expectedEntry,
        long maximumRecords,
        long maximumLength,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        ValidateObject(element, ["entry", "recordCount", "uncompressedByteLength", "sha256"], location, issues);
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        var entry = RequiredString(element, "entry", location, issues);
        var count = RequiredInteger(element, "recordCount", 0, long.MaxValue, location, issues);
        var length = RequiredInteger(element, "uncompressedByteLength", 0, long.MaxValue, location, issues);
        var sha = RequiredString(element, "sha256", location, issues);
        if (entry is not null && entry != expectedEntry)
            InvalidValue(issues, location + ".entry", expectedEntry);
        if (count > maximumRecords)
            issues.Add(Issue(ArchiveV1IssueCode.RecordLimitExceeded, location + ".recordCount"));
        if (length > maximumLength)
            issues.Add(Issue(ArchiveV1IssueCode.EntryTooLarge, location + ".uncompressedByteLength"));
        if (sha is not null && !ArchiveV1Format.IsLowercaseSha256(sha))
            InvalidValue(issues, location + ".sha256", "lowercase SHA-256");
        return entry is null || !count.HasValue || !length.HasValue || sha is null
            ? null
            : new ArchiveV1DataSetDescriptor(entry, count.Value, length.Value, sha);
    }

    private static IReadOnlyList<ArchiveV1MetadataRow> ParseMetadata(
        byte[] bytes,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        if (!ValidateJsonBytes(bytes, ArchiveV1Format.MetadataEntryName, issues))
            return null;
        try
        {
            using var document = JsonDocument.Parse(bytes, ArchiveV1Format.StrictDocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                InvalidType(issues, ArchiveV1Format.MetadataEntryName, "array");
                return null;
            }
            var rows = new List<ArchiveV1MetadataRow>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var location = $"metadata.json[{rows.Count}]";
                ValidateObject(element, ["key", "value"], location, issues);
                if (element.ValueKind != JsonValueKind.Object)
                {
                    rows.Add(null);
                    continue;
                }
                var key = RequiredString(element, "key", location, issues);
                string value = null;
                if (element.TryGetProperty("value", out var valueElement))
                {
                    if (valueElement.ValueKind == JsonValueKind.String)
                        value = valueElement.GetString();
                    else if (valueElement.ValueKind != JsonValueKind.Null)
                        InvalidType(issues, location + ".value", "string or null");
                }
                rows.Add(new ArchiveV1MetadataRow(key, value));
                if (rows.Count > ArchiveV1Format.MaximumMetadataRows)
                {
                    issues.Add(Issue(ArchiveV1IssueCode.RecordLimitExceeded, ArchiveV1Format.MetadataEntryName));
                    break;
                }
            }
            foreach (var issue in ArchiveV1ContentModel2RecordValidator.ValidateMetadata(rows))
                issues.Add(issue);
            return rows;
        }
        catch (JsonException)
        {
            issues.Add(Issue(ArchiveV1IssueCode.MalformedJson, ArchiveV1Format.MetadataEntryName));
            return null;
        }
    }

    private static async Task ProcessPagesAsync(
        ZipArchiveEntry entry,
        ArchiveV1DataSetDescriptor descriptor,
        IArchiveV1ContentModel2RecordSink sink,
        ICollection<ArchiveV1ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        long length = 0;
        long count = 0;
        var sawBytes = false;
        var endedWithLf = false;
        var firstBytes = new List<byte>(3);
        var internalIds = new HashSet<long>();
        var pageIds = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var line = new MemoryStream();
        try
        {
            await using var input = entry.Open();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                sawBytes = true;
                length += read;
                if (length > ArchiveV1Format.MaximumPagesLength)
                {
                    issues.Add(Issue(ArchiveV1IssueCode.EntryTooLarge, ArchiveV1Format.PagesEntryName));
                    return;
                }
                hash.AppendData(buffer, 0, read);
                for (var index = 0; index < read; index++)
                {
                    var current = buffer[index];
                    if (firstBytes.Count < 3)
                        firstBytes.Add(current);
                    if (current == '\r')
                    {
                        issues.Add(Issue(ArchiveV1IssueCode.InvalidNdjsonFraming, $"pages.ndjson:{count + 1}"));
                        return;
                    }
                    if (current == '\n')
                    {
                        endedWithLf = true;
                        if (line.Length == 0)
                        {
                            issues.Add(Issue(ArchiveV1IssueCode.InvalidNdjsonFraming, $"pages.ndjson:{count + 1}"));
                            return;
                        }
                        count++;
                        if (count > ArchiveV1Format.MaximumPageRecords)
                        {
                            issues.Add(Issue(ArchiveV1IssueCode.RecordLimitExceeded, ArchiveV1Format.PagesEntryName));
                            return;
                        }
                        var row = ParsePage(line.ToArray(), count, issues);
                        if (row is not null)
                        {
                            if (!internalIds.Add(row.SourceInternalId))
                                issues.Add(Issue(ArchiveV1IssueCode.DuplicatePageInternalId, $"pages.ndjson:{count}.sourceInternalId"));
                            if (Guid.TryParseExact(row.PageId, "D", out var pageId) && !pageIds.Add(pageId))
                                issues.Add(Issue(ArchiveV1IssueCode.DuplicatePageId, $"pages.ndjson:{count}.pageId"));
                            if (row.Name is not null && !names.Add(row.Name))
                                issues.Add(Issue(ArchiveV1IssueCode.DuplicatePageName, $"pages.ndjson:{count}.name"));
                            if (sink is not null && issues.Count == 0)
                                await sink.WritePageAsync(row, cancellationToken).ConfigureAwait(false);
                        }
                        line.SetLength(0);
                    }
                    else
                    {
                        endedWithLf = false;
                        line.WriteByte(current);
                        if (line.Length > ArchiveV1Format.MaximumPageRecordLength)
                        {
                            issues.Add(Issue(ArchiveV1IssueCode.RecordTooLarge, $"pages.ndjson:{count + 1}"));
                            return;
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            issues.Add(Issue(ArchiveV1IssueCode.UnreadableEntry, ArchiveV1Format.PagesEntryName));
            return;
        }

        if (firstBytes.Count == 3 && firstBytes[0] == 0xef && firstBytes[1] == 0xbb && firstBytes[2] == 0xbf)
            issues.Add(Issue(ArchiveV1IssueCode.ByteOrderMarkNotAllowed, ArchiveV1Format.PagesEntryName));
        if (sawBytes && !endedWithLf)
            issues.Add(Issue(ArchiveV1IssueCode.InvalidNdjsonFraming, $"pages.ndjson:{count + 1}"));
        if (CompressionRatio(length, entry.CompressedLength) > ArchiveV1Format.MaximumCompressionRatio)
            issues.Add(Issue(ArchiveV1IssueCode.CompressionRatioExceeded, ArchiveV1Format.PagesEntryName));
        ValidateDescriptor(
            descriptor,
            length,
            count,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            issues);
    }

    private static ArchiveV1ContentModel2PageRow ParsePage(
        byte[] bytes,
        long recordNumber,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        var location = $"pages.ndjson:{recordNumber}";
        if (!ValidateJsonBytes(bytes, location, issues))
            return null;
        try
        {
            using var document = JsonDocument.Parse(bytes, ArchiveV1Format.StrictDocumentOptions);
            var root = document.RootElement;
            ValidateObject(
                root,
                ["sourceInternalId", "pageId", "name", "createTime", "updateTime", "revision", "tags", "text"],
                location,
                issues);
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            var internalId = RequiredInteger(root, "sourceInternalId", 1, long.MaxValue, location, issues);
            var pageId = RequiredString(root, "pageId", location, issues);
            var name = RequiredString(root, "name", location, issues);
            var createTime = RequiredString(root, "createTime", location, issues);
            var updateTime = RequiredString(root, "updateTime", location, issues);
            var revision = RequiredInteger(root, "revision", 1, long.MaxValue, location, issues);
            var tags = RequiredString(root, "tags", location, issues);
            var text = RequiredString(root, "text", location, issues);
            if (!internalId.HasValue || pageId is null || name is null || createTime is null ||
                updateTime is null || !revision.HasValue || tags is null || text is null)
            {
                return null;
            }
            var row = new ArchiveV1ContentModel2PageRow(
                internalId.Value,
                pageId,
                name,
                createTime,
                updateTime,
                revision.Value,
                tags,
                text);
            foreach (var issue in ArchiveV1ContentModel2RecordValidator.ValidatePage(row, location))
                issues.Add(issue);
            return row;
        }
        catch (JsonException)
        {
            issues.Add(Issue(ArchiveV1IssueCode.MalformedJson, location));
            return null;
        }
    }

    private static bool ValidateJsonBytes(
        byte[] bytes,
        string location,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
        {
            issues.Add(Issue(ArchiveV1IssueCode.ByteOrderMarkNotAllowed, location));
            return false;
        }
        if (!ArchiveV1Format.IsValidUtf8(bytes))
        {
            issues.Add(Issue(ArchiveV1IssueCode.InvalidUtf8, location));
            return false;
        }
        return true;
    }

    private static void ValidateObject(
        JsonElement element,
        IReadOnlyCollection<string> allowedNames,
        string location,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            InvalidType(issues, location, "object");
            return;
        }
        var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!found.Add(property.Name))
                issues.Add(Issue(ArchiveV1IssueCode.DuplicateProperty, location + "." + property.Name));
            if (!allowed.Contains(property.Name))
                issues.Add(Issue(ArchiveV1IssueCode.UnknownProperty, location + "." + property.Name));
        }
        foreach (var required in allowed)
        {
            if (!found.Contains(required))
                issues.Add(Issue(ArchiveV1IssueCode.MissingProperty, location + "." + required));
        }
    }

    private static string RequiredString(
        JsonElement parent,
        string name,
        string location,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        if (!parent.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.String)
        {
            InvalidType(issues, location + "." + name, "string");
            return null;
        }
        return value.GetString();
    }

    private static long? RequiredInteger(
        JsonElement parent,
        string name,
        long minimum,
        long maximum,
        string location,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        if (!parent.TryGetProperty(name, out var value))
            return null;
        if (!ArchiveV1Format.IsDecimalInteger(value, minimum, maximum))
        {
            InvalidValue(issues, location + "." + name, "canonical decimal integer");
            return null;
        }
        return value.GetInt64();
    }

    private static void ValidateDescriptor(
        ArchiveV1DataSetDescriptor descriptor,
        long actualLength,
        long actualCount,
        string actualSha,
        ICollection<ArchiveV1ValidationIssue> issues)
    {
        if (descriptor.UncompressedByteLength != actualLength)
            issues.Add(Issue(ArchiveV1IssueCode.ByteLengthMismatch, descriptor.Entry));
        if (descriptor.RecordCount != actualCount)
            issues.Add(Issue(ArchiveV1IssueCode.RecordCountMismatch, descriptor.Entry));
        if (descriptor.Sha256 != actualSha)
            issues.Add(Issue(ArchiveV1IssueCode.ChecksumMismatch, descriptor.Entry));
    }

    private static long CountMetadataRecords(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, ArchiveV1Format.StrictDocumentOptions);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static void InvalidType(
        ICollection<ArchiveV1ValidationIssue> issues,
        string location,
        string expected) =>
        issues.Add(new ArchiveV1ValidationIssue(ArchiveV1IssueCode.InvalidPropertyType, location, expected));

    private static void InvalidValue(
        ICollection<ArchiveV1ValidationIssue> issues,
        string location,
        string expected) =>
        issues.Add(new ArchiveV1ValidationIssue(ArchiveV1IssueCode.InvalidPropertyValue, location, expected));

    private static ArchiveV1ValidationIssue Issue(ArchiveV1IssueCode code, string location) => new(code, location);

    private static ArchiveV1ValidationReport Report(
        ArchiveV1Classification classification,
        IEnumerable<ArchiveV1ValidationIssue> issues) => new(classification, issues);

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static double CompressionRatio(long length, long compressedLength) =>
        length == 0 ? 0 : (double)length / Math.Max(compressedLength, 1);

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private sealed class Cm2Manifest
    {
        internal Cm2Manifest(
            ArchiveV1DataSetDescriptor metadata,
            ArchiveV1DataSetDescriptor pages)
        {
            Metadata = metadata;
            Pages = pages;
        }

        internal ArchiveV1DataSetDescriptor Metadata { get; }

        internal ArchiveV1DataSetDescriptor Pages { get; }
    }
}
