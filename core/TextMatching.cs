using System;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// This class represents a text matching utility to create patterns for SQL LIKE queries based on input keywords and provide helpful functions to manipulate and use the patterns.
    /// </summary>
    public class TextMatching
    {
        // Constructor to initialize TextMatching object with pattern and matching type
        TextMatching(string pattern, MatchingType type)
        {
            Pattern = pattern;
            MatchingType = type;
        }

        /// <summary>
        /// Factory method to create a TextMatching object from a keyword
        /// </summary>
        /// <param name="keyword"></param>
        /// <returns></returns>
        public static TextMatching Create(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new TextMatching("", MatchingType.None);

            var pattern = keyword.Trim();
            pattern = EscapeSingleQuote(pattern);
            pattern = EscapeDoubleQuote(pattern);
            pattern = EscapePercent(pattern);
            pattern = EscapeUnderScore(pattern);
            pattern = ReplaceGlobToLike(pattern);

            if (keyword.Contains("*") || keyword.Contains("?"))
                return new TextMatching(pattern, MatchingType.Partial);
            else
                return new TextMatching(pattern, MatchingType.Exact);
        }

        /// <summary>
        /// Generates a WHERE clause condition for SQL queries based on the pattern and column
        /// </summary>
        /// <param name="column"></param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentException"></exception>
        public string Where(string column = null)
        {
            if (this.MatchingType == MatchingType.None)
                return "";
            
            if (string.IsNullOrWhiteSpace(column))
                return "";
            if (column.Contains('\''))
                throw new System.ArgumentException(nameof(column));        

            return $"WHERE {column} LIKE '{this.Pattern}' ESCAPE '\\' ";
        }

        // Static helper methods to escape special characters in the keyword
        static string EscapeSingleQuote(string keyword) => keyword.Replace("\'", "\'\'");
        static string EscapeDoubleQuote(string keyword) => keyword.Replace("\"", "\"\"");
        static string EscapePercent(string keyword) => keyword.Replace("%", "\\%");
        static string EscapeUnderScore(string keyword) => keyword.Replace("_", "\\_");
        static string ReplaceGlobToLike(string keyword) => keyword.Replace("*", "%").Replace("?", "_");

        /// <summary>
        /// Properties to access pattern and matching type of the TextMatching object
        /// </summary>
        public string Pattern { get; private set; }
        /// <summary>
        /// Properties to access pattern and matching type of the TextMatching object
        /// </summary>
        public MatchingType MatchingType { get; private set; }

        /// <summary>
        /// Checks if the pattern has a prefix wildcard for matching
        /// </summary>
        public bool IsPrefixMatch
        {
            get 
            {
                var last = Pattern.LastOrDefault();
                return last == '%' || last == '_';
            }
        }

        /// <summary>
        /// Checks if the pattern has a suffix wildcard for matching
        /// </summary>
        public bool IsSuffixMatch
        {
            get
            {
                var first = Pattern.FirstOrDefault();
                return first == '%' || first == '_';
            }
        }

        /// <summary>
        /// Checks if the pattern is a sentence by looking for spaces
        /// </summary>
        public bool IsSentence => Pattern.Any(c => c == ' ');
    }

    /// <summary>
    /// Enum to represent the type of matching for a TextMatching object
    /// </summary>
    public enum MatchingType
    {
        None,
        Partial,
        Exact
    }
}
