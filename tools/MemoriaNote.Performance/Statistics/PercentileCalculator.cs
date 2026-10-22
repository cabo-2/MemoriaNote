using MemoriaNote.Performance.Contracts;

namespace MemoriaNote.Performance.Statistics;

internal static class PercentileCalculator
{
    internal const string Method = "nearest-rank";

    internal static PerformanceSummary Calculate(IEnumerable<long> durations)
    {
        ArgumentNullException.ThrowIfNull(durations);
        var ordered = durations.Order().ToArray();
        if (ordered.Length == 0)
            throw new ArgumentException("At least one duration is required.", nameof(durations));
        if (ordered.Any(duration => duration < 0))
            throw new ArgumentException("Durations cannot be negative.", nameof(durations));

        return new PerformanceSummary
        {
            SampleCount = ordered.Length,
            PercentileMethod = Method,
            P50Nanoseconds = NearestRank(ordered, 0.50m),
            P95Nanoseconds = NearestRank(ordered, 0.95m),
            MinNanoseconds = ordered[0],
            MaxNanoseconds = ordered[^1],
            ArithmeticMeanNanoseconds = checked((long)Math.Round(
                ordered.Average(duration => (decimal)duration),
                MidpointRounding.AwayFromZero))
        };
    }

    static long NearestRank(IReadOnlyList<long> ordered, decimal percentile)
    {
        var rank = (int)Math.Ceiling(percentile * ordered.Count);
        return ordered[rank - 1];
    }
}
