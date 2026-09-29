using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Archive
{
    /// <summary>Classifies a ZIP input before archive payloads are interpreted.</summary>
    public enum ArchiveV1Classification
    {
        /// <summary>The ZIP contains an archive v1 manifest.</summary>
        ArchiveV1,

        /// <summary>The ZIP entry layout resembles the unsupported legacy backup format.</summary>
        LegacyArchiveCandidate,

        /// <summary>The input is a readable ZIP that is not a recognized MemoriaNote archive.</summary>
        UnrecognizedZip,

        /// <summary>The input cannot be read as a ZIP container.</summary>
        MalformedZip
    }

    /// <summary>Identifies a stable archive v1 validation failure.</summary>
    public enum ArchiveV1IssueCode
    {
        /// <summary>The physical ZIP file exceeds the archive limit.</summary>
        ArchiveTooLarge,

        /// <summary>The input cannot be read as a ZIP container.</summary>
        MalformedZip,

        /// <summary>The input resembles the unsupported legacy backup format.</summary>
        LegacyArchiveUnsupported,

        /// <summary>The ZIP does not contain a recognized archive layout.</summary>
        UnrecognizedZip,

        /// <summary>The ZIP contains an incorrect number of entries.</summary>
        InvalidEntryCount,

        /// <summary>A required ZIP entry is absent.</summary>
        MissingEntry,

        /// <summary>A ZIP entry is not part of archive v1.</summary>
        UnexpectedEntry,

        /// <summary>A ZIP entry name occurs more than once.</summary>
        DuplicateEntry,

        /// <summary>A directory or nested ZIP entry was found.</summary>
        InvalidEntryPath,

        /// <summary>A ZIP entry cannot be decompressed by the reader.</summary>
        UnreadableEntry,

        /// <summary>A declared or actual entry length exceeds its limit.</summary>
        EntryTooLarge,

        /// <summary>An entry or archive exceeds the permitted compression ratio.</summary>
        CompressionRatioExceeded,

        /// <summary>A UTF-8 byte-order mark is present.</summary>
        ByteOrderMarkNotAllowed,

        /// <summary>An entry contains invalid UTF-8.</summary>
        InvalidUtf8,

        /// <summary>An entry is not well-formed JSON or UTF-8.</summary>
        MalformedJson,

        /// <summary>An NDJSON payload does not use the required line framing.</summary>
        InvalidNdjsonFraming,

        /// <summary>A JSON object repeats a property name.</summary>
        DuplicateProperty,

        /// <summary>A JSON object contains an unknown property.</summary>
        UnknownProperty,

        /// <summary>A required JSON property is absent.</summary>
        MissingProperty,

        /// <summary>A JSON property has the wrong JSON type.</summary>
        InvalidPropertyType,

        /// <summary>A JSON property violates a lexical or semantic rule.</summary>
        InvalidPropertyValue,

        /// <summary>The manifest format identifier is not archive v1.</summary>
        InvalidFormatIdentifier,

        /// <summary>The manifest version tuple is unsupported.</summary>
        UnsupportedVersion,

        /// <summary>A data set contains too many records.</summary>
        RecordLimitExceeded,

        /// <summary>One page record exceeds the per-record limit.</summary>
        RecordTooLarge,

        /// <summary>A manifest record count differs from the payload.</summary>
        RecordCountMismatch,

        /// <summary>A manifest byte length differs from the payload.</summary>
        ByteLengthMismatch,

        /// <summary>A manifest checksum differs from the payload.</summary>
        ChecksumMismatch,

        /// <summary>A metadata key occurs more than once.</summary>
        DuplicateMetadataKey,

        /// <summary>A required metadata row is absent.</summary>
        MissingMetadataKey,

        /// <summary>The metadata Version value disagrees with the manifest.</summary>
        MetadataVersionMismatch,

        /// <summary>A page row identifier occurs more than once.</summary>
        DuplicatePageRowId,

        /// <summary>A page UUID occurs more than once.</summary>
        DuplicatePageUuid,

        /// <summary>Writer input is not in the canonical archive order.</summary>
        NonCanonicalOrder
    }

    /// <summary>Describes one archive validation problem without presentation-specific wording.</summary>
    public sealed class ArchiveV1ValidationIssue
    {
        /// <summary>Initializes an archive validation issue.</summary>
        /// <param name="code">The stable issue code.</param>
        /// <param name="location">The archive location associated with the issue.</param>
        /// <param name="expected">A safe summary of the expected value.</param>
        /// <param name="actual">A safe summary of the actual value.</param>
        public ArchiveV1ValidationIssue(
            ArchiveV1IssueCode code,
            string location,
            string expected = null,
            string actual = null)
        {
            Code = code;
            Location = location ?? throw new ArgumentNullException(nameof(location));
            Expected = expected;
            Actual = actual;
        }

        /// <summary>Gets the stable issue code.</summary>
        public ArchiveV1IssueCode Code { get; }

        /// <summary>Gets the entry and field location.</summary>
        public string Location { get; }

        /// <summary>Gets a safe summary of the expected value, when useful.</summary>
        public string Expected { get; }

        /// <summary>Gets a safe summary of the actual value, when useful.</summary>
        public string Actual { get; }
    }

    /// <summary>Contains the classification and validation issues for one archive input.</summary>
    public sealed class ArchiveV1ValidationReport
    {
        internal ArchiveV1ValidationReport(
            ArchiveV1Classification classification,
            IEnumerable<ArchiveV1ValidationIssue> issues)
        {
            Classification = classification;
            Issues = new ReadOnlyCollection<ArchiveV1ValidationIssue>(
                (issues ?? Enumerable.Empty<ArchiveV1ValidationIssue>()).ToList());
        }

        /// <summary>Gets the structural classification of the input.</summary>
        public ArchiveV1Classification Classification { get; }

        /// <summary>Gets the detected validation issues.</summary>
        public IReadOnlyList<ArchiveV1ValidationIssue> Issues { get; }

        /// <summary>Gets whether the input is a valid supported archive v1.</summary>
        public bool IsValid =>
            Classification == ArchiveV1Classification.ArchiveV1 && Issues.Count == 0;
    }

    /// <summary>Represents invalid source records supplied to an archive writer.</summary>
    public sealed class ArchiveV1ValidationException : Exception
    {
        /// <summary>Initializes an archive validation exception.</summary>
        /// <param name="report">The validation report that prevented writing.</param>
        public ArchiveV1ValidationException(ArchiveV1ValidationReport report)
            : base("Archive v1 validation failed.")
        {
            Report = report ?? throw new ArgumentNullException(nameof(report));
        }

        /// <summary>Gets the validation report that prevented writing.</summary>
        public ArchiveV1ValidationReport Report { get; }
    }

    /// <summary>Represents one raw Metadata table row in archive v1.</summary>
    public sealed class ArchiveV1MetadataRow
    {
        /// <summary>Initializes a raw metadata row.</summary>
        /// <param name="key">The stored metadata key.</param>
        /// <param name="value">The stored metadata value, including null.</param>
        public ArchiveV1MetadataRow(string key, string value)
        {
            Key = key;
            Value = value;
        }

        /// <summary>Gets the stored metadata key.</summary>
        public string Key { get; }

        /// <summary>Gets the stored metadata value.</summary>
        public string Value { get; }
    }

    /// <summary>Represents one raw Pages table row in archive v1.</summary>
    public sealed class ArchiveV1PageRow
    {
        /// <summary>Initializes a raw page row.</summary>
        public ArchiveV1PageRow(
            int rowid,
            string uuid,
            string name,
            int index,
            string tags,
            string contentType,
            string createTime,
            string updateTime,
            int isErased,
            string text)
        {
            Rowid = rowid;
            Uuid = uuid;
            Name = name;
            Index = index;
            Tags = tags;
            ContentType = contentType;
            CreateTime = createTime;
            UpdateTime = updateTime;
            IsErased = isErased;
            Text = text;
        }

        /// <summary>Gets the stored SQLite row identifier.</summary>
        public int Rowid { get; }

        /// <summary>Gets the stored UUID text.</summary>
        public string Uuid { get; }

        /// <summary>Gets the stored page name.</summary>
        public string Name { get; }

        /// <summary>Gets the stored one-based name index.</summary>
        public int Index { get; }

        /// <summary>Gets the stored tags JSON text.</summary>
        public string Tags { get; }

        /// <summary>Gets the stored content type.</summary>
        public string ContentType { get; }

        /// <summary>Gets the stored creation time text.</summary>
        public string CreateTime { get; }

        /// <summary>Gets the stored update time text.</summary>
        public string UpdateTime { get; }

        /// <summary>Gets the stored erase flag as zero or one.</summary>
        public int IsErased { get; }

        /// <summary>Gets the stored page body.</summary>
        public string Text { get; }
    }

    /// <summary>Describes the application that creates an archive.</summary>
    public sealed class ArchiveV1Creator
    {
        /// <summary>Initializes creator information.</summary>
        public ArchiveV1Creator(string application, string version)
        {
            Application = application;
            Version = version;
        }

        /// <summary>Gets the application name.</summary>
        public string Application { get; }

        /// <summary>Gets the application version.</summary>
        public string Version { get; }
    }

    /// <summary>Supplies raw records and creation information to the archive v1 writer.</summary>
    public sealed class ArchiveV1WriteRequest
    {
        /// <summary>Initializes an archive v1 write request.</summary>
        public ArchiveV1WriteRequest(
            string sourceNotebookFormatVersion,
            ArchiveV1Creator creator,
            DateTimeOffset createdAtUtc,
            IEnumerable<ArchiveV1MetadataRow> metadata,
            IAsyncEnumerable<ArchiveV1PageRow> pages)
        {
            SourceNotebookFormatVersion = sourceNotebookFormatVersion;
            Creator = creator ?? throw new ArgumentNullException(nameof(creator));
            CreatedAtUtc = createdAtUtc;
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Pages = pages ?? throw new ArgumentNullException(nameof(pages));
        }

        /// <summary>Gets the exact source Metadata.Version value.</summary>
        public string SourceNotebookFormatVersion { get; }

        /// <summary>Gets the archive creator information.</summary>
        public ArchiveV1Creator Creator { get; }

        /// <summary>Gets the archive creation time.</summary>
        public DateTimeOffset CreatedAtUtc { get; }

        /// <summary>Gets the raw metadata rows.</summary>
        public IEnumerable<ArchiveV1MetadataRow> Metadata { get; }

        /// <summary>Gets the raw page rows in ascending rowid order.</summary>
        public IAsyncEnumerable<ArchiveV1PageRow> Pages { get; }
    }

    /// <summary>Receives validated archive records without materializing all pages.</summary>
    public interface IArchiveV1RecordSink
    {
        /// <summary>Receives one validated metadata row.</summary>
        ValueTask WriteMetadataAsync(
            ArchiveV1MetadataRow row,
            CancellationToken cancellationToken);

        /// <summary>Receives one validated page row.</summary>
        ValueTask WritePageAsync(
            ArchiveV1PageRow row,
            CancellationToken cancellationToken);
    }
}
