using System;
using System.IO;

namespace MemoriaNote.Domain
{
    /// <summary>
    /// Identifies a notebook by its normalized database locator.
    /// </summary>
    /// <remarks>
    /// A notebook identifier is currently derived from an absolute file path. It is therefore not a
    /// persistent identifier across file moves. Windows paths are compared without regard to case;
    /// paths on other operating systems are compared using ordinal case-sensitive semantics.
    /// </remarks>
    public sealed class NotebookId : IEquatable<NotebookId>
    {
        static readonly StringComparer LocatorComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        NotebookId(string locator)
        {
            Locator = locator;
        }

        internal string Locator { get; }

        /// <summary>
        /// Creates a notebook identifier from a file-backed SQLite data source.
        /// </summary>
        /// <param name="databasePath">The relative or absolute path of the notebook database.</param>
        /// <returns>An identifier containing the normalized absolute path.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="databasePath"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="databasePath"/> is empty, whitespace, or the SQLite in-memory
        /// locator.
        /// </exception>
        public static NotebookId FromDatabasePath(string databasePath)
        {
            if (databasePath == null)
                throw new ArgumentNullException(nameof(databasePath));
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("The data source path cannot be empty.", nameof(databasePath));
            if (string.Equals(databasePath, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The data source must be a file-backed SQLite locator.",
                    nameof(databasePath));
            }

            return new NotebookId(Path.GetFullPath(databasePath));
        }

        /// <inheritdoc/>
        public bool Equals(NotebookId other)
        {
            return other != null && LocatorComparer.Equals(Locator, other.Locator);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as NotebookId);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return LocatorComparer.GetHashCode(Locator);
        }

        /// <summary>
        /// Determines whether two notebook identifiers have the same normalized locator.
        /// </summary>
        /// <param name="left">The first notebook identifier.</param>
        /// <param name="right">The second notebook identifier.</param>
        /// <returns>True when both identifiers represent the same notebook locator.</returns>
        public static bool operator ==(NotebookId left, NotebookId right)
        {
            return ReferenceEquals(left, right) || left?.Equals(right) == true;
        }

        /// <summary>
        /// Determines whether two notebook identifiers have different normalized locators.
        /// </summary>
        /// <param name="left">The first notebook identifier.</param>
        /// <param name="right">The second notebook identifier.</param>
        /// <returns>True when the identifiers represent different notebook locators.</returns>
        public static bool operator !=(NotebookId left, NotebookId right)
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
