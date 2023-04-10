using System;
using System.Linq;

namespace MemoriaNote
{
    public class TextMatching
    {
        TextMatching(string pattern, MatchingType type)
        {
            Pattern = pattern;
            MatchingType = type;
        }

        public static TextMatching Create(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new TextMatching("", MatchingType.None);

            var pattern = EscapeSingleQuote(keyword);
            pattern = EscapeDoubleQuote(pattern);
            pattern = EscapePercent(pattern);
            pattern = EscapeUnderScore(pattern);
            pattern = ReplaceGlobToLike(pattern);

            if (keyword.Contains("*") || keyword.Contains("?"))
                return new TextMatching(pattern, MatchingType.Partial);
            else
                return new TextMatching(pattern, MatchingType.Exact);
        }

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

        static string EscapeSingleQuote(string keyword) => keyword.Replace("\'", "\'\'");
        static string EscapeDoubleQuote(string keyword) => keyword.Replace("\"", "\"\"");
        static string EscapePercent(string keyword) => keyword.Replace("%", "\\%");
        static string EscapeUnderScore(string keyword) => keyword.Replace("_", "\\_");
        static string ReplaceGlobToLike(string keyword) => keyword.Replace("*", "%").Replace("?", "_");

        public string Pattern { get; private set; }
        public MatchingType MatchingType { get; private set; }
    }

    public enum MatchingType
    {
        None,
        Partial,
        Exact
    }
}
