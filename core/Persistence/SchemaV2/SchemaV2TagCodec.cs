using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Encodes and validates the canonical tag representation stored in Pages.Tags.</summary>
internal static class SchemaV2TagCodec
{
    /// <summary>Canonicalizes labels by SQLite BINARY order and encodes a compact JSON array.</summary>
    internal static string Encode(IEnumerable<string> labels)
    {
        var canonical = Canonicalize(labels);
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < canonical.Count; index++)
        {
            if (index > 0)
                builder.Append(',');
            AppendJsonString(builder, canonical[index]);
        }

        return builder.Append(']').ToString();
    }

    /// <summary>Returns validated, duplicate-free labels in SQLite BINARY order.</summary>
    internal static IReadOnlyList<string> Canonicalize(IEnumerable<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in labels)
        {
            ValidateLabel(label);
            unique.Add(label);
        }

        var result = unique.ToArray();
        Array.Sort(result, Utf8ByteComparer.Instance);
        return result;
    }

    /// <summary>Decodes canonical JSON and rejects every non-canonical representation.</summary>
    internal static IReadOnlyList<string> Decode(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Pages.Tags must be a JSON array.");

            var labels = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Every Pages.Tags element must be a string.");
                labels.Add(element.GetString());
            }

            IReadOnlyList<string> canonical;
            try
            {
                canonical = Canonicalize(labels);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Pages.Tags contains an invalid label.", exception);
            }

            if (canonical.Count != labels.Count ||
                !string.Equals(Encode(canonical), json, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Pages.Tags is not canonical JSON.");
            }

            return canonical;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Pages.Tags is not valid JSON.", exception);
        }
    }

    private static void ValidateLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (string.IsNullOrWhiteSpace(label) ||
            !string.Equals(label, label.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("A tag label has invalid whitespace.", nameof(label));
        }

        var scalarCount = 0;
        var remaining = label.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done)
                throw new ArgumentException("A tag label is not valid UTF-16.", nameof(label));
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
                throw new ArgumentException("A tag label contains a control character.", nameof(label));
            scalarCount++;
            remaining = remaining[consumed..];
        }

        if (scalarCount is < 1 or > 64)
            throw new ArgumentException("A tag label must contain 1 to 64 Unicode scalars.", nameof(label));
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var rune in value.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\t': builder.Append("\\t"); break;
                case '\n': builder.Append("\\n"); break;
                case '\f': builder.Append("\\f"); break;
                case '\r': builder.Append("\\r"); break;
                default: builder.Append(rune.ToString()); break;
            }
        }

        builder.Append('"');
    }

    private sealed class Utf8ByteComparer : IComparer<string>
    {
        internal static Utf8ByteComparer Instance { get; } = new();

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            return Encoding.UTF8.GetBytes(x).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(y));
        }
    }
}
