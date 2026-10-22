namespace MemoriaNote.Performance.Contracts;

internal sealed class ScenarioManifestDocument
{
    internal const string CurrentSchemaVersion = "1";

    public string SchemaVersion { get; set; } = string.Empty;

    public List<ScenarioDefinition> Scenarios { get; set; } = [];
}

internal sealed class ScenarioDefinition
{
    public string ScenarioId { get; set; } = string.Empty;

    public string ScenarioVersion { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> Availability { get; set; } = [];

    public string ArtifactInputKind { get; set; } = string.Empty;

    public string? DatasetId { get; set; }

    public string? ScaleId { get; set; }

    public string CacheMode { get; set; } = string.Empty;

    public List<ArrangeStepDefinition> Arrange { get; set; } = [];

    public ProcessCommandDefinition Command { get; set; } = new();

    public ExpectedProcessResult Expected { get; set; } = new();

    public int WarmupCount { get; set; }

    public int MeasurementCount { get; set; }

    public int TimeoutMilliseconds { get; set; }

    public string ResetPolicy { get; set; } = string.Empty;
}

internal sealed class ArrangeStepDefinition
{
    public ProcessCommandDefinition Command { get; set; } = new();

    public ExpectedProcessResult Expected { get; set; } = new();
}

internal sealed class ProcessCommandDefinition
{
    public List<string> Arguments { get; set; } = [];

    public string? StandardInput { get; set; }

    public string WorkingDirectory { get; set; } = "{working}";

    public Dictionary<string, string> Environment { get; set; } = [];
}

internal sealed class ExpectedProcessResult
{
    public int ExitCode { get; set; }

    public List<string> StandardOutputContains { get; set; } = [];

    public List<string> StandardErrorContains { get; set; } = [];

    public bool StandardOutputEmpty { get; set; }

    public bool StandardErrorEmpty { get; set; }

    public List<ExpectedFileSystemEntry> FileSystemEntries { get; set; } = [];
}

internal sealed class ExpectedFileSystemEntry
{
    public string Path { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public bool Exists { get; set; } = true;

    public long? SizeBytes { get; set; }

    public string? Sha256 { get; set; }
}
