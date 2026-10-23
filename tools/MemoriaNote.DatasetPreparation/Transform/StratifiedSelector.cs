namespace MemoriaNote.DatasetPreparation.Transform;

internal static class StratifiedSelector
{
    internal static IReadOnlyList<PreparedPage> Rank(IEnumerable<PreparedPage> pages)
    {
        var strata = pages
            .GroupBy(page => GetStratum(page.BodyBytes))
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                Stratum = group.Key,
                Pages = group.OrderBy(page => page.SelectionHash, StringComparer.Ordinal)
                    .ThenBy(page => page.SourcePath, StringComparer.Ordinal)
                    .ToArray()
            })
            .ToArray();
        var ranked = new List<RankedPage>();
        foreach (var stratum in strata)
        {
            for (var index = 0; index < stratum.Pages.Length; index++)
            {
                ranked.Add(new RankedPage(
                    stratum.Pages[index],
                    stratum.Stratum,
                    index,
                    stratum.Pages.Length));
            }
        }
        ranked.Sort(Compare);
        return ranked.Select(item => item.Page).ToArray();
    }

    static int Compare(RankedPage left, RankedPage right)
    {
        var leftRatio = (long)left.Index * right.Count;
        var rightRatio = (long)right.Index * left.Count;
        var comparison = leftRatio.CompareTo(rightRatio);
        if (comparison != 0)
            return comparison;
        comparison = left.Stratum.CompareTo(right.Stratum);
        if (comparison != 0)
            return comparison;
        return StringComparer.Ordinal.Compare(left.Page.SelectionHash, right.Page.SelectionHash);
    }

    static int GetStratum(int bodyBytes)
    {
        return bodyBytes switch
        {
            <= 1024 => 0,
            <= 4096 => 1,
            <= 16384 => 2,
            <= 65536 => 3,
            _ => 4
        };
    }

    sealed record RankedPage(PreparedPage Page, int Stratum, int Index, int Count);
}
