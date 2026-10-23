using System.Text.Json;

namespace MemoriaNote.DatasetPreparation.Transform;

internal static class MarkdownDocumentParser
{
    internal static ParsedMarkdown Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = NormalizeNewlines(text.TrimStart('\uFEFF'));
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = normalized;
        if (normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            if (end >= 0)
            {
                ParseFrontMatter(normalized.AsSpan(4, end - 4), metadata);
                body = normalized[(end + 5)..];
            }
        }

        return new ParsedMarkdown(body, metadata);
    }

    internal static string? FindFirstHeading(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                var heading = line[2..].Trim();
                if (heading.Length > 0)
                    return heading;
            }
        }
        return null;
    }

    static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    static void ParseFrontMatter(
        ReadOnlySpan<char> frontMatter,
        IDictionary<string, string> metadata)
    {
        foreach (var range in frontMatter.Split('\n'))
        {
            var line = frontMatter[range];
            if (line.IsEmpty || char.IsWhiteSpace(line[0]) || line[0] == '#')
                continue;
            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;
            var key = line[..separator].Trim().ToString();
            var rawValue = line[(separator + 1)..].Trim().ToString();
            var value = ParseScalar(rawValue);
            if (value != null)
                metadata.TryAdd(key, value);
        }
    }

    static string? ParseScalar(string value)
    {
        if (value.Length == 0 || value is ">" or ">-" or "|" or "|-")
            return null;
        if (value[0] == '[' || value[0] == '{')
            return null;
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(value);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return (comment >= 0 ? value[..comment] : value).Trim();
    }
}

internal sealed record ParsedMarkdown(
    string Body,
    IReadOnlyDictionary<string, string> Metadata);
