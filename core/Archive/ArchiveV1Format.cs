using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MemoriaNote.Archive
{
    internal static class ArchiveV1Format
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal const string FormatIdentifier = "memoria-note-archive";
        internal const int ArchiveFormatVersion = 1;
        internal const int ContentModelVersion = 1;
        internal const string SupportedSourceNotebookFormatVersion = "1";
        internal const string ManifestEntryName = "manifest.json";
        internal const string MetadataEntryName = "metadata.json";
        internal const string PagesEntryName = "pages.ndjson";
        internal const long MaximumArchiveLength = 2147483648;
        internal const long MaximumManifestLength = 65536;
        internal const long MaximumMetadataLength = 16777216;
        internal const long MaximumPagesLength = 8589934592;
        internal const int MaximumMetadataRows = 65536;
        internal const int MaximumPageRecords = 1000000;
        internal const int MaximumPageRecordLength = 67108864;
        internal const double MaximumCompressionRatio = 1000;

        internal static readonly string[] EntryNames =
        {
            ManifestEntryName,
            MetadataEntryName,
            PagesEntryName
        };

        internal static bool IsJsonWhitespace(string value)
        {
            return value.All(character =>
                character == ' ' || character == '\t' ||
                character == '\r' || character == '\n');
        }

        internal static bool IsValidMetadataTime(string value)
        {
            return value != null &&
                DateTime.TryParseExact(
                    value,
                    "yyyyMMddhhmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _);
        }

        internal static bool IsValidPageTime(string value)
        {
            if (value == null)
                return false;

            var formats = new[]
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss.f",
                "yyyy-MM-dd HH:mm:ss.ff",
                "yyyy-MM-dd HH:mm:ss.fff",
                "yyyy-MM-dd HH:mm:ss.ffff",
                "yyyy-MM-dd HH:mm:ss.fffff",
                "yyyy-MM-dd HH:mm:ss.ffffff",
                "yyyy-MM-dd HH:mm:ss.fffffff"
            };
            return DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);
        }

        internal static bool IsValidCreatedAt(string value)
        {
            return value != null &&
                DateTimeOffset.TryParseExact(
                    value,
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out _);
        }

        internal static bool IsValidTags(string value)
        {
            if (value == null || value.Length == 0 || IsJsonWhitespace(value))
                return true;

            try
            {
                using var document = JsonDocument.Parse(value, StrictDocumentOptions);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        return false;
                    if (property.Value.ValueKind != JsonValueKind.String &&
                        property.Value.ValueKind != JsonValueKind.Null)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        internal static bool IsDecimalInteger(JsonElement value, long minimum, long maximum)
        {
            if (value.ValueKind != JsonValueKind.Number)
                return false;

            var text = value.GetRawText();
            if (text.Length == 0 || !text.All(character => character >= '0' && character <= '9'))
                return false;

            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
                parsed >= minimum && parsed <= maximum;
        }

        internal static bool IsLowercaseSha256(string value)
        {
            return value != null && value.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f'));
        }

        internal static int UnicodeScalarCount(string value)
        {
            return value == null ? 0 : value.EnumerateRunes().Count();
        }

        internal static bool IsValidUtf8(ReadOnlySpan<byte> value)
        {
            try
            {
                StrictUtf8.GetCharCount(value);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        internal static string FormatCreatedAt(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                CultureInfo.InvariantCulture);
        }

        internal static JsonDocumentOptions StrictDocumentOptions => new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        };
    }

    internal sealed class ArchiveV1DataSetDescriptor
    {
        internal ArchiveV1DataSetDescriptor(
            string entry,
            long recordCount,
            long uncompressedByteLength,
            string sha256)
        {
            Entry = entry;
            RecordCount = recordCount;
            UncompressedByteLength = uncompressedByteLength;
            Sha256 = sha256;
        }

        internal string Entry { get; }
        internal long RecordCount { get; }
        internal long UncompressedByteLength { get; }
        internal string Sha256 { get; }
    }

    internal sealed class ArchiveV1Manifest
    {
        internal ArchiveV1Manifest(
            string sourceNotebookFormatVersion,
            string application,
            string applicationVersion,
            string createdAtUtc,
            ArchiveV1DataSetDescriptor metadata,
            ArchiveV1DataSetDescriptor pages)
        {
            SourceNotebookFormatVersion = sourceNotebookFormatVersion;
            Application = application;
            ApplicationVersion = applicationVersion;
            CreatedAtUtc = createdAtUtc;
            Metadata = metadata;
            Pages = pages;
        }

        internal string SourceNotebookFormatVersion { get; }
        internal string Application { get; }
        internal string ApplicationVersion { get; }
        internal string CreatedAtUtc { get; }
        internal ArchiveV1DataSetDescriptor Metadata { get; }
        internal ArchiveV1DataSetDescriptor Pages { get; }
    }
}
