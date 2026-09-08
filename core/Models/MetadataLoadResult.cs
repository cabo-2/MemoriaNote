using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// Identifies a metadata value that could not be interpreted normally.
    /// </summary>
    public enum MetadataLoadIssueKind
    {
        /// <summary>
        /// A required metadata key was not stored.
        /// </summary>
        MissingKey,

        /// <summary>
        /// The stored read-only value was not a Boolean.
        /// </summary>
        InvalidBoolean,

        /// <summary>
        /// The stored creation time did not use the supported 14-character format.
        /// </summary>
        InvalidCreateTime
    }

    /// <summary>
    /// Describes one classifiable problem found while loading metadata.
    /// </summary>
    public sealed class MetadataLoadIssue
    {
        /// <summary>
        /// Initializes a new metadata load issue.
        /// </summary>
        /// <param name="kind">The category of the problem.</param>
        /// <param name="key">The affected metadata key.</param>
        /// <param name="value">The stored value, when one was present.</param>
        public MetadataLoadIssue(MetadataLoadIssueKind kind, string key, string value)
        {
            Kind = kind;
            Key = key;
            Value = value;
        }

        /// <summary>
        /// Gets the category of the problem.
        /// </summary>
        public MetadataLoadIssueKind Kind { get; }

        /// <summary>
        /// Gets the affected metadata key.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets the stored value, or null when the key was absent.
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// Contains a metadata snapshot and any non-fatal parsing issues found while loading it.
    /// </summary>
    public sealed class MetadataLoadResult
    {
        /// <summary>
        /// Initializes a new metadata load result.
        /// </summary>
        /// <param name="metadata">The materialized metadata snapshot.</param>
        /// <param name="issues">The issues found while materializing the snapshot.</param>
        public MetadataLoadResult(
            NoteMetadata metadata,
            IEnumerable<MetadataLoadIssue> issues)
        {
            Metadata = metadata;
            Issues = issues.ToList().AsReadOnly();
        }

        /// <summary>
        /// Gets the materialized snapshot.
        /// </summary>
        public NoteMetadata Metadata { get; }

        /// <summary>
        /// Gets the classifiable problems found while loading values.
        /// </summary>
        public IReadOnlyList<MetadataLoadIssue> Issues { get; }

        /// <summary>
        /// Gets a value indicating whether any load issues were found.
        /// </summary>
        public bool HasIssues => Issues.Count > 0;
    }
}
