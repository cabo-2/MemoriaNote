using System;

namespace MemoriaNote
{
    /// <summary>
    /// Identifies a page using the UUID stored by the note database.
    /// </summary>
    public sealed class PageId : IEquatable<PageId>
    {
        PageId(Guid value)
        {
            Value = value;
        }

        /// <summary>
        /// Gets the page UUID as a <see cref="Guid"/>.
        /// </summary>
        public Guid Value { get; }

        /// <summary>
        /// Creates a page identifier from a non-empty <see cref="Guid"/>.
        /// </summary>
        /// <param name="value">The page UUID.</param>
        /// <returns>A typed page identifier.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="value"/> is <see cref="Guid.Empty"/>.
        /// </exception>
        public static PageId FromGuid(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("The page identifier cannot be empty.", nameof(value));

            return new PageId(value);
        }

        internal static PageId FromUuid(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            return FromGuid(Guid.Parse(value));
        }

        internal string ToUuid()
        {
            return Value.ToString("D");
        }

        /// <inheritdoc/>
        public bool Equals(PageId other)
        {
            return other != null && Value == other.Value;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as PageId);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>
        /// Determines whether two page identifiers contain the same UUID.
        /// </summary>
        /// <param name="left">The first page identifier.</param>
        /// <param name="right">The second page identifier.</param>
        /// <returns>True when both identifiers contain the same UUID.</returns>
        public static bool operator ==(PageId left, PageId right)
        {
            return ReferenceEquals(left, right) || left?.Equals(right) == true;
        }

        /// <summary>
        /// Determines whether two page identifiers contain different UUIDs.
        /// </summary>
        /// <param name="left">The first page identifier.</param>
        /// <param name="right">The second page identifier.</param>
        /// <returns>True when the identifiers contain different UUIDs.</returns>
        public static bool operator !=(PageId left, PageId right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToUuid();
        }
    }
}
