using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Archive
{
    /// <summary>Classifies, validates, and streams records from MemoriaNote archive v1.</summary>
    public sealed class ArchiveV1Reader
    {
        /// <summary>Validates an archive without materializing all page records.</summary>
        /// <param name="input">A readable, seekable archive stream.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The archive classification and validation report.</returns>
        public Task<ArchiveV1ValidationReport> ValidateAsync(
            Stream input,
            CancellationToken cancellationToken)
        {
            ValidateInputStream(input);
            return ProcessAsync(input, null, cancellationToken);
        }

        /// <summary>
        /// Validates an archive completely, then streams its validated records to a sink.
        /// </summary>
        /// <param name="input">A readable, seekable archive stream.</param>
        /// <param name="sink">The destination for validated records.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The archive validation report.</returns>
        public async Task<ArchiveV1ValidationReport> ReadAsync(
            Stream input,
            IArchiveV1RecordSink sink,
            CancellationToken cancellationToken)
        {
            ValidateInputStream(input);
            if (sink == null)
                throw new ArgumentNullException(nameof(sink));

            var preflight = await ProcessAsync(input, null, cancellationToken)
                .ConfigureAwait(false);
            if (!preflight.IsValid)
                return preflight;

            return await ProcessAsync(input, sink, cancellationToken).ConfigureAwait(false);
        }

        static void ValidateInputStream(Stream input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (!input.CanRead || !input.CanSeek)
            {
                throw new ArgumentException(
                    "The archive input stream must be readable and seekable.",
                    nameof(input));
            }
        }

        static async Task<ArchiveV1ValidationReport> ProcessAsync(
            Stream input,
            IArchiveV1RecordSink sink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var issues = new List<ArchiveV1ValidationIssue>();
            if (input.Length > ArchiveV1Format.MaximumArchiveLength)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.ArchiveTooLarge,
                    "$archive",
                    ArchiveV1Format.MaximumArchiveLength.ToString(CultureInfo.InvariantCulture),
                    input.Length.ToString(CultureInfo.InvariantCulture)));
                return Report(ArchiveV1Classification.MalformedZip, issues);
            }

            input.Position = 0;
            try
            {
                using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
                var entries = archive.Entries.ToList();
                var classification = Classify(entries);
                if (classification == ArchiveV1Classification.LegacyArchiveCandidate)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.LegacyArchiveUnsupported,
                        "$archive"));
                    return Report(classification, issues);
                }
                if (classification == ArchiveV1Classification.UnrecognizedZip)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.UnrecognizedZip,
                        "$archive"));
                    return Report(classification, issues);
                }

                ValidateContainer(entries, issues);
                if (issues.Count > 0)
                    return Report(classification, issues);

                var manifestEntry = entries.Single(entry =>
                    entry.FullName == ArchiveV1Format.ManifestEntryName);
                var manifestBytes = await ReadEntryAsync(
                        manifestEntry,
                        ArchiveV1Format.MaximumManifestLength,
                        issues,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (manifestBytes == null)
                    return Report(classification, issues);

                var manifest = ParseManifest(manifestBytes, issues);
                if (manifest == null || issues.Count > 0)
                    return Report(classification, issues);

                var metadataEntry = entries.Single(entry =>
                    entry.FullName == ArchiveV1Format.MetadataEntryName);
                var metadataBytes = await ReadEntryAsync(
                        metadataEntry,
                        ArchiveV1Format.MaximumMetadataLength,
                        issues,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (metadataBytes == null)
                    return Report(classification, issues);

                ValidateDescriptor(
                    manifest.Metadata,
                    metadataBytes.LongLength,
                    CountMetadataRecords(metadataBytes),
                    Sha256(metadataBytes),
                    issues);
                var metadata = ParseMetadata(
                    metadataBytes,
                    manifest.SourceNotebookFormatVersion,
                    issues);
                if (metadata != null && sink != null && issues.Count == 0)
                {
                    foreach (var row in metadata)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await sink.WriteMetadataAsync(row, cancellationToken).ConfigureAwait(false);
                    }
                }

                var pagesEntry = entries.Single(entry =>
                    entry.FullName == ArchiveV1Format.PagesEntryName);
                var pagesLength = await ProcessPagesAsync(
                        pagesEntry,
                        manifest.Pages,
                        sink,
                        issues,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (pagesLength.HasValue)
                {
                    var actualTotalLength = SaturatingAdd(
                        manifestBytes.LongLength,
                        SaturatingAdd(metadataBytes.LongLength, pagesLength.Value));
                    var compressedTotalLength = entries.Aggregate(
                        0L,
                        (total, entry) => SaturatingAdd(total, entry.CompressedLength));
                    if (CompressionRatio(actualTotalLength, compressedTotalLength) >
                        ArchiveV1Format.MaximumCompressionRatio)
                    {
                        issues.Add(new ArchiveV1ValidationIssue(
                            ArchiveV1IssueCode.CompressionRatioExceeded,
                            "$archive"));
                    }
                }
                return Report(classification, issues);
            }
            catch (InvalidDataException)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.MalformedZip,
                    "$archive"));
                return Report(ArchiveV1Classification.MalformedZip, issues);
            }
        }

        static ArchiveV1Classification Classify(IReadOnlyList<ZipArchiveEntry> entries)
        {
            if (entries.Any(entry => entry.FullName == ArchiveV1Format.ManifestEntryName))
                return ArchiveV1Classification.ArchiveV1;

            var metadataCount = entries.Count(entry =>
                entry.FullName == ArchiveV1Format.MetadataEntryName &&
                entry.Name == ArchiveV1Format.MetadataEntryName);
            var legacyPages = entries.Where(entry =>
                entry.FullName != ArchiveV1Format.MetadataEntryName).ToList();
            if (metadataCount == 1 && legacyPages.All(IsLegacyPageEntry))
                return ArchiveV1Classification.LegacyArchiveCandidate;

            return ArchiveV1Classification.UnrecognizedZip;
        }

        static bool IsLegacyPageEntry(ZipArchiveEntry entry)
        {
            if (entry.Name != entry.FullName || !entry.FullName.EndsWith(".json", StringComparison.Ordinal))
                return false;
            var stem = entry.FullName.Substring(0, entry.FullName.Length - 5);
            return stem.Length > 0 && stem.All(character => character >= '0' && character <= '9');
        }

        static void ValidateContainer(
            IReadOnlyList<ZipArchiveEntry> entries,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (entries.Count != ArchiveV1Format.EntryNames.Length)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.InvalidEntryCount,
                    "$archive",
                    ArchiveV1Format.EntryNames.Length.ToString(CultureInfo.InvariantCulture),
                    entries.Count.ToString(CultureInfo.InvariantCulture)));
            }

            foreach (var group in entries.GroupBy(entry => entry.FullName, StringComparer.Ordinal))
            {
                if (group.Count() > 1)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.DuplicateEntry,
                        group.Key));
                }
            }

            foreach (var required in ArchiveV1Format.EntryNames)
            {
                if (!entries.Any(entry => entry.FullName == required))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.MissingEntry,
                        required));
                }
            }

            long totalLength = 0;
            long totalCompressedLength = 0;
            foreach (var entry in entries)
            {
                if (entry.Name != entry.FullName ||
                    entry.FullName.Contains('/') || entry.FullName.Contains('\\'))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.InvalidEntryPath,
                        entry.FullName));
                }
                if (!ArchiveV1Format.EntryNames.Contains(entry.FullName, StringComparer.Ordinal))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.UnexpectedEntry,
                        entry.FullName));
                }

                var limit = GetEntryLimit(entry.FullName);
                if (limit.HasValue && entry.Length > limit.Value)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.EntryTooLarge,
                        entry.FullName,
                        limit.Value.ToString(CultureInfo.InvariantCulture),
                        entry.Length.ToString(CultureInfo.InvariantCulture)));
                }
                if (CompressionRatio(entry.Length, entry.CompressedLength) >
                    ArchiveV1Format.MaximumCompressionRatio)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.CompressionRatioExceeded,
                        entry.FullName));
                }
                totalLength = SaturatingAdd(totalLength, entry.Length);
                totalCompressedLength = SaturatingAdd(
                    totalCompressedLength,
                    entry.CompressedLength);
            }

            if (CompressionRatio(totalLength, totalCompressedLength) >
                ArchiveV1Format.MaximumCompressionRatio)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.CompressionRatioExceeded,
                    "$archive"));
            }
        }

        static long? GetEntryLimit(string entryName)
        {
            return entryName switch
            {
                ArchiveV1Format.ManifestEntryName => ArchiveV1Format.MaximumManifestLength,
                ArchiveV1Format.MetadataEntryName => ArchiveV1Format.MaximumMetadataLength,
                ArchiveV1Format.PagesEntryName => ArchiveV1Format.MaximumPagesLength,
                _ => null
            };
        }

        static double CompressionRatio(long length, long compressedLength)
        {
            return length == 0 ? 0 : (double)length / Math.Max(compressedLength, 1);
        }

        static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        static async Task<byte[]> ReadEntryAsync(
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
                        issues.Add(new ArchiveV1ValidationIssue(
                            ArchiveV1IssueCode.EntryTooLarge,
                            entry.FullName,
                            maximumLength.ToString(CultureInfo.InvariantCulture)));
                        return null;
                    }
                    output.Write(buffer, 0, read);
                }
                var result = output.ToArray();
                if (CompressionRatio(result.LongLength, entry.CompressedLength) >
                    ArchiveV1Format.MaximumCompressionRatio)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.CompressionRatioExceeded,
                        entry.FullName));
                    return null;
                }
                return result;
            }
            catch (InvalidDataException)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.UnreadableEntry,
                    entry.FullName));
                return null;
            }
            catch (NotSupportedException)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.UnreadableEntry,
                    entry.FullName));
                return null;
            }
        }

        static ArchiveV1Manifest ParseManifest(
            byte[] bytes,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (HasBom(bytes))
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.ByteOrderMarkNotAllowed,
                    ArchiveV1Format.ManifestEntryName));
                return null;
            }
            if (!ArchiveV1Format.IsValidUtf8(bytes))
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.InvalidUtf8,
                    ArchiveV1Format.ManifestEntryName));
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(bytes, ArchiveV1Format.StrictDocumentOptions);
                var root = document.RootElement;
                if (!ValidateObject(
                    root,
                    new[]
                    {
                        "format", "archiveFormatVersion", "contentModelVersion",
                        "sourceNotebookFormatVersion", "createdBy", "createdAtUtc", "dataSets"
                    },
                    "manifest.json",
                    issues))
                {
                    return null;
                }

                var format = GetRequiredString(root, "format", "manifest.json", issues);
                var archiveVersion = GetRequiredInteger(
                    root, "archiveFormatVersion", 0, int.MaxValue, "manifest.json", issues);
                var contentVersion = GetRequiredInteger(
                    root, "contentModelVersion", 0, int.MaxValue, "manifest.json", issues);
                var sourceVersion = GetRequiredString(
                    root, "sourceNotebookFormatVersion", "manifest.json", issues);
                var createdAt = GetRequiredString(root, "createdAtUtc", "manifest.json", issues);

                if (format != null && format != ArchiveV1Format.FormatIdentifier)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.InvalidFormatIdentifier,
                        "manifest.json.format",
                        ArchiveV1Format.FormatIdentifier,
                        format));
                }
                if (sourceVersion != null && (sourceVersion.Length == 0 || sourceVersion.Length > 64))
                {
                    InvalidValue(issues, "manifest.json.sourceNotebookFormatVersion", "1 to 64 characters");
                }
                if (archiveVersion.HasValue && contentVersion.HasValue && sourceVersion != null &&
                    (archiveVersion != ArchiveV1Format.ArchiveFormatVersion ||
                     contentVersion != ArchiveV1Format.ContentModelVersion ||
                     sourceVersion != ArchiveV1Format.SupportedSourceNotebookFormatVersion))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.UnsupportedVersion,
                        "manifest.json",
                        "archive=1, content=1, source=1"));
                }
                if (createdAt != null && !ArchiveV1Format.IsValidCreatedAt(createdAt))
                    InvalidValue(issues, "manifest.json.createdAtUtc", "canonical UTC timestamp");

                var creator = ParseCreator(root, issues);
                var descriptors = ParseDataSets(root, issues);
                if (format == null || sourceVersion == null || createdAt == null ||
                    creator == null || descriptors == null ||
                    archiveVersion == null || contentVersion == null)
                {
                    return null;
                }

                return new ArchiveV1Manifest(
                    sourceVersion,
                    creator.Value.Application,
                    creator.Value.Version,
                    createdAt,
                    descriptors.Value.Metadata,
                    descriptors.Value.Pages);
            }
            catch (JsonException)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.MalformedJson,
                    ArchiveV1Format.ManifestEntryName));
                return null;
            }
        }

        static (string Application, string Version)? ParseCreator(
            JsonElement root,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (!root.TryGetProperty("createdBy", out var value))
                return null;
            if (!ValidateObject(
                value,
                new[] { "application", "version" },
                "manifest.json.createdBy",
                issues))
            {
                return null;
            }
            var application = GetRequiredString(
                value, "application", "manifest.json.createdBy", issues);
            var version = GetRequiredString(
                value, "version", "manifest.json.createdBy", issues);
            if (application != null && (application.Length == 0 || application.Length > 128))
                InvalidValue(issues, "manifest.json.createdBy.application", "1 to 128 characters");
            if (version != null && (version.Length == 0 || version.Length > 128))
                InvalidValue(issues, "manifest.json.createdBy.version", "1 to 128 characters");
            return application == null || version == null ? null : (application, version);
        }

        static (ArchiveV1DataSetDescriptor Metadata, ArchiveV1DataSetDescriptor Pages)?
            ParseDataSets(
                JsonElement root,
                ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (!root.TryGetProperty("dataSets", out var value))
                return null;
            if (!ValidateObject(
                value,
                new[] { "metadata", "pages" },
                "manifest.json.dataSets",
                issues))
            {
                return null;
            }
            if (!value.TryGetProperty("metadata", out var metadataElement) ||
                !value.TryGetProperty("pages", out var pagesElement))
            {
                return null;
            }
            var metadata = ParseDescriptor(
                metadataElement,
                "manifest.json.dataSets.metadata",
                ArchiveV1Format.MetadataEntryName,
                ArchiveV1Format.MaximumMetadataRows,
                ArchiveV1Format.MaximumMetadataLength,
                issues);
            var pages = ParseDescriptor(
                pagesElement,
                "manifest.json.dataSets.pages",
                ArchiveV1Format.PagesEntryName,
                ArchiveV1Format.MaximumPageRecords,
                ArchiveV1Format.MaximumPagesLength,
                issues);
            return metadata == null || pages == null ? null : (metadata, pages);
        }

        static ArchiveV1DataSetDescriptor ParseDescriptor(
            JsonElement value,
            string location,
            string expectedEntry,
            long maximumRecords,
            long maximumLength,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (!ValidateObject(
                value,
                new[] { "entry", "recordCount", "uncompressedByteLength", "sha256" },
                location,
                issues))
            {
                return null;
            }
            var entry = GetRequiredString(value, "entry", location, issues);
            var count = GetRequiredInteger(
                value, "recordCount", 0, long.MaxValue, location, issues);
            var length = GetRequiredInteger(
                value, "uncompressedByteLength", 0, long.MaxValue, location, issues);
            var sha = GetRequiredString(value, "sha256", location, issues);
            if (entry != null && entry != expectedEntry)
                InvalidValue(issues, location + ".entry", expectedEntry);
            if (count.HasValue && count.Value > maximumRecords)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.RecordLimitExceeded,
                    location + ".recordCount",
                    maximumRecords.ToString(CultureInfo.InvariantCulture),
                    count.Value.ToString(CultureInfo.InvariantCulture)));
            }
            if (length.HasValue && length.Value > maximumLength)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.EntryTooLarge,
                    location + ".uncompressedByteLength",
                    maximumLength.ToString(CultureInfo.InvariantCulture),
                    length.Value.ToString(CultureInfo.InvariantCulture)));
            }
            if (sha != null && !ArchiveV1Format.IsLowercaseSha256(sha))
                InvalidValue(issues, location + ".sha256", "lowercase SHA-256");
            return entry == null || count == null || length == null || sha == null
                ? null
                : new ArchiveV1DataSetDescriptor(entry, count.Value, length.Value, sha);
        }

        static IReadOnlyList<ArchiveV1MetadataRow> ParseMetadata(
            byte[] bytes,
            string sourceVersion,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (HasBom(bytes))
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.ByteOrderMarkNotAllowed,
                    ArchiveV1Format.MetadataEntryName));
                return null;
            }
            if (!ArchiveV1Format.IsValidUtf8(bytes))
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.InvalidUtf8,
                    ArchiveV1Format.MetadataEntryName));
                return null;
            }
            try
            {
                using var document = JsonDocument.Parse(bytes, ArchiveV1Format.StrictDocumentOptions);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.InvalidPropertyType,
                        ArchiveV1Format.MetadataEntryName,
                        "array"));
                    return null;
                }

                var rows = new List<ArchiveV1MetadataRow>();
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    var location = $"metadata.json[{rows.Count}]";
                    if (!ValidateObject(element, new[] { "key", "value" }, location, issues))
                    {
                        rows.Add(null);
                        continue;
                    }
                    var key = GetRequiredString(element, "key", location, issues);
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
                        issues.Add(new ArchiveV1ValidationIssue(
                            ArchiveV1IssueCode.RecordLimitExceeded,
                            ArchiveV1Format.MetadataEntryName));
                        break;
                    }
                }
                foreach (var issue in ArchiveV1RecordValidator.ValidateMetadata(rows, sourceVersion))
                    issues.Add(issue);
                return rows;
            }
            catch (JsonException)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.MalformedJson,
                    ArchiveV1Format.MetadataEntryName));
                return null;
            }
        }

        static async Task<long?> ProcessPagesAsync(
            ZipArchiveEntry entry,
            ArchiveV1DataSetDescriptor descriptor,
            IArchiveV1RecordSink sink,
            ICollection<ArchiveV1ValidationIssue> issues,
            CancellationToken cancellationToken)
        {
            long length = 0;
            long count = 0;
            var sawAnyByte = false;
            var endedWithLf = false;
            var firstBytes = new List<byte>(3);
            var rowids = new HashSet<int>();
            var uuids = new HashSet<Guid>();
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
                    sawAnyByte = true;
                    length += read;
                    if (length > ArchiveV1Format.MaximumPagesLength)
                    {
                        issues.Add(new ArchiveV1ValidationIssue(
                            ArchiveV1IssueCode.EntryTooLarge,
                            ArchiveV1Format.PagesEntryName));
                        return null;
                    }
                    hash.AppendData(buffer, 0, read);
                    for (var index = 0; index < read; index++)
                    {
                        var current = buffer[index];
                        if (firstBytes.Count < 3)
                            firstBytes.Add(current);
                        if (current == '\r')
                        {
                            issues.Add(new ArchiveV1ValidationIssue(
                                ArchiveV1IssueCode.InvalidNdjsonFraming,
                                $"pages.ndjson:{count + 1}",
                                "LF without literal CR"));
                            return null;
                        }
                        if (current == '\n')
                        {
                            endedWithLf = true;
                            if (line.Length == 0)
                            {
                                issues.Add(new ArchiveV1ValidationIssue(
                                    ArchiveV1IssueCode.InvalidNdjsonFraming,
                                    $"pages.ndjson:{count + 1}",
                                    "non-empty JSON record"));
                                return null;
                            }
                            count++;
                            if (count > ArchiveV1Format.MaximumPageRecords)
                            {
                                issues.Add(new ArchiveV1ValidationIssue(
                                    ArchiveV1IssueCode.RecordLimitExceeded,
                                    ArchiveV1Format.PagesEntryName));
                                return null;
                            }
                            var row = ParsePage(line.ToArray(), count, issues);
                            if (row != null)
                            {
                                if (!rowids.Add(row.Rowid))
                                {
                                    issues.Add(new ArchiveV1ValidationIssue(
                                        ArchiveV1IssueCode.DuplicatePageRowId,
                                        $"pages.ndjson:{count}.rowid"));
                                }
                                if (Guid.TryParseExact(row.Uuid, "D", out var uuid) &&
                                    !uuids.Add(uuid))
                                {
                                    issues.Add(new ArchiveV1ValidationIssue(
                                        ArchiveV1IssueCode.DuplicatePageUuid,
                                        $"pages.ndjson:{count}.uuid"));
                                }
                                if (sink != null && issues.Count == 0)
                                {
                                    await sink.WritePageAsync(row, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                            }
                            line.SetLength(0);
                        }
                        else
                        {
                            endedWithLf = false;
                            line.WriteByte(current);
                            if (line.Length > ArchiveV1Format.MaximumPageRecordLength)
                            {
                                issues.Add(new ArchiveV1ValidationIssue(
                                    ArchiveV1IssueCode.RecordTooLarge,
                                    $"pages.ndjson:{count + 1}"));
                                return null;
                            }
                        }
                    }
                }
            }
            catch (InvalidDataException)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.UnreadableEntry,
                    ArchiveV1Format.PagesEntryName));
                return null;
            }
            catch (NotSupportedException)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.UnreadableEntry,
                    ArchiveV1Format.PagesEntryName));
                return null;
            }

            if (firstBytes.Count == 3 && firstBytes[0] == 0xef &&
                firstBytes[1] == 0xbb && firstBytes[2] == 0xbf)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.ByteOrderMarkNotAllowed,
                    ArchiveV1Format.PagesEntryName));
            }
            if (sawAnyByte && !endedWithLf)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.InvalidNdjsonFraming,
                    $"pages.ndjson:{count + 1}",
                    "final LF"));
            }

            if (CompressionRatio(length, entry.CompressedLength) >
                ArchiveV1Format.MaximumCompressionRatio)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.CompressionRatioExceeded,
                    ArchiveV1Format.PagesEntryName));
            }

            ValidateDescriptor(
                descriptor,
                length,
                count,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                issues);
            return length;
        }

        static ArchiveV1PageRow ParsePage(
            byte[] bytes,
            long recordNumber,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            var location = $"pages.ndjson:{recordNumber}";
            if (!ArchiveV1Format.IsValidUtf8(bytes))
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.InvalidUtf8,
                    location));
                return null;
            }
            try
            {
                using var document = JsonDocument.Parse(bytes, ArchiveV1Format.StrictDocumentOptions);
                var root = document.RootElement;
                if (!ValidateObject(
                    root,
                    new[]
                    {
                        "rowid", "uuid", "name", "index", "tags", "contentType",
                        "createTime", "updateTime", "isErased", "text"
                    },
                    location,
                    issues))
                {
                    return null;
                }

                var rowid = GetRequiredInteger(root, "rowid", int.MinValue, int.MaxValue, location, issues);
                var uuid = GetRequiredString(root, "uuid", location, issues);
                var name = GetNullableString(root, "name", location, issues);
                var index = GetRequiredInteger(root, "index", int.MinValue, int.MaxValue, location, issues);
                var tags = GetNullableString(root, "tags", location, issues);
                var contentType = GetNullableString(root, "contentType", location, issues);
                var createTime = GetRequiredString(root, "createTime", location, issues);
                var updateTime = GetRequiredString(root, "updateTime", location, issues);
                var isErased = GetRequiredInteger(root, "isErased", int.MinValue, int.MaxValue, location, issues);
                var text = GetNullableString(root, "text", location, issues);
                if (rowid == null || uuid == null || index == null || createTime == null ||
                    updateTime == null || isErased == null)
                {
                    return null;
                }
                var row = new ArchiveV1PageRow(
                    (int)rowid.Value,
                    uuid,
                    name,
                    (int)index.Value,
                    tags,
                    contentType,
                    createTime,
                    updateTime,
                    (int)isErased.Value,
                    text);
                foreach (var issue in ArchiveV1RecordValidator.ValidatePage(row, location))
                    issues.Add(issue);
                return row;
            }
            catch (JsonException)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.MalformedJson,
                    location));
                return null;
            }
        }

        static bool ValidateObject(
            JsonElement value,
            IReadOnlyCollection<string> propertyNames,
            string location,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                InvalidType(issues, location, "object");
                return false;
            }
            var allowed = new HashSet<string>(propertyNames, StringComparer.Ordinal);
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!found.Add(property.Name))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.DuplicateProperty,
                        location + "." + property.Name));
                }
                if (!allowed.Contains(property.Name))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.UnknownProperty,
                        location + "." + property.Name));
                }
            }
            foreach (var required in allowed)
            {
                if (!found.Contains(required))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.MissingProperty,
                        location + "." + required));
                }
            }
            return true;
        }

        static string GetRequiredString(
            JsonElement parent,
            string propertyName,
            string location,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (!parent.TryGetProperty(propertyName, out var value))
                return null;
            if (value.ValueKind != JsonValueKind.String)
            {
                InvalidType(issues, location + "." + propertyName, "string");
                return null;
            }
            return value.GetString();
        }

        static string GetNullableString(
            JsonElement parent,
            string propertyName,
            string location,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (!parent.TryGetProperty(propertyName, out var value))
                return null;
            if (value.ValueKind == JsonValueKind.Null)
                return null;
            if (value.ValueKind != JsonValueKind.String)
            {
                InvalidType(issues, location + "." + propertyName, "string or null");
                return null;
            }
            return value.GetString();
        }

        static long? GetRequiredInteger(
            JsonElement parent,
            string propertyName,
            long minimum,
            long maximum,
            string location,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (!parent.TryGetProperty(propertyName, out var value))
                return null;
            if (!ArchiveV1Format.IsDecimalInteger(value, minimum, maximum))
            {
                InvalidValue(
                    issues,
                    location + "." + propertyName,
                    $"decimal integer from {minimum} through {maximum}");
                return null;
            }
            return value.GetInt64();
        }

        static void ValidateDescriptor(
            ArchiveV1DataSetDescriptor descriptor,
            long actualLength,
            long actualCount,
            string actualSha,
            ICollection<ArchiveV1ValidationIssue> issues)
        {
            if (descriptor.UncompressedByteLength != actualLength)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.ByteLengthMismatch,
                    descriptor.Entry,
                    descriptor.UncompressedByteLength.ToString(CultureInfo.InvariantCulture),
                    actualLength.ToString(CultureInfo.InvariantCulture)));
            }
            if (descriptor.RecordCount != actualCount)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.RecordCountMismatch,
                    descriptor.Entry,
                    descriptor.RecordCount.ToString(CultureInfo.InvariantCulture),
                    actualCount.ToString(CultureInfo.InvariantCulture)));
            }
            if (descriptor.Sha256 != actualSha)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.ChecksumMismatch,
                    descriptor.Entry,
                    descriptor.Sha256,
                    actualSha));
            }
        }

        static long CountMetadataRecords(byte[] bytes)
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

        static bool HasBom(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
        }

        static string Sha256(byte[] value)
        {
            return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
        }

        static void InvalidType(
            ICollection<ArchiveV1ValidationIssue> issues,
            string location,
            string expected)
        {
            issues.Add(new ArchiveV1ValidationIssue(
                ArchiveV1IssueCode.InvalidPropertyType,
                location,
                expected));
        }

        static void InvalidValue(
            ICollection<ArchiveV1ValidationIssue> issues,
            string location,
            string expected)
        {
            issues.Add(new ArchiveV1ValidationIssue(
                ArchiveV1IssueCode.InvalidPropertyValue,
                location,
                expected));
        }

        static ArchiveV1ValidationReport Report(
            ArchiveV1Classification classification,
            IEnumerable<ArchiveV1ValidationIssue> issues)
        {
            return new ArchiveV1ValidationReport(classification, issues);
        }
    }
}
