using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MemoriaNote;

namespace MemoriaNote.Models
{
    /// <summary>
    /// Represents a database-independent snapshot of persisted notebook metadata.
    /// </summary>
    public sealed class NotebookMetadata : IEquatable<NotebookMetadata>
    {
        readonly IReadOnlyDictionary<string, string> _storedValues;

        internal NotebookMetadata(
            string databasePath,
            string name,
            string title,
            string version,
            string description,
            string author,
            bool readOnly,
            string tag,
            DateTime createTime,
            IReadOnlyDictionary<string, string> storedValues)
        {
            DatabasePath = databasePath;
            Name = name;
            Title = title;
            Version = version;
            Description = description;
            Author = author;
            ReadOnly = readOnly;
            Tag = tag;
            CreateTime = createTime;
            _storedValues = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(storedValues));
        }

        /// <summary>
        /// Gets the normalized path of the notebook database that produced this snapshot.
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>
        /// Gets the notebook name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the notebook title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the notebook format version.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the optional notebook description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the optional notebook author.
        /// </summary>
        public string Author { get; }

        /// <summary>
        /// Gets a value indicating whether the notebook is read-only.
        /// </summary>
        public bool ReadOnly { get; }

        /// <summary>
        /// Gets the optional notebook tag.
        /// </summary>
        public string Tag { get; }

        /// <summary>
        /// Gets the parsed creation time, or the default value when none was stored or parsing failed.
        /// </summary>
        public DateTime CreateTime { get; }

        internal IReadOnlyDictionary<string, string> StoredValues => _storedValues;

        /// <summary>
        /// Creates an independent copy of this in-memory snapshot.
        /// </summary>
        /// <returns>A snapshot containing the same values.</returns>
        public NotebookMetadata Clone()
        {
            return new NotebookMetadata(
                DatabasePath,
                Name,
                Title,
                Version,
                Description,
                Author,
                ReadOnly,
                Tag,
                CreateTime,
                _storedValues);
        }

        /// <inheritdoc/>
        public bool Equals(NotebookMetadata other)
        {
            return other != null &&
                string.Equals(DatabasePath, other.DatabasePath, StringComparison.Ordinal) &&
                string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                string.Equals(Title, other.Title, StringComparison.Ordinal) &&
                string.Equals(Version, other.Version, StringComparison.Ordinal) &&
                string.Equals(Description, other.Description, StringComparison.Ordinal) &&
                string.Equals(Author, other.Author, StringComparison.Ordinal) &&
                ReadOnly == other.ReadOnly &&
                string.Equals(Tag, other.Tag, StringComparison.Ordinal) &&
                CreateTime == other.CreateTime;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as NotebookMetadata);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(DatabasePath, StringComparer.Ordinal);
            hash.Add(Name, StringComparer.Ordinal);
            hash.Add(Title, StringComparer.Ordinal);
            hash.Add(Version, StringComparer.Ordinal);
            hash.Add(Description, StringComparer.Ordinal);
            hash.Add(Author, StringComparer.Ordinal);
            hash.Add(ReadOnly);
            hash.Add(Tag, StringComparer.Ordinal);
            hash.Add(CreateTime);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (Name == null)
                return base.ToString();

            return Tag == null
                ? $"{Name}:{CreateTime.ToDateString()}"
                : $"{Name}:{Tag}";
        }
    }
}
