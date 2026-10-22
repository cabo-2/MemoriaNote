namespace MemoriaNote.Performance.Contracts;

internal sealed class PerformanceResultDocument
{
    internal const string CurrentSchemaVersion = "1";

    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    public RunIdentity Run { get; set; } = new();

    public TargetIdentity Target { get; set; } = new();

    public DatasetIdentity? Dataset { get; set; }

    public EnvironmentIdentity Environment { get; set; } = new();

    public List<ScenarioResult> ScenarioResults { get; set; } = [];

    public List<RunFailure> Failures { get; set; } = [];
}

internal sealed class RunIdentity
{
    public string RunId { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public string RunnerVersion { get; set; } = string.Empty;

    public string RunnerCommit { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}

internal sealed class TargetIdentity
{
    public string ArtifactId { get; set; } = string.Empty;

    public string ProductVersion { get; set; } = string.Empty;

    public string SourceCommit { get; set; } = string.Empty;

    public string Rid { get; set; } = string.Empty;

    public string Configuration { get; set; } = string.Empty;

    public string DeploymentKind { get; set; } = string.Empty;

    public string ArtifactInputKind { get; set; } = string.Empty;

    public string ExecutableSha256 { get; set; } = string.Empty;

    public long ExecutableSizeBytes { get; set; }

    public string? ArchiveSha256 { get; set; }
}

internal sealed class DatasetIdentity
{
    public string DatasetId { get; set; } = string.Empty;

    public string DatasetVersion { get; set; } = string.Empty;

    public string SourceRevision { get; set; } = string.Empty;

    public string PreparedSha256 { get; set; } = string.Empty;
}

internal sealed class EnvironmentIdentity
{
    public string EnvironmentLabel { get; set; } = string.Empty;

    public string OsDescription { get; set; } = string.Empty;

    public string OsArchitecture { get; set; } = string.Empty;

    public string ProcessArchitecture { get; set; } = string.Empty;

    public string CpuModel { get; set; } = string.Empty;

    public int LogicalProcessorCount { get; set; }

    public long? InstalledMemoryBytes { get; set; }

    public string FileSystem { get; set; } = string.Empty;

    public string? PowerModeNote { get; set; }
}

internal sealed class ScenarioResult
{
    public string ScenarioId { get; set; } = string.Empty;

    public string ScenarioVersion { get; set; } = string.Empty;

    public string CacheMode { get; set; } = string.Empty;

    public int WarmupCount { get; set; }

    public int MeasurementCount { get; set; }

    public int TimeoutMilliseconds { get; set; }

    public string Status { get; set; } = string.Empty;

    public List<PerformanceSample> Samples { get; set; } = [];

    public PerformanceSummary? Summary { get; set; }

    public ValidationOutcome Validation { get; set; } = new();
}

internal sealed class PerformanceSample
{
    public int Sequence { get; set; }

    public long? ElapsedNanoseconds { get; set; }

    public int? ExitCode { get; set; }

    public string Status { get; set; } = string.Empty;

    public long StandardOutputBytes { get; set; }

    public long StandardErrorBytes { get; set; }

    public long? PeakWorkingSetBytes { get; set; }

    public long? TotalProcessorTimeNanoseconds { get; set; }

    public string? SecondaryMetricUnavailableReason { get; set; }

    public ValidationOutcome Validation { get; set; } = new();
}

internal sealed class PerformanceSummary
{
    public int SampleCount { get; set; }

    public string PercentileMethod { get; set; } = string.Empty;

    public long P50Nanoseconds { get; set; }

    public long P95Nanoseconds { get; set; }

    public long MinNanoseconds { get; set; }

    public long MaxNanoseconds { get; set; }

    public long ArithmeticMeanNanoseconds { get; set; }
}

internal sealed class ValidationOutcome
{
    public bool IsValid { get; set; }

    public List<string> Issues { get; set; } = [];
}

internal sealed class RunFailure
{
    public string Code { get; set; } = string.Empty;

    public string ScenarioId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
