namespace MemoriaNote.Performance.Runner;

internal sealed record RunOptions
{
    internal required string ScenarioManifestPath { get; init; }

    internal string? DatasetManifestPath { get; init; }

    internal string? DatasetRoot { get; init; }

    internal required string ExecutablePath { get; init; }

    internal required string ArtifactId { get; init; }

    internal required string ProductVersion { get; init; }

    internal required string SourceCommit { get; init; }

    internal required string Rid { get; init; }

    internal string Configuration { get; init; } = "Release";

    internal required string DeploymentKind { get; init; }

    internal required string ArtifactInputKind { get; init; }

    internal string? ArchivePath { get; init; }

    internal required string EnvironmentLabel { get; init; }

    internal string? PowerModeNote { get; init; }

    internal required string OutputPath { get; init; }

    internal string? TemporaryRoot { get; init; }

    internal bool KeepTemporary { get; init; }

    internal IReadOnlyList<string> ScenarioIds { get; init; } = [];
}
