using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoriaNote.Performance.Validation;

internal sealed class JsonSchemaValidator
{
    readonly JsonDocument _schema;

    JsonSchemaValidator(JsonDocument schema)
    {
        _schema = schema;
    }

    internal static JsonSchemaValidator FromEmbeddedResource(string fileName)
    {
        var assembly = typeof(JsonSchemaValidator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith($".{fileName}", StringComparison.Ordinal)) ??
            throw new InvalidOperationException(
                $"Embedded JSON Schema '{fileName}' was not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"Embedded JSON Schema '{resourceName}' could not be opened.");
        return new JsonSchemaValidator(JsonDocument.Parse(stream));
    }

    internal IReadOnlyList<string> Validate(ReadOnlyMemory<byte> json)
    {
        if (json.Span.StartsWith("\ufeff"u8))
            json = json[3..];

        JsonDocument instance;
        try
        {
            instance = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
        }
        catch (JsonException exception)
        {
            return [$"$: invalid JSON: {exception.Message}"];
        }

        using (instance)
        {
            var issues = new List<string>();
            ValidateElement(instance.RootElement, _schema.RootElement, "$", issues);
            return issues;
        }
    }

    void ValidateElement(
        JsonElement instance,
        JsonElement schema,
        string path,
        List<string> issues)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            ValidateElement(instance, ResolveReference(reference.GetString()), path, issues);
            return;
        }

        if (schema.TryGetProperty("type", out var type) && !MatchesType(instance, type))
        {
            issues.Add($"{path}: expected type {type.GetRawText()}.");
            return;
        }

        if (schema.TryGetProperty("const", out var constant) &&
            !JsonElement.DeepEquals(instance, constant))
        {
            issues.Add($"{path}: expected constant {constant.GetRawText()}.");
        }

        if (schema.TryGetProperty("enum", out var enumeration) &&
            !enumeration.EnumerateArray().Any(value => JsonElement.DeepEquals(instance, value)))
        {
            issues.Add($"{path}: value is not in {enumeration.GetRawText()}.");
        }

        switch (instance.ValueKind)
        {
            case JsonValueKind.Object:
                ValidateObject(instance, schema, path, issues);
                break;
            case JsonValueKind.Array:
                ValidateArray(instance, schema, path, issues);
                break;
            case JsonValueKind.String:
                ValidateString(instance.GetString()!, schema, path, issues);
                break;
            case JsonValueKind.Number:
                ValidateNumber(instance, schema, path, issues);
                break;
        }
    }

    void ValidateObject(
        JsonElement instance,
        JsonElement schema,
        string path,
        List<string> issues)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var nameElement in required.EnumerateArray())
            {
                var name = nameElement.GetString()!;
                if (!instance.TryGetProperty(name, out _))
                    issues.Add($"{path}: required property '{name}' is missing.");
            }
        }

        schema.TryGetProperty("properties", out var properties);
        schema.TryGetProperty("additionalProperties", out var additionalProperties);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in instance.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (!seenNames.Add(property.Name))
            {
                issues.Add($"{propertyPath}: duplicate property is not allowed.");
                continue;
            }
            if (properties.ValueKind == JsonValueKind.Object &&
                properties.TryGetProperty(property.Name, out var propertySchema))
            {
                ValidateElement(property.Value, propertySchema, propertyPath, issues);
                continue;
            }

            if (additionalProperties.ValueKind == JsonValueKind.False)
            {
                issues.Add($"{propertyPath}: additional property is not allowed.");
            }
            else if (additionalProperties.ValueKind == JsonValueKind.Object)
            {
                ValidateElement(property.Value, additionalProperties, propertyPath, issues);
            }
        }
    }

    void ValidateArray(
        JsonElement instance,
        JsonElement schema,
        string path,
        List<string> issues)
    {
        var items = instance.EnumerateArray().ToArray();
        if (schema.TryGetProperty("minItems", out var minimum) &&
            items.Length < minimum.GetInt32())
        {
            issues.Add($"{path}: must contain at least {minimum.GetInt32()} items.");
        }
        if (schema.TryGetProperty("items", out var itemSchema))
        {
            for (var index = 0; index < items.Length; index++)
                ValidateElement(items[index], itemSchema, $"{path}[{index}]", issues);
        }
    }

    static void ValidateString(
        string value,
        JsonElement schema,
        string path,
        List<string> issues)
    {
        if (schema.TryGetProperty("minLength", out var minimum) &&
            value.Length < minimum.GetInt32())
        {
            issues.Add($"{path}: must contain at least {minimum.GetInt32()} characters.");
        }
        if (schema.TryGetProperty("pattern", out var pattern) &&
            !Regex.IsMatch(value, pattern.GetString()!, RegexOptions.CultureInvariant))
        {
            issues.Add($"{path}: does not match '{pattern.GetString()}'.");
        }
        if (schema.TryGetProperty("format", out var format) &&
            format.GetString() == "date-time" &&
            !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
        {
            issues.Add($"{path}: must be an RFC 3339 round-trip date-time.");
        }
    }

    static void ValidateNumber(
        JsonElement instance,
        JsonElement schema,
        string path,
        List<string> issues)
    {
        if (schema.TryGetProperty("minimum", out var minimum) &&
            instance.GetDecimal() < minimum.GetDecimal())
        {
            issues.Add($"{path}: must be at least {minimum.GetRawText()}.");
        }
    }

    JsonElement ResolveReference(string? reference)
    {
        if (reference == null || !reference.StartsWith("#/", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported JSON Schema reference '{reference}'.");

        var current = _schema.RootElement;
        foreach (var segment in reference[2..].Split('/'))
        {
            var name = segment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            current = current.GetProperty(name);
        }
        return current;
    }

    static bool MatchesType(JsonElement instance, JsonElement schemaType)
    {
        if (schemaType.ValueKind == JsonValueKind.Array)
        {
            return schemaType.EnumerateArray()
                .Any(candidate => MatchesType(instance, candidate));
        }

        return schemaType.GetString() switch
        {
            "object" => instance.ValueKind == JsonValueKind.Object,
            "array" => instance.ValueKind == JsonValueKind.Array,
            "string" => instance.ValueKind == JsonValueKind.String,
            "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
            "number" => instance.ValueKind == JsonValueKind.Number,
            "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => instance.ValueKind == JsonValueKind.Null,
            _ => throw new InvalidOperationException(
                $"Unsupported JSON Schema type '{schemaType.GetString()}'.")
        };
    }
}
