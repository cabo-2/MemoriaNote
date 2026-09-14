namespace MemoriaNote
{
    /// <summary>
    /// Represents a search value escaped for SQLite LIKE and FTS queries.
    /// </summary>
    internal sealed class SqliteSearchPattern
    {
        SqliteSearchPattern(string pattern, SqliteSearchMatchType matchingType)
        {
            Pattern = pattern;
            MatchingType = matchingType;
        }

        /// <summary>
        /// Gets the escaped SQLite pattern.
        /// </summary>
        internal string Pattern { get; }

        /// <summary>
        /// Gets the matching behavior selected by the source query.
        /// </summary>
        internal SqliteSearchMatchType MatchingType { get; }

        /// <summary>
        /// Creates a SQLite pattern from the application search value.
        /// </summary>
        /// <param name="searchEntry">The unescaped application search value.</param>
        /// <returns>The escaped SQLite search pattern.</returns>
        internal static SqliteSearchPattern Create(string searchEntry)
        {
            if (string.IsNullOrWhiteSpace(searchEntry))
                return new SqliteSearchPattern(
                    string.Empty,
                    SqliteSearchMatchType.None);

            var pattern = searchEntry.Trim()
                .Replace("\"", "\"\"")
                .Replace("%", "\\%")
                .Replace("_", "\\_")
                .Replace("*", "%")
                .Replace("?", "_");
            var matchingType = searchEntry.Contains("*") || searchEntry.Contains("?")
                ? SqliteSearchMatchType.Partial
                : SqliteSearchMatchType.Exact;
            return new SqliteSearchPattern(pattern, matchingType);
        }
    }

    /// <summary>
    /// Identifies how an escaped SQLite search pattern is matched.
    /// </summary>
    internal enum SqliteSearchMatchType
    {
        None,
        Partial,
        Exact
    }
}
