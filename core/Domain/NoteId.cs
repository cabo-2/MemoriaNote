using System;
using System.IO;

namespace MemoriaNote
{
    /// <summary>
    /// Identifies a note by its normalized database locator.
    /// </summary>
    /// <remarks>
    /// A note identifier is currently derived from an absolute file path. It is therefore not a
    /// persistent identifier across file moves. Windows paths are compared without regard to case;
    /// paths on other operating systems are compared using ordinal case-sensitive semantics.
    /// </remarks>
    public sealed class NoteId : IEquatable<NoteId>
    {
        static readonly StringComparer LocatorComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        NoteId(string locator)
        {
            Locator = locator;
        }

        internal string Locator { get; }

        /// <summary>
        /// Creates a note identifier from a file-backed SQLite data source.
        /// </summary>
        /// <param name="dataSource">The relative or absolute path of the note database.</param>
        /// <returns>An identifier containing the normalized absolute path.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="dataSource"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="dataSource"/> is empty, whitespace, or the SQLite in-memory
        /// locator.
        /// </exception>
        public static NoteId FromDataSource(string dataSource)
        {
            if (dataSource == null)
                throw new ArgumentNullException(nameof(dataSource));
            if (string.IsNullOrWhiteSpace(dataSource))
                throw new ArgumentException("The data source path cannot be empty.", nameof(dataSource));
            if (string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The data source must be a file-backed SQLite locator.",
                    nameof(dataSource));
            }

            return new NoteId(Path.GetFullPath(dataSource));
        }

        /// <inheritdoc/>
        public bool Equals(NoteId other)
        {
            return other != null && LocatorComparer.Equals(Locator, other.Locator);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as NoteId);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return LocatorComparer.GetHashCode(Locator);
        }

        /// <summary>
        /// Determines whether two note identifiers have the same normalized locator.
        /// </summary>
        /// <param name="left">The first note identifier.</param>
        /// <param name="right">The second note identifier.</param>
        /// <returns>True when both identifiers represent the same note locator.</returns>
        public static bool operator ==(NoteId left, NoteId right)
        {
            return ReferenceEquals(left, right) || left?.Equals(right) == true;
        }

        /// <summary>
        /// Determines whether two note identifiers have different normalized locators.
        /// </summary>
        /// <param name="left">The first note identifier.</param>
        /// <param name="right">The second note identifier.</param>
        /// <returns>True when the identifiers represent different note locators.</returns>
        public static bool operator !=(NoteId left, NoteId right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Locator;
        }
    }
}
