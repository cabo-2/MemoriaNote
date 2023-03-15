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
                return new TextMatching("", MatchingType.DoNotMatch);

            if (keyword.First() == '*')
            {
                if (keyword.Length > 1 && keyword.Last() == '*')
                    return new TextMatching(keyword.Replace("*", ""), MatchingType.Contains);
                else
                    return new TextMatching(keyword.Replace("*", ""), MatchingType.Backward);
            }
            else
            {
                if (keyword.Length > 1 && keyword.Last() == '*')
                    return new TextMatching(keyword.Replace("*", ""), MatchingType.Forward);
                else
                    return new TextMatching(keyword.Replace("*", ""), MatchingType.Perfect);
            }
        }

        public string GetWhereClause(string column = null)
        {
            if (string.IsNullOrWhiteSpace(column))
                return "";

            switch (MatchingType)
            {
                case MatchingType.Forward:
                    return "WHERE " + column + " LIKE '" + Pattern + "%' ";
                case MatchingType.Backward:
                    return "WHERE " + column + " LIKE '" + "%" + Pattern + "' ";
                case MatchingType.Contains:
                    return "WHERE " + column + " LIKE '" + "%" + Pattern + "%' ";
                case MatchingType.Perfect:
                    return "WHERE " + column + " LIKE '" + Pattern + "' ";
                case MatchingType.DoNotMatch:
                default:
                    return "";
            }
        }

        public string Pattern { get; private set; }
        public MatchingType MatchingType { get; private set; }
    }

    public enum MatchingType
    {
        DoNotMatch,
        Contains,
        Forward,
        Backward,
        Perfect
    }
}
