using System;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Applies the CLI's implicit prefix-search convention to user input.
    /// </summary>
    internal sealed class CliSearchQueryNormalizer
    {
        /// <summary>
        /// Normalizes a CLI search query without applying persistence-specific escaping.
        /// </summary>
        /// <param name="query">The query entered on the command line.</param>
        /// <returns>The query passed to the application search boundary.</returns>
        internal string Normalize(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            var trimmedQuery = query.Trim();
            if (trimmedQuery.IndexOf(' ') >= 0)
                return query;

            if (HasBoundaryWildcard(trimmedQuery))
                return query;

            return query + "*";
        }

        static bool HasBoundaryWildcard(string query)
        {
            return IsWildcard(query[0]) || IsWildcard(query[query.Length - 1]);
        }

        static bool IsWildcard(char character)
        {
            return character == '*' || character == '?';
        }
    }
}
