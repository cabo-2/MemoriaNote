#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MemoriaNote.Domain.Queries;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Converts one marked FTS5 snippet into the common structured snippet contract.</summary>
internal static class Fts5BodySearchSnippetParser
{
    private const int ContextBeforeMatchScalarCount = 80;

    internal static BodySearchSnippet Parse(
        string markedText,
        string startMarker,
        string endMarker,
        bool sourceContainsMarker)
    {
        ArgumentNullException.ThrowIfNull(markedText);
        ArgumentException.ThrowIfNullOrEmpty(startMarker);
        ArgumentException.ThrowIfNullOrEmpty(endMarker);
        if (string.Equals(startMarker, endMarker, StringComparison.Ordinal))
            throw new ArgumentException("FTS5 snippet markers must be distinct.");
        if (sourceContainsMarker)
        {
            throw new InvalidDataException(
                "An FTS5 snippet marker collides with authoritative page text.");
        }

        var text = new StringBuilder(markedText.Length);
        var matches = new List<BodySearchMatch>();
        var sourceOffset = 0;
        int? matchStart = null;
        while (sourceOffset < markedText.Length)
        {
            var nextStart = markedText.IndexOf(
                startMarker,
                sourceOffset,
                StringComparison.Ordinal);
            var nextEnd = markedText.IndexOf(
                endMarker,
                sourceOffset,
                StringComparison.Ordinal);
            if (nextStart < 0 && nextEnd < 0)
            {
                text.Append(markedText, sourceOffset, markedText.Length - sourceOffset);
                sourceOffset = markedText.Length;
                break;
            }

            var isStart = nextStart >= 0 && (nextEnd < 0 || nextStart < nextEnd);
            var markerOffset = isStart ? nextStart : nextEnd;
            text.Append(markedText, sourceOffset, markerOffset - sourceOffset);
            if (isStart)
            {
                if (matchStart is not null)
                    throw new InvalidDataException("An FTS5 snippet contains nested markers.");
                matchStart = text.Length;
                sourceOffset = markerOffset + startMarker.Length;
            }
            else
            {
                if (matchStart is null)
                    throw new InvalidDataException("An FTS5 snippet contains an unmatched marker.");
                if (text.Length == matchStart.Value)
                    throw new InvalidDataException("An FTS5 snippet contains an empty match.");
                matches.Add(new BodySearchMatch(matchStart.Value, text.Length - matchStart.Value));
                matchStart = null;
                sourceOffset = markerOffset + endMarker.Length;
            }
        }

        if (matchStart is not null)
            throw new InvalidDataException("An FTS5 snippet contains an unmatched marker.");
        if (matches.Count == 0)
            throw new InvalidDataException("An FTS5 snippet does not contain a visible match.");

        var plainText = text.ToString();
        ValidateMatchBoundaries(plainText, matches);
        var bounded = Bound(plainText, matches);
        return new BodySearchSnippet(
            BodySearchSnippetText.Sanitize(bounded.Text),
            bounded.Matches);
    }

    private static BodySearchSnippet Bound(
        string text,
        IReadOnlyList<BodySearchMatch> matches)
    {
        var boundaries = BodySearchSnippetText.ReadScalarBoundaries(text);
        if (boundaries.Count - 1 <= BodySearchSnippetBuilder.MaximumScalarCount)
            return new BodySearchSnippet(text, matches);

        var firstMatch = matches[0];
        var firstMatchStartScalar = BodySearchSnippetText.FindBoundaryIndex(
            boundaries,
            firstMatch.Start);
        var firstMatchEndScalar = BodySearchSnippetText.FindBoundaryIndex(
            boundaries,
            firstMatch.Start + firstMatch.Length);
        var startScalar = Math.Max(
            0,
            firstMatchStartScalar - ContextBeforeMatchScalarCount);
        if (firstMatchEndScalar - startScalar > BodySearchSnippetBuilder.MaximumScalarCount)
        {
            startScalar = Math.Max(
                0,
                firstMatchEndScalar - BodySearchSnippetBuilder.MaximumScalarCount);
        }
        var endScalar = Math.Min(
            boundaries.Count - 1,
            startScalar + BodySearchSnippetBuilder.MaximumScalarCount);
        startScalar = Math.Max(
            0,
            endScalar - BodySearchSnippetBuilder.MaximumScalarCount);

        var windowStart = boundaries[startScalar];
        var windowEnd = boundaries[endScalar];
        var boundedMatches = matches
            .Select(match => new BodySearchMatch(
                Math.Max(match.Start, windowStart) - windowStart,
                Math.Min(match.Start + match.Length, windowEnd) -
                Math.Max(match.Start, windowStart)))
            .Where(match => match.Length > 0)
            .ToArray();
        if (boundedMatches.Length == 0)
            throw new InvalidDataException("A bounded FTS5 snippet contains no visible match.");

        return new BodySearchSnippet(
            text.Substring(windowStart, windowEnd - windowStart),
            boundedMatches);
    }

    private static void ValidateMatchBoundaries(
        string text,
        IReadOnlyList<BodySearchMatch> matches)
    {
        var previousEnd = -1;
        foreach (var match in matches)
        {
            var end = match.Start + match.Length;
            if (match.Start < 0 || match.Length <= 0 || end > text.Length ||
                match.Start < previousEnd ||
                !BodySearchSnippetText.IsScalarBoundary(text, match.Start) ||
                !BodySearchSnippetText.IsScalarBoundary(text, end))
            {
                throw new InvalidDataException(
                    "An FTS5 snippet contains an invalid match range.");
            }
            previousEnd = end;
        }
    }
}
