using System.Linq;

namespace MemoriaNote
{
    public class TextMatching
    {
        public TextMatching(string pattern, MatchingType type)
        {
            Pattern = pattern;
            MatchingType = type;
        }

        public static TextMatching Create(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new TextMatching("", MatchingType.None);

            if (keyword.First() == '*')
            {
                if (keyword.Length > 1 && keyword.Last() == '*')
                    return new TextMatching(keyword.Replace("*", ""), MatchingType.Partial);
                else
                    return new TextMatching(keyword.Replace("*", ""), MatchingType.Suffix);
            }
            else
            {
                if (keyword.Length > 1 && keyword.Last() == '*')
                    return new TextMatching(keyword.Replace("*", ""), MatchingType.Prefix);
                else
                    return new TextMatching(keyword.Replace("*", ""), MatchingType.Exact);
            }
        }

        public string GetWhereClause(string column = null)
        {
            if (string.IsNullOrWhiteSpace(column))
                return "";

            switch (MatchingType)
            {
                case MatchingType.Prefix:
                    return "WHERE " + column + " LIKE '" + Pattern + "%' ";
                case MatchingType.Suffix:
                    return "WHERE " + column + " LIKE '" + "%" + Pattern + "' ";
                case MatchingType.Partial:
                    return "WHERE " + column + " LIKE '" + "%" + Pattern + "%' ";
                case MatchingType.Exact:
                    return "WHERE " + column + " LIKE '" + Pattern + "' ";
                case MatchingType.None:
                default:
                    return "";
            }
        }

        public string Pattern { get; private set; }
        public MatchingType MatchingType { get; private set; }
    }

    public enum MatchingType
    {
        None,
        Partial,
        Prefix,
        Suffix,
        Exact
    }
}
