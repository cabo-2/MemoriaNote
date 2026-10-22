using MemoriaNote.Performance.Contracts;
using MemoriaNote.Performance.Statistics;
using MemoriaNote.Performance.Validation;

namespace MemoriaNote.Performance.Tests;

/// <summary>Verifies performance result semantic validation.</summary>
[TestFixture]
public sealed class ResultValidatorTests
{
    /// <summary>Verifies a complete result with raw samples and matching summary is valid.</summary>
    [Test]
    public void Validate_CompleteResult_IsValid()
    {
        var result = CreateValidResult();

        var issues = ResultValidator.Validate(result);

        Assert.That(issues, Is.Empty);
    }

    /// <summary>Verifies summary tampering is detected from retained raw samples.</summary>
    [Test]
    public void Validate_ModifiedSummary_IsRejected()
    {
        var result = CreateValidResult();
        result.ScenarioResults[0].Summary!.P95Nanoseconds++;

        var issues = ResultValidator.Validate(result);

        Assert.That(issues, Has.Some.Contains("does not match its raw samples"));
    }

    internal static PerformanceResultDocument CreateValidResult()
    {
        var samples = Enumerable.Range(1, 20)
            .Select((duration, sequence) => new PerformanceSample
            {
                Sequence = sequence,
                ElapsedNanoseconds = duration,
                ExitCode = 0,
                Status = "valid",
                StandardOutputBytes = 3,
                StandardErrorBytes = 0,
                Validation = new ValidationOutcome { IsValid = true }
            })
            .ToList();
        return new PerformanceResultDocument
        {
            Run = new RunIdentity
            {
                RunId = "test-run",
                StartedAtUtc = DateTimeOffset.UtcNow,
                RunnerVersion = "1.0.0",
                RunnerCommit = "abcdef0",
                Status = "valid"
            },
            Target = new TargetIdentity
            {
                ArtifactId = "test-artifact",
                ProductVersion = "1.0.0",
                SourceCommit = "abcdef0",
                Rid = "test-x64",
                Configuration = "Release",
                DeploymentKind = "self-contained",
                ArtifactInputKind = "publish-directory",
                ExecutableSha256 = new string('a', 64),
                ExecutableSizeBytes = 1
            },
            Environment = new EnvironmentIdentity
            {
                EnvironmentLabel = "test",
                OsDescription = "test OS",
                OsArchitecture = "X64",
                ProcessArchitecture = "X64",
                CpuModel = "test CPU",
                LogicalProcessorCount = 1,
                FileSystem = "testfs"
            },
            ScenarioResults =
            [
                new ScenarioResult
                {
                    ScenarioId = "test",
                    ScenarioVersion = "1",
                    CacheMode = "process-cold",
                    WarmupCount = 0,
                    MeasurementCount = 20,
                    TimeoutMilliseconds = 1000,
                    Status = "valid",
                    Samples = samples,
                    Summary = PercentileCalculator.Calculate(
                        samples.Select(sample => sample.ElapsedNanoseconds!.Value)),
                    Validation = new ValidationOutcome { IsValid = true }
                }
            ]
        };
    }
}
