using MemoriaNote.Performance.Contracts;

namespace MemoriaNote.Performance.Validation;

internal static class ContractLoader
{
    static readonly JsonSchemaValidator ScenarioSchema =
        JsonSchemaValidator.FromEmbeddedResource("scenario-manifest-v1.schema.json");
    static readonly JsonSchemaValidator DatasetSchema =
        JsonSchemaValidator.FromEmbeddedResource("dataset-manifest-v1.schema.json");
    static readonly JsonSchemaValidator DatasetSchemaV2 =
        JsonSchemaValidator.FromEmbeddedResource("dataset-manifest-v2.schema.json");
    static readonly JsonSchemaValidator ResultSchema =
        JsonSchemaValidator.FromEmbeddedResource("result-v1.schema.json");

    internal static async Task<ScenarioManifestDocument> ReadScenarioManifestAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        ThrowForSchemaIssues(path, ScenarioSchema.Validate(bytes));
        var manifest = await ContractJson.ReadAsync<ScenarioManifestDocument>(
            path,
            cancellationToken);
        ThrowForSemanticIssues(path, ManifestValidators.Validate(manifest));
        return manifest;
    }

    internal static async Task<DatasetManifestDocument> ReadDatasetManifestAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var schema = ReadSchemaVersion(bytes) == "2" ? DatasetSchemaV2 : DatasetSchema;
        ThrowForSchemaIssues(path, schema.Validate(bytes));
        var manifest = await ContractJson.ReadAsync<DatasetManifestDocument>(
            path,
            cancellationToken);
        ThrowForSemanticIssues(path, ManifestValidators.Validate(manifest));
        return manifest;
    }

    static string? ReadSchemaVersion(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(bytes);
            return document.RootElement.TryGetProperty("schemaVersion", out var version) &&
                version.ValueKind == System.Text.Json.JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    internal static async Task<PerformanceResultDocument> ReadResultAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        ThrowForSchemaIssues(path, ResultSchema.Validate(bytes));
        return await ContractJson.ReadAsync<PerformanceResultDocument>(path, cancellationToken);
    }

    internal static IReadOnlyList<string> ValidateResultJson(ReadOnlyMemory<byte> bytes)
    {
        return ResultSchema.Validate(bytes);
    }

    static void ThrowForSchemaIssues(string path, IReadOnlyList<string> issues)
    {
        if (issues.Count > 0)
        {
            throw new ContractException(
                $"'{path}' does not satisfy its JSON Schema:{Environment.NewLine}" +
                string.Join(Environment.NewLine, issues.Select(issue => $"- {issue}")));
        }
    }

    static void ThrowForSemanticIssues(string path, IReadOnlyList<string> issues)
    {
        if (issues.Count > 0)
        {
            throw new ContractException(
                $"'{path}' is semantically invalid:{Environment.NewLine}" +
                string.Join(Environment.NewLine, issues.Select(issue => $"- {issue}")));
        }
    }
}
