using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.NotebookMigration;

internal interface ILegacyNotebookMigrationSource : IAsyncDisposable
{
    NotebookMigrationSourceKind Kind { get; }

    string SourcePath { get; }

    IReadOnlyList<LegacyMetadataRecord> Metadata { get; }

    IAsyncEnumerable<LegacyPageRecord> ReadPagesAsync(CancellationToken cancellationToken);
}

internal sealed record LegacyMetadataRecord(string Key, string Value);

internal sealed record LegacyPageRecord(
    long RowId,
    string Uuid,
    string Name,
    long Index,
    string Tags,
    string ContentType,
    string CreateTime,
    string UpdateTime,
    long IsErased,
    string Text,
    IReadOnlyList<string> ReadErrors);

internal sealed class LegacySourceException : Exception
{
    internal LegacySourceException(NotebookMigrationErrorCode error, string message)
        : base(message)
    {
        Error = error;
    }

    internal LegacySourceException(
        NotebookMigrationErrorCode error,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    internal NotebookMigrationErrorCode Error { get; }
}

internal sealed record MigrationResolution(
    IReadOnlyDictionary<string, string> Renames,
    IReadOnlyDictionary<string, IReadOnlyList<string>> TagOverrides,
    IReadOnlySet<string> ErasedPageExclusions)
{
    internal static MigrationResolution Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));
}

internal static class MigrationResolutionReader
{
    internal static async Task<MigrationResolution> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (path is null)
            return MigrationResolution.Empty;

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new LegacySourceException(
                NotebookMigrationErrorCode.InputNotFound,
                "The migration resolution file does not exist.");
        }

        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                    stream,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 32
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return Parse(document.RootElement);
        }
        catch (LegacySourceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new LegacySourceException(
                NotebookMigrationErrorCode.InvalidResolution,
                "The migration resolution document is invalid.",
                exception);
        }
    }

    private static MigrationResolution Parse(JsonElement root)
    {
        RequireObject(root, "resolution");
        RequireProperties(root, "version", "renames", "tagOverrides", "erasedPageExclusions");
        var properties = UniqueProperties(root, "resolution");

        if (!properties.TryGetValue("version", out var version) ||
            version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var versionNumber) ||
            versionNumber != 1)
        {
            Invalid("The resolution version must be 1.");
        }

        var renames = ReadRenames(Required(properties, "renames"));
        var overrides = ReadTagOverrides(Required(properties, "tagOverrides"));
        var exclusions = ReadExclusions(Required(properties, "erasedPageExclusions"));

        foreach (var pageId in exclusions)
        {
            if (renames.ContainsKey(pageId) || overrides.ContainsKey(pageId))
                Invalid("An erased-page exclusion cannot also rename or override the page.");
        }

        return new MigrationResolution(renames, overrides, exclusions);
    }

    private static IReadOnlyDictionary<string, string> ReadRenames(JsonElement element)
    {
        RequireArray(element, "renames");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            RequireObject(item, "rename");
            RequireProperties(item, "pageId", "name");
            var properties = UniqueProperties(item, "rename");
            var pageId = ReadPageId(Required(properties, "pageId"));
            var name = ReadString(Required(properties, "name"), "rename.name");
            if (!result.TryAdd(pageId, name))
                Invalid("The resolution contains a duplicate rename Page ID.");
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadTagOverrides(
        JsonElement element)
    {
        RequireArray(element, "tagOverrides");
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            RequireObject(item, "tagOverride");
            RequireProperties(item, "pageId", "labels");
            var properties = UniqueProperties(item, "tagOverride");
            var pageId = ReadPageId(Required(properties, "pageId"));
            var labelsElement = Required(properties, "labels");
            RequireArray(labelsElement, "tagOverride.labels");
            var labels = labelsElement.EnumerateArray()
                .Select(candidate => ReadString(candidate, "tagOverride.labels[]"))
                .ToArray();
            if (!result.TryAdd(pageId, labels))
                Invalid("The resolution contains a duplicate tag override Page ID.");
        }

        return result;
    }

    private static IReadOnlySet<string> ReadExclusions(JsonElement element)
    {
        RequireArray(element, "erasedPageExclusions");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            var pageId = ReadPageId(item);
            if (!result.Add(pageId))
                Invalid("The resolution contains a duplicate erased-page exclusion.");
        }

        return result;
    }

    private static string ReadPageId(JsonElement element)
    {
        var value = ReadString(element, "pageId");
        if (!Guid.TryParseExact(value, "D", out var pageId) || pageId == Guid.Empty)
            Invalid("A resolution Page ID is invalid.");
        return pageId.ToString("D");
    }

    private static string ReadString(JsonElement element, string location)
    {
        if (element.ValueKind != JsonValueKind.String)
            Invalid($"{location} must be a string.");
        return element.GetString();
    }

    private static Dictionary<string, JsonElement> UniqueProperties(
        JsonElement element,
        string location)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value))
                Invalid($"{location} contains a duplicate property.");
        }

        return result;
    }

    private static void RequireProperties(JsonElement element, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name))
                Invalid($"The resolution contains an unknown property: {property.Name}.");
        }
    }

    private static JsonElement Required(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value))
            Invalid($"The resolution is missing the {name} property.");
        return value;
    }

    private static void RequireObject(JsonElement element, string location)
    {
        if (element.ValueKind != JsonValueKind.Object)
            Invalid($"{location} must be an object.");
    }

    private static void RequireArray(JsonElement element, string location)
    {
        if (element.ValueKind != JsonValueKind.Array)
            Invalid($"{location} must be an array.");
    }

    private static void Invalid(string message) =>
        throw new InvalidDataException(message);
}
