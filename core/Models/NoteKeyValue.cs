using System.ComponentModel.DataAnnotations;

namespace MemoriaNote
{
    /// <summary>
    /// Represents one key-value row in the note metadata table.
    /// </summary>
    public class NoteKeyValue
    {
        /// <summary>
        /// Gets or sets the metadata key.
        /// </summary>
        [Key]
        public string Key { get; set; }

        /// <summary>
        /// Gets or sets the stored metadata value.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Gets the key used for the note name.
        /// </summary>
        public static string Name => nameof(Name);

        /// <summary>
        /// Gets the key used for the note title.
        /// </summary>
        public static string Title => nameof(Title);

        /// <summary>
        /// Gets the key used for the note format version.
        /// </summary>
        public static string Version => nameof(Version);

        /// <summary>
        /// Gets the key used for the read-only flag.
        /// </summary>
        public static string ReadOnly => nameof(ReadOnly);

        /// <summary>
        /// Gets the key used for the note description.
        /// </summary>
        public static string Description => nameof(Description);

        /// <summary>
        /// Gets the key used for the note author.
        /// </summary>
        public static string Author => nameof(Author);

        /// <summary>
        /// Gets the key used for the note tag.
        /// </summary>
        public static string Tag => nameof(Tag);

        /// <summary>
        /// Gets the key used for the note creation time.
        /// </summary>
        public static string CreateTime => nameof(CreateTime);
    }
}
