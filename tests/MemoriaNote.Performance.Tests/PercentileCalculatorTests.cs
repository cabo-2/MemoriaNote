using MemoriaNote.Performance.Statistics;

namespace MemoriaNote.Performance.Tests;

/// <summary>Verifies deterministic nearest-rank summary calculation.</summary>
[TestFixture]
public sealed class PercentileCalculatorTests
{
    /// <summary>Verifies p50 and p95 use the documented one-based nearest rank.</summary>
    [Test]
    public void Calculate_TwentyOrderedSamples_UsesNearestRank()
    {
        var summary = PercentileCalculator.Calculate(Enumerable.Range(1, 20)
            .Select(value => (long)value)
            .Reverse());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.PercentileMethod, Is.EqualTo("nearest-rank"));
            Assert.That(summary.SampleCount, Is.EqualTo(20));
            Assert.That(summary.P50Nanoseconds, Is.EqualTo(10));
            Assert.That(summary.P95Nanoseconds, Is.EqualTo(19));
            Assert.That(summary.MinNanoseconds, Is.EqualTo(1));
            Assert.That(summary.MaxNanoseconds, Is.EqualTo(20));
            Assert.That(summary.ArithmeticMeanNanoseconds, Is.EqualTo(11));
        }
    }

    /// <summary>Verifies negative durations cannot enter a result summary.</summary>
    [Test]
    public void Calculate_NegativeDuration_IsRejected()
    {
        Assert.That(
            () => PercentileCalculator.Calculate([1, -1]),
            Throws.ArgumentException);
    }
}
