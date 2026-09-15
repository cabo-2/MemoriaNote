using System;

namespace MemoriaNote
{
    /// <summary>
    /// Describes the complete proposed metadata state for a notebook update.
    /// </summary>
    public sealed class NotebookMetadataUpdate
    {
        /// <summary>Initializes a proposed notebook metadata state.</summary>
        /// <param name="name">The proposed notebook name.</param>
        /// <param name="title">The proposed notebook title.</param>
        /// <param name="version">The proposed notebook format version.</param>
        /// <param name="description">The proposed notebook description.</param>
        /// <param name="author">The proposed notebook author.</param>
        /// <param name="readOnly">Whether the notebook should be read-only.</param>
        /// <param name="tag">The proposed notebook tag.</param>
        /// <param name="createTime">The proposed notebook creation time.</param>
        public NotebookMetadataUpdate(
            string name,
            string title,
            string version,
            string description,
            string author,
            bool readOnly,
            string tag,
            DateTime createTime)
        {
            Name = name;
            Title = title;
            Version = version;
            Description = description;
            Author = author;
            ReadOnly = readOnly;
            Tag = tag;
            CreateTime = createTime;
        }

        /// <summary>Gets the proposed notebook name.</summary>
        public string Name { get; }

        /// <summary>Gets the proposed notebook title.</summary>
        public string Title { get; }

        /// <summary>Gets the proposed notebook format version.</summary>
        public string Version { get; }

        /// <summary>Gets the proposed notebook description.</summary>
        public string Description { get; }

        /// <summary>Gets the proposed notebook author.</summary>
        public string Author { get; }

        /// <summary>Gets a value indicating whether the notebook should be read-only.</summary>
        public bool ReadOnly { get; }

        /// <summary>Gets the proposed notebook tag.</summary>
        public string Tag { get; }

        /// <summary>Gets the proposed notebook creation time.</summary>
        public DateTime CreateTime { get; }
    }
}
