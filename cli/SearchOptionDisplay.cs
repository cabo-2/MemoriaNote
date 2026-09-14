namespace MemoriaNote.Cli
{
    /// <summary>Formats search options for the terminal status bar.</summary>
    internal static class SearchOptionDisplay
    {
        /// <summary>Formats a search range.</summary>
        /// <param name="range">The range to format.</param>
        /// <returns>The fixed-width display text.</returns>
        internal static string Format(SearchRangeType range)
        {
            return range == SearchRangeType.Notebook
                ? "A note   "
                : "All notes";
        }

        /// <summary>Formats a search method.</summary>
        /// <param name="method">The method to format.</param>
        /// <returns>The fixed-width display text.</returns>
        internal static string Format(SearchMethodType method)
        {
            return method == SearchMethodType.Heading
                ? "Heading  "
                : "Full text";
        }
    }
}
