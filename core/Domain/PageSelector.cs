using System;
using System.Linq;

namespace MemoriaNote.Domain
{
    /// <summary>
    /// Identifies a page by an exact name or by a normalized Page ID prefix.
    /// </summary>
    public sealed class PageSelector
    {
        PageSelector(string name, string pageIdPrefix)
        {
            Name = name;
            PageIdPrefix = pageIdPrefix;
        }

        /// <summary>Gets the exact page name, or null for an ID selector.</summary>
        public string Name { get; }

        /// <summary>
        /// Gets the lowercase, hyphen-free Page ID prefix, or null for a name selector.
        /// </summary>
        public string PageIdPrefix { get; }

        /// <summary>Gets whether this selector uses an exact page name.</summary>
        public bool IsName => Name != null;

        /// <summary>Creates an exact-name selector.</summary>
        /// <param name="name">The exact page name.</param>
        /// <returns>The selector.</returns>
        public static PageSelector FromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A page name is required.", nameof(name));

            return new PageSelector(name, null);
        }

        /// <summary>
        /// Attempts to create a selector from a complete UUID or a prefix of 4 to 32 hexadecimal
        /// characters.
        /// </summary>
        /// <param name="value">The complete UUID or hexadecimal prefix.</param>
        /// <param name="selector">The parsed selector when successful.</param>
        /// <returns>True when the value has a supported Page ID form.</returns>
        public static bool TryFromPageId(string value, out PageSelector selector)
        {
            selector = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized;
            if (Guid.TryParseExact(value, "D", out var pageId))
            {
                normalized = pageId.ToString("N");
            }
            else
            {
                normalized = value;
            }

            if (normalized.Length < 4 || normalized.Length > 32 ||
                normalized.Any(character => !Uri.IsHexDigit(character)))
            {
                return false;
            }

            selector = new PageSelector(null, normalized.ToLowerInvariant());
            return true;
        }
    }
}
