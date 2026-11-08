#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MemoriaNote.Domain.Queries;

internal readonly record struct BodySearchMatch(int Start, int Length);

internal sealed record BodySearchSnippet(
    string Text,
    IReadOnlyList<BodySearchMatch> Matches);

/// <summary>Builds one bounded, sanitized snippet around the first literal match.</summary>
internal static class BodySearchSnippetBuilder
{
    internal const int MaximumScalarCount = 240;
    private const int ContextBeforeMatchScalarCount = 80;

    internal static BodySearchSnippet Build(
        string text,
        IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(keywords);
        if (keywords.Count == 0)
            throw new ArgumentException("At least one keyword is required.", nameof(keywords));

        var firstMatch = int.MaxValue;
        foreach (var keyword in keywords)
        {
            var position = text.IndexOf(keyword, StringComparison.Ordinal);
            if (position >= 0 && position < firstMatch)
                firstMatch = position;
        }
        if (firstMatch == int.MaxValue)
            throw new InvalidDataException("A search hit does not contain a query keyword.");

        var boundaries = BodySearchSnippetText.ReadScalarBoundaries(text);
        var firstMatchScalar = BodySearchSnippetText.FindBoundaryIndex(boundaries, firstMatch);
        var startScalar = Math.Max(0, firstMatchScalar - ContextBeforeMatchScalarCount);
        var endScalar = Math.Min(boundaries.Count - 1, startScalar + MaximumScalarCount);
        startScalar = Math.Max(0, endScalar - MaximumScalarCount);

        var windowStart = boundaries[startScalar];
        var windowEnd = boundaries[endScalar];
        var rawText = text.Substring(windowStart, windowEnd - windowStart);
        var matches = FindAndMergeMatches(rawText, keywords);
        if (matches.Count == 0)
            throw new InvalidDataException("A search snippet does not contain a complete match.");

        return new BodySearchSnippet(
            BodySearchSnippetText.Sanitize(rawText),
            matches.AsReadOnly());
    }

    private static List<BodySearchMatch> FindAndMergeMatches(
        string text,
        IReadOnlyList<string> keywords)
    {
        var matches = new List<BodySearchMatch>();
        foreach (var keyword in keywords)
        {
            var searchFrom = 0;
            while (searchFrom <= text.Length - keyword.Length)
            {
                var start = text.IndexOf(keyword, searchFrom, StringComparison.Ordinal);
                if (start < 0)
                    break;
                matches.Add(new BodySearchMatch(start, keyword.Length));
                searchFrom = start + 1;
            }
        }

        matches.Sort(static (left, right) =>
        {
            var start = left.Start.CompareTo(right.Start);
            return start != 0 ? start : left.Length.CompareTo(right.Length);
        });
        var merged = new List<BodySearchMatch>(matches.Count);
        foreach (var match in matches)
        {
            if (merged.Count == 0)
            {
                merged.Add(match);
                continue;
            }

            var previous = merged[^1];
            var previousEnd = previous.Start + previous.Length;
            var matchEnd = match.Start + match.Length;
            if (match.Start < previousEnd)
            {
                merged[^1] = new BodySearchMatch(
                    previous.Start,
                    Math.Max(previousEnd, matchEnd) - previous.Start);
            }
            else
            {
                merged.Add(match);
            }
        }
        return merged;
    }

}

/// <summary>Provides scalar-safe operations shared by body-search snippet implementations.</summary>
internal static class BodySearchSnippetText
{
    internal static List<int> ReadScalarBoundaries(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var boundaries = new List<int> { 0 };
        var offset = 0;
        while (offset < text.Length)
        {
            var status = Rune.DecodeFromUtf16(
                text.AsSpan(offset),
                out _,
                out var consumed);
            if (status != OperationStatus.Done)
                consumed = 1;
            offset += consumed;
            boundaries.Add(offset);
        }
        return boundaries;
    }

    internal static int FindBoundaryIndex(IReadOnlyList<int> boundaries, int offset)
    {
        ArgumentNullException.ThrowIfNull(boundaries);
        var low = 0;
        var high = boundaries.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (boundaries[middle] == offset)
                return middle;
            if (boundaries[middle] < offset)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return Math.Max(0, high);
    }

    internal static bool IsScalarBoundary(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset < 0 || offset > text.Length)
            return false;
        return offset == 0 || offset == text.Length ||
               !char.IsLowSurrogate(text[offset]) ||
               !char.IsHighSurrogate(text[offset - 1]);
    }

    internal static string Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length);
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                result.Append('\uFFFD');
                remaining = remaining[1..];
                continue;
            }

            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                result.Append(rune.Value is '\r' or '\n' or '\t' ? ' ' : '\uFFFD');
            }
            else
            {
                result.Append(rune.ToString());
            }
            remaining = remaining[consumed..];
        }
        return result.ToString();
    }
}
