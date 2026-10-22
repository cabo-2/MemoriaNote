using System.Text.RegularExpressions;
using MemoriaNote.Performance.Contracts;
using MemoriaNote.Performance.Statistics;

namespace MemoriaNote.Performance.Validation;

internal static partial class ResultValidator
{
    internal static IReadOnlyList<string> Validate(PerformanceResultDocument result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var issues = new List<string>();
        if (result.SchemaVersion != PerformanceResultDocument.CurrentSchemaVersion)
        {
            issues.Add(
                $"schemaVersion must be '{PerformanceResultDocument.CurrentSchemaVersion}'.");
        }
        RequireText(result.Run.RunId, "run.runId", issues);
        RequireText(result.Run.RunnerVersion, "run.runnerVersion", issues);
        RequireText(result.Run.RunnerCommit, "run.runnerCommit", issues);
        RequireStatus(result.Run.Status, "run.status", issues);
        RequireText(result.Target.ArtifactId, "target.artifactId", issues);
        RequireText(result.Target.ProductVersion, "target.productVersion", issues);
        RequireText(result.Target.SourceCommit, "target.sourceCommit", issues);
        RequireText(result.Target.Rid, "target.rid", issues);
        RequireText(result.Target.Configuration, "target.configuration", issues);
        if (result.Target.DeploymentKind is not ("self-contained" or "native-aot"))
            issues.Add("target.deploymentKind is not supported.");
        if (result.Target.ArtifactInputKind is not ("publish-directory" or "extracted-archive"))
            issues.Add("target.artifactInputKind is not supported.");
        ValidateSha256(result.Target.ExecutableSha256, "target.executableSha256", issues);
        if (result.Target.ExecutableSizeBytes <= 0)
            issues.Add("target.executableSizeBytes must be positive.");
        if (result.Target.ArchiveSha256 != null)
            ValidateSha256(result.Target.ArchiveSha256, "target.archiveSha256", issues);

        if (result.Dataset != null)
        {
            RequireText(result.Dataset.DatasetId, "dataset.datasetId", issues);
            RequireText(result.Dataset.DatasetVersion, "dataset.datasetVersion", issues);
            RequireText(result.Dataset.SourceRevision, "dataset.sourceRevision", issues);
            ValidateSha256(result.Dataset.PreparedSha256, "dataset.preparedSha256", issues);
        }

        RequireText(result.Environment.EnvironmentLabel, "environment.environmentLabel", issues);
        RequireText(result.Environment.OsDescription, "environment.osDescription", issues);
        RequireText(result.Environment.OsArchitecture, "environment.osArchitecture", issues);
        RequireText(result.Environment.ProcessArchitecture, "environment.processArchitecture", issues);
        RequireText(result.Environment.CpuModel, "environment.cpuModel", issues);
        RequireText(result.Environment.FileSystem, "environment.fileSystem", issues);
        if (result.Environment.LogicalProcessorCount <= 0)
            issues.Add("environment.logicalProcessorCount must be positive.");

        if (result.ScenarioResults.Count == 0)
            issues.Add("scenarioResults must contain at least one result.");
        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in result.ScenarioResults)
        {
            var prefix = $"scenarioResults['{scenario.ScenarioId}']";
            RequireText(scenario.ScenarioId, $"{prefix}.scenarioId", issues);
            RequireText(scenario.ScenarioVersion, $"{prefix}.scenarioVersion", issues);
            if (!scenarioIds.Add(scenario.ScenarioId))
                issues.Add($"{prefix}.scenarioId is duplicated.");
            RequireText(scenario.CacheMode, $"{prefix}.cacheMode", issues);
            RequireStatus(scenario.Status, $"{prefix}.status", issues);
            if (scenario.WarmupCount < 0)
                issues.Add($"{prefix}.warmupCount cannot be negative.");
            if (scenario.MeasurementCount < 20)
                issues.Add($"{prefix}.measurementCount must be at least 20 for p95.");
            if (scenario.TimeoutMilliseconds <= 0)
                issues.Add($"{prefix}.timeoutMilliseconds must be positive.");
            ValidateOutcome(scenario.Validation, $"{prefix}.validation", issues);
            ValidateSamples(scenario, prefix, issues);
        }

        var hasInvalidScenario = result.ScenarioResults.Any(
            scenario => scenario.Status == "invalid");
        if ((result.Run.Status == "invalid") != hasInvalidScenario)
        {
            issues.Add(
                "run.status must be invalid exactly when a scenario result is invalid.");
        }
        if (hasInvalidScenario && result.Failures.Count == 0)
            issues.Add("An invalid run must contain at least one failure.");
        if (!hasInvalidScenario && result.Failures.Count != 0)
            issues.Add("A valid run cannot contain failures.");
        foreach (var failure in result.Failures)
        {
            RequireText(failure.Code, "failures[].code", issues);
            RequireText(failure.Message, "failures[].message", issues);
        }

        return issues;
    }

    static void ValidateSamples(
        ScenarioResult scenario,
        string prefix,
        List<string> issues)
    {
        var sequences = new HashSet<int>();
        for (var index = 0; index < scenario.Samples.Count; index++)
        {
            var sample = scenario.Samples[index];
            var samplePath = $"{prefix}.samples[{index}]";
            if (!sequences.Add(sample.Sequence))
                issues.Add($"{samplePath}.sequence is duplicated.");
            if (sample.Sequence != index)
                issues.Add($"{samplePath}.sequence must equal its zero-based position.");
            if (sample.ElapsedNanoseconds < 0)
                issues.Add($"{samplePath}.elapsedNanoseconds cannot be negative.");
            if (sample.StandardOutputBytes < 0 || sample.StandardErrorBytes < 0)
                issues.Add($"{samplePath} stream byte counts cannot be negative.");
            if (sample.Status is not (
                "valid" or "timeout" or "cancelled" or "start-failure" or "invalid"))
            {
                issues.Add($"{samplePath}.status is not supported.");
            }
            ValidateOutcome(sample.Validation, $"{samplePath}.validation", issues);
            if ((sample.Status == "valid") != sample.Validation.IsValid)
            {
                issues.Add(
                    $"{samplePath}.status and validation.isValid must agree.");
            }
        }

        if (scenario.Status == "valid")
        {
            if (scenario.Samples.Count != scenario.MeasurementCount)
                issues.Add($"{prefix} does not contain the declared measurement count.");
            if (scenario.Samples.Any(sample => sample.Status != "valid"))
                issues.Add($"{prefix} contains an invalid sample but is marked valid.");
            if (scenario.Summary == null)
            {
                issues.Add($"{prefix}.summary is required for a valid scenario.");
            }
            else
            {
                ValidateSummary(scenario, prefix, issues);
            }
        }
        else if (scenario.Summary != null)
        {
            issues.Add($"{prefix}.summary must be absent for an invalid scenario.");
        }
    }

    static void ValidateSummary(
        ScenarioResult scenario,
        string prefix,
        List<string> issues)
    {
        var durations = scenario.Samples
            .Select(sample => sample.ElapsedNanoseconds)
            .Where(duration => duration.HasValue)
            .Select(duration => duration!.Value)
            .ToArray();
        if (durations.Length != scenario.Samples.Count)
        {
            issues.Add($"{prefix} has a valid sample without elapsedNanoseconds.");
            return;
        }

        var expected = PercentileCalculator.Calculate(durations);
        var actual = scenario.Summary!;
        if (actual.SampleCount != expected.SampleCount ||
            actual.PercentileMethod != expected.PercentileMethod ||
            actual.P50Nanoseconds != expected.P50Nanoseconds ||
            actual.P95Nanoseconds != expected.P95Nanoseconds ||
            actual.MinNanoseconds != expected.MinNanoseconds ||
            actual.MaxNanoseconds != expected.MaxNanoseconds ||
            actual.ArithmeticMeanNanoseconds != expected.ArithmeticMeanNanoseconds)
        {
            issues.Add($"{prefix}.summary does not match its raw samples.");
        }
    }

    static void ValidateOutcome(
        ValidationOutcome outcome,
        string path,
        List<string> issues)
    {
        if (outcome.IsValid && outcome.Issues.Count != 0)
            issues.Add($"{path} cannot contain issues when it is valid.");
        if (!outcome.IsValid && outcome.Issues.Count == 0)
            issues.Add($"{path} must contain an issue when it is invalid.");
        if (outcome.Issues.Any(string.IsNullOrWhiteSpace))
            issues.Add($"{path}.issues cannot contain empty values.");
    }

    static void RequireStatus(string value, string path, List<string> issues)
    {
        if (value is not ("valid" or "invalid"))
            issues.Add($"{path} must be 'valid' or 'invalid'.");
    }

    static void RequireText(string value, string path, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add($"{path} is required.");
    }

    static void ValidateSha256(string value, string path, List<string> issues)
    {
        if (!Sha256Pattern().IsMatch(value))
            issues.Add($"{path} must be a lowercase SHA-256 value.");
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
