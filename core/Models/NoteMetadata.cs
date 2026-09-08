using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a database-independent snapshot of persisted note metadata.
    /// </summary>
    public sealed class NoteMetadata : IEquatable<NoteMetadata>
    {
        readonly IReadOnlyDictionary<string, string> _storedValues;

        internal NoteMetadata(
            string dataSource,
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
            DataSource = dataSource;
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
        /// Gets the normalized path of the note database that produced this snapshot.
        /// </summary>
        public string DataSource { get; }

        /// <summary>
        /// Gets the note name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the note title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the note format version.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the optional note description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the optional note author.
        /// </summary>
        public string Author { get; }

        /// <summary>
        /// Gets a value indicating whether the note is read-only.
        /// </summary>
        public bool ReadOnly { get; }

        /// <summary>
        /// Gets the optional note tag.
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
        public NoteMetadata Clone()
        {
            return new NoteMetadata(
                DataSource,
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
        public bool Equals(NoteMetadata other)
        {
            return other != null &&
                string.Equals(DataSource, other.DataSource, StringComparison.Ordinal) &&
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
        public override bool Equals(object obj) => Equals(obj as NoteMetadata);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(DataSource, StringComparer.Ordinal);
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
