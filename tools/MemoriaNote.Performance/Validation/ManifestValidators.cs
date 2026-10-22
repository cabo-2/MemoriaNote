using System.Text.RegularExpressions;
using MemoriaNote.Performance.Contracts;

namespace MemoriaNote.Performance.Validation;

internal static partial class ManifestValidators
{
    static readonly HashSet<string> ArtifactInputKinds =
        ["publish-directory", "extracted-archive"];
    static readonly HashSet<string> CacheModes =
        ["process-cold", "warm-reopen", "fresh-state", "os-cold-diagnostic"];
    static readonly HashSet<string> ResetPolicies = ["per-trial", "reuse-state"];

    internal static IReadOnlyList<string> Validate(ScenarioManifestDocument manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<string>();
        if (manifest.SchemaVersion != ScenarioManifestDocument.CurrentSchemaVersion)
        {
            issues.Add(
                $"schemaVersion must be '{ScenarioManifestDocument.CurrentSchemaVersion}'.");
        }
        if (manifest.Scenarios.Count == 0)
            issues.Add("scenarios must contain at least one scenario.");

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in manifest.Scenarios)
        {
            var prefix = string.IsNullOrWhiteSpace(scenario.ScenarioId)
                ? "scenario"
                : $"scenario '{scenario.ScenarioId}'";
            ValidateIdentifier(scenario.ScenarioId, $"{prefix}.scenarioId", issues);
            if (!identifiers.Add(scenario.ScenarioId))
                issues.Add($"{prefix}.scenarioId is duplicated.");
            ValidateIdentifier(scenario.ScenarioVersion, $"{prefix}.scenarioVersion", issues);
            RequireText(scenario.Description, $"{prefix}.description", issues);
            if (scenario.Availability.Count == 0 ||
                scenario.Availability.Any(string.IsNullOrWhiteSpace))
            {
                issues.Add($"{prefix}.availability must contain non-empty values.");
            }
            if (!ArtifactInputKinds.Contains(scenario.ArtifactInputKind))
                issues.Add($"{prefix}.artifactInputKind is not supported.");
            if (!CacheModes.Contains(scenario.CacheMode))
                issues.Add($"{prefix}.cacheMode is not supported.");
            if (!ResetPolicies.Contains(scenario.ResetPolicy))
                issues.Add($"{prefix}.resetPolicy is not supported.");
            if (scenario.WarmupCount < 0)
                issues.Add($"{prefix}.warmupCount cannot be negative.");
            if (scenario.MeasurementCount <= 0)
                issues.Add($"{prefix}.measurementCount must be positive.");
            if (scenario.MeasurementCount < 20)
                issues.Add($"{prefix}.measurementCount must be at least 20 for p95.");
            if (scenario.TimeoutMilliseconds <= 0)
                issues.Add($"{prefix}.timeoutMilliseconds must be positive.");
            if ((scenario.DatasetId == null) != (scenario.ScaleId == null))
                issues.Add($"{prefix}.datasetId and scaleId must be specified together.");

            ValidateCommand(scenario.Command, $"{prefix}.command", issues);
            ValidateExpected(scenario.Expected, $"{prefix}.expected", issues);
            for (var index = 0; index < scenario.Arrange.Count; index++)
            {
                ValidateCommand(
                    scenario.Arrange[index].Command,
                    $"{prefix}.arrange[{index}].command",
                    issues);
                ValidateExpected(
                    scenario.Arrange[index].Expected,
                    $"{prefix}.arrange[{index}].expected",
                    issues);
            }
        }

        return issues;
    }

    internal static IReadOnlyList<string> Validate(DatasetManifestDocument manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<string>();
        if (manifest.SchemaVersion != DatasetManifestDocument.CurrentSchemaVersion)
        {
            issues.Add(
                $"schemaVersion must be '{DatasetManifestDocument.CurrentSchemaVersion}'.");
        }
        ValidateIdentifier(manifest.DatasetId, "datasetId", issues);
        ValidateIdentifier(manifest.DatasetVersion, "datasetVersion", issues);
        if (manifest.Languages.Count == 0 || manifest.Languages.Any(string.IsNullOrWhiteSpace))
            issues.Add("languages must contain non-empty values.");
        RequireText(manifest.Source.Name, "source.name", issues);
        RequireText(manifest.Source.Url, "source.url", issues);
        RequireText(manifest.Source.Revision, "source.revision", issues);
        ValidateOptionalSha256(manifest.Source.ArchiveSha256, "source.archiveSha256", issues);
        if (manifest.Licenses.Count == 0)
            issues.Add("licenses must contain at least one license record.");
        for (var index = 0; index < manifest.Licenses.Count; index++)
        {
            var license = manifest.Licenses[index];
            RequireText(license.Expression, $"licenses[{index}].expression", issues);
            RequireText(license.AppliesTo, $"licenses[{index}].appliesTo", issues);
            RequireText(license.LicenseFile, $"licenses[{index}].licenseFile", issues);
            RequireText(license.Attribution, $"licenses[{index}].attribution", issues);
        }
        RequireText(manifest.Selection.Description, "selection.description", issues);
        if (manifest.Selection.Scales.Count == 0)
            issues.Add("selection.scales must contain at least one scale.");
        var scaleIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scale in manifest.Selection.Scales)
        {
            ValidateIdentifier(scale.ScaleId, "selection.scales[].scaleId", issues);
            if (!scaleIdentifiers.Add(scale.ScaleId))
                issues.Add($"scaleId '{scale.ScaleId}' is duplicated.");
            if (scale.PageCount <= 0)
                issues.Add($"scale '{scale.ScaleId}' pageCount must be positive.");
        }
        RequireText(manifest.Transform.Name, "transform.name", issues);
        ValidateIdentifier(manifest.Transform.Version, "transform.version", issues);
        RequireText(manifest.Prepared.RelativePath, "prepared.relativePath", issues);
        ValidateSha256(manifest.Prepared.Sha256, "prepared.sha256", issues);
        if (manifest.Prepared.PageCount <= 0)
            issues.Add("prepared.pageCount must be positive.");
        if (manifest.Prepared.TotalBytes <= 0)
            issues.Add("prepared.totalBytes must be positive.");
        ValidateRelativePath(manifest.Prepared.RelativePath, "prepared.relativePath", issues);
        if (manifest.Selection.Scales.Any(
            scale => scale.PageCount > manifest.Prepared.PageCount))
        {
            issues.Add("selection scale pageCount cannot exceed prepared.pageCount.");
        }

        return issues;
    }

    static void ValidateCommand(
        ProcessCommandDefinition command,
        string path,
        List<string> issues)
    {
        if (command.Arguments.Any(argument => argument == null))
            issues.Add($"{path}.arguments cannot contain null.");
        RequireText(command.WorkingDirectory, $"{path}.workingDirectory", issues);
        foreach (var variable in command.Environment)
        {
            RequireText(variable.Key, $"{path}.environment key", issues);
            if (variable.Value == null)
                issues.Add($"{path}.environment value cannot be null.");
        }
    }

    static void ValidateExpected(
        ExpectedProcessResult expected,
        string path,
        List<string> issues)
    {
        if (expected.StandardOutputContains.Any(string.IsNullOrEmpty))
            issues.Add($"{path}.standardOutputContains cannot contain empty values.");
        if (expected.StandardErrorContains.Any(string.IsNullOrEmpty))
            issues.Add($"{path}.standardErrorContains cannot contain empty values.");
        for (var index = 0; index < expected.FileSystemEntries.Count; index++)
        {
            var entry = expected.FileSystemEntries[index];
            var entryPath = $"{path}.fileSystemEntries[{index}]";
            RequireText(entry.Path, $"{entryPath}.path", issues);
            if (entry.Kind is not ("file" or "directory"))
                issues.Add($"{entryPath}.kind is not supported.");
            if (!entry.Exists && (entry.SizeBytes != null || entry.Sha256 != null))
                issues.Add($"{entryPath} cannot specify size or hash when exists is false.");
            if (entry.Kind == "directory" && (entry.SizeBytes != null || entry.Sha256 != null))
                issues.Add($"{entryPath} directory cannot specify size or hash.");
            if (entry.SizeBytes < 0)
                issues.Add($"{entryPath}.sizeBytes cannot be negative.");
            ValidateOptionalSha256(entry.Sha256, $"{entryPath}.sha256", issues);
        }
    }

    static void ValidateIdentifier(string value, string path, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdentifierPattern().IsMatch(value))
            issues.Add($"{path} must match {IdentifierPattern()}.");
    }

    static void RequireText(string value, string path, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add($"{path} is required.");
    }

    static void ValidateOptionalSha256(string? value, string path, List<string> issues)
    {
        if (value != null)
            ValidateSha256(value, path, issues);
    }

    static void ValidateSha256(string value, string path, List<string> issues)
    {
        if (!Sha256Pattern().IsMatch(value))
            issues.Add($"{path} must be a lowercase SHA-256 value.");
    }

    static void ValidateRelativePath(string value, string path, List<string> issues)
    {
        if (Path.IsPathRooted(value) ||
            value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment == ".."))
        {
            issues.Add($"{path} must be a contained relative path.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
