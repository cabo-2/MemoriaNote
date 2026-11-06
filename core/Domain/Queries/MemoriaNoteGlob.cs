#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace MemoriaNote.Domain.Queries;

internal enum MemoriaNoteGlobParseError
{
    None,
    Empty,
    InvalidUnicode,
    ControlCharacter,
    SourceTooLong,
    DanglingEscape,
    UnclosedCharacterClass,
    EmptyCharacterClass,
    InvalidRange,
    ReversedRange
}

internal abstract record MemoriaNoteGlobToken;

internal sealed record MemoriaNoteGlobLiteralToken(Rune Value) : MemoriaNoteGlobToken;

internal sealed record MemoriaNoteGlobAnyToken : MemoriaNoteGlobToken;

internal sealed record MemoriaNoteGlobManyToken : MemoriaNoteGlobToken;

internal readonly record struct MemoriaNoteGlobCharacterRange(Rune Start, Rune End);

internal sealed record MemoriaNoteGlobCharacterClassToken : MemoriaNoteGlobToken
{
    internal MemoriaNoteGlobCharacterClassToken(
        bool isNegated,
        IReadOnlyList<MemoriaNoteGlobCharacterRange> ranges)
    {
        IsNegated = isNegated;
        Ranges = new ReadOnlyCollection<MemoriaNoteGlobCharacterRange>([.. ranges]);
    }

    internal bool IsNegated { get; }

    internal IReadOnlyList<MemoriaNoteGlobCharacterRange> Ranges { get; }
}

/// <summary>Represents one validated, provider-independent MemoriaNote glob.</summary>
internal sealed class MemoriaNoteGlob
{
    internal const int MaximumSourceScalarCount = 512;

    private static readonly MemoriaNoteGlob MatchAllValue = new(
        [new MemoriaNoteGlobManyToken()],
        string.Empty);

    private readonly IReadOnlyList<MemoriaNoteGlobToken> _tokens;

    internal MemoriaNoteGlob(
        IReadOnlyList<MemoriaNoteGlobToken> tokens,
        string fixedLiteralPrefix)
    {
        _tokens = new ReadOnlyCollection<MemoriaNoteGlobToken>([.. tokens]);
        FixedLiteralPrefix = fixedLiteralPrefix;
    }

    internal static MemoriaNoteGlob MatchAll => MatchAllValue;

    internal IReadOnlyList<MemoriaNoteGlobToken> Tokens => _tokens;

    internal string FixedLiteralPrefix { get; }

    internal bool IsMatchAll =>
        _tokens.Count == 1 && _tokens[0] is MemoriaNoteGlobManyToken;
}

/// <summary>Parses the application glob dialect without exposing provider syntax.</summary>
internal static class MemoriaNoteGlobParser
{
    internal static bool TryParse(
        string source,
        out MemoriaNoteGlob? glob,
        out MemoriaNoteGlobParseError error)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length == 0)
        {
            glob = null;
            error = MemoriaNoteGlobParseError.Empty;
            return false;
        }

        if (!TryReadScalars(source, out var scalars, out error))
        {
            glob = null;
            return false;
        }

        var tokens = new List<MemoriaNoteGlobToken>();
        var prefix = new StringBuilder();
        var prefixComplete = false;
        for (var index = 0; index < scalars.Count;)
        {
            var scalar = scalars[index];
            if (scalar.Value == '\\')
            {
                index++;
                if (index == scalars.Count)
                {
                    glob = null;
                    error = MemoriaNoteGlobParseError.DanglingEscape;
                    return false;
                }

                var literal = scalars[index++];
                tokens.Add(new MemoriaNoteGlobLiteralToken(literal));
                if (!prefixComplete)
                    prefix.Append(literal.ToString());
                continue;
            }

            if (scalar.Value == '*')
            {
                index++;
                prefixComplete = true;
                if (tokens.Count == 0 || tokens[^1] is not MemoriaNoteGlobManyToken)
                    tokens.Add(new MemoriaNoteGlobManyToken());
                continue;
            }

            if (scalar.Value == '?')
            {
                index++;
                prefixComplete = true;
                tokens.Add(new MemoriaNoteGlobAnyToken());
                continue;
            }

            if (scalar.Value == '[')
            {
                prefixComplete = true;
                if (!TryParseCharacterClass(scalars, ref index, out var token, out error))
                {
                    glob = null;
                    return false;
                }

                tokens.Add(token!);
                continue;
            }

            index++;
            tokens.Add(new MemoriaNoteGlobLiteralToken(scalar));
            if (!prefixComplete)
                prefix.Append(scalar.ToString());
        }

        glob = new MemoriaNoteGlob(tokens, prefix.ToString());
        error = MemoriaNoteGlobParseError.None;
        return true;
    }

    private static bool TryReadScalars(
        string source,
        out IReadOnlyList<Rune> scalars,
        out MemoriaNoteGlobParseError error)
    {
        var result = new List<Rune>();
        var remaining = source.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                scalars = Array.Empty<Rune>();
                error = MemoriaNoteGlobParseError.InvalidUnicode;
                return false;
            }
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                scalars = Array.Empty<Rune>();
                error = MemoriaNoteGlobParseError.ControlCharacter;
                return false;
            }

            result.Add(rune);
            if (result.Count > MemoriaNoteGlob.MaximumSourceScalarCount)
            {
                scalars = Array.Empty<Rune>();
                error = MemoriaNoteGlobParseError.SourceTooLong;
                return false;
            }
            remaining = remaining[consumed..];
        }

        scalars = result;
        error = MemoriaNoteGlobParseError.None;
        return true;
    }

    private static bool TryParseCharacterClass(
        IReadOnlyList<Rune> scalars,
        ref int index,
        out MemoriaNoteGlobCharacterClassToken? token,
        out MemoriaNoteGlobParseError error)
    {
        index++;
        var isNegated = index < scalars.Count &&
            scalars[index].Value is '!' or '^';
        if (isNegated)
            index++;

        var ranges = new List<MemoriaNoteGlobCharacterRange>();
        while (index < scalars.Count && scalars[index].Value != ']')
        {
            if (!TryReadClassAtom(scalars, ref index, out var start, out error))
            {
                token = null;
                return false;
            }

            if (index < scalars.Count && scalars[index].Value == '-')
            {
                index++;
                if (index == scalars.Count || scalars[index].Value == ']')
                {
                    token = null;
                    error = MemoriaNoteGlobParseError.InvalidRange;
                    return false;
                }
                if (!TryReadClassAtom(scalars, ref index, out var end, out error))
                {
                    token = null;
                    return false;
                }
                if (start.Value > end.Value)
                {
                    token = null;
                    error = MemoriaNoteGlobParseError.ReversedRange;
                    return false;
                }

                ranges.Add(new MemoriaNoteGlobCharacterRange(start, end));
            }
            else
            {
                ranges.Add(new MemoriaNoteGlobCharacterRange(start, start));
            }
        }

        if (index == scalars.Count)
        {
            token = null;
            error = MemoriaNoteGlobParseError.UnclosedCharacterClass;
            return false;
        }
        if (ranges.Count == 0)
        {
            token = null;
            error = MemoriaNoteGlobParseError.EmptyCharacterClass;
            return false;
        }

        index++;
        token = new MemoriaNoteGlobCharacterClassToken(isNegated, ranges);
        error = MemoriaNoteGlobParseError.None;
        return true;
    }

    private static bool TryReadClassAtom(
        IReadOnlyList<Rune> scalars,
        ref int index,
        out Rune atom,
        out MemoriaNoteGlobParseError error)
    {
        if (scalars[index].Value == '\\')
        {
            index++;
            if (index == scalars.Count)
            {
                atom = default;
                error = MemoriaNoteGlobParseError.DanglingEscape;
                return false;
            }

            atom = scalars[index++];
            error = MemoriaNoteGlobParseError.None;
            return true;
        }
        if (scalars[index].Value == '-')
        {
            atom = default;
            error = MemoriaNoteGlobParseError.InvalidRange;
            return false;
        }

        atom = scalars[index++];
        error = MemoriaNoteGlobParseError.None;
        return true;
    }
}
