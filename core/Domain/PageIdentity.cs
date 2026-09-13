using System;

namespace MemoriaNote
{
    /// <summary>
    /// Defines the identity contract for page entities.
    /// </summary>
    static class PageIdentity
    {
        /// <summary>
        /// Determines whether two pages have the same non-empty page identifier.
        /// </summary>
        /// <param name="left">The first entity.</param>
        /// <param name="right">The second entity.</param>
        /// <returns>True when the entities are the same instance or have the same non-empty ID.</returns>
        internal static bool Equals(Page left, Page right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            if (left.Guid == Guid.Empty || right.Guid == Guid.Empty)
                return false;

            return PageId.FromGuid(left.Guid) == PageId.FromGuid(right.Guid);
        }

        /// <summary>
        /// Gets the hash code of a page's non-empty identifier.
        /// </summary>
        /// <param name="value">The entity whose identifier is hashed.</param>
        /// <returns>The hash code of the entity's page identifier.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="value"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the entity has not been assigned a page identifier.
        /// </exception>
        internal static int GetHashCode(Page value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (value.Guid == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "A page must have an identifier before it can be hashed.");
            }

            return PageId.FromGuid(value.Guid).GetHashCode();
        }
    }
}
