#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MemoriaNote.Domain.Queries;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Compiles validated MemoriaNote glob tokens into SQLite GLOB syntax.</summary>
internal static class SqliteGlobPatternCompiler
{
    internal const int MaximumUtf8ByteCount = 2048;

    internal static string Compile(MemoriaNoteGlob glob)
    {
        ArgumentNullException.ThrowIfNull(glob);
        var builder = new StringBuilder();
        foreach (var token in glob.Tokens)
        {
            switch (token)
            {
                case MemoriaNoteGlobLiteralToken literal:
                    AppendLiteral(builder, literal.Value);
                    break;
                case MemoriaNoteGlobAnyToken:
                    builder.Append('?');
                    break;
                case MemoriaNoteGlobManyToken:
                    builder.Append('*');
                    break;
                case MemoriaNoteGlobCharacterClassToken characterClass:
                    AppendCharacterClass(builder, characterClass);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(glob));
            }
        }

        var result = builder.ToString();
        if (Encoding.UTF8.GetByteCount(result) > MaximumUtf8ByteCount)
        {
            throw new ArgumentException(
                $"A compiled page-name pattern must contain at most " +
                $"{MaximumUtf8ByteCount} UTF-8 bytes.",
                nameof(glob));
        }

        return result;
    }

    private static void AppendLiteral(StringBuilder builder, Rune value)
    {
        switch (value.Value)
        {
            case '*':
                builder.Append("[*]");
                break;
            case '?':
                builder.Append("[?]");
                break;
            case '[':
                builder.Append("[[]");
                break;
            default:
                builder.Append(value.ToString());
                break;
        }
    }

    private static void AppendCharacterClass(
        StringBuilder builder,
        MemoriaNoteGlobCharacterClassToken characterClass)
    {
        var ranges = Normalize(characterClass.Ranges);
        if (!characterClass.IsNegated &&
            ranges.Count == 1 &&
            ranges[0].Start == ranges[0].End)
        {
            AppendLiteral(builder, ranges[0].Start);
            return;
        }

        builder.Append('[');
        if (characterClass.IsNegated)
            builder.Append('^');

        AppendAndRemove(builder, ranges, ']');
        AppendAndRemove(builder, ranges, '-');

        var deferPositiveCaret = !characterClass.IsNegated &&
            RemoveScalar(ranges, '^');
        foreach (var range in ranges)
        {
            builder.Append(range.Start.ToString());
            if (range.Start != range.End)
            {
                builder.Append('-');
                builder.Append(range.End.ToString());
            }
        }
        if (deferPositiveCaret)
            builder.Append('^');

        builder.Append(']');
    }

    private static List<MemoriaNoteGlobCharacterRange> Normalize(
        IReadOnlyList<MemoriaNoteGlobCharacterRange> source)
    {
        var ordered = source
            .OrderBy(range => range.Start.Value)
            .ThenBy(range => range.End.Value)
            .ToArray();
        var result = new List<MemoriaNoteGlobCharacterRange>();
        foreach (var range in ordered)
        {
            if (result.Count == 0 ||
                range.Start.Value > (long)result[^1].End.Value + 1)
            {
                result.Add(range);
                continue;
            }

            var previous = result[^1];
            if (range.End.Value > previous.End.Value)
            {
                result[^1] = new MemoriaNoteGlobCharacterRange(
                    previous.Start,
                    range.End);
            }
        }

        return result;
    }

    private static void AppendAndRemove(
        StringBuilder builder,
        List<MemoriaNoteGlobCharacterRange> ranges,
        int scalar)
    {
        if (RemoveScalar(ranges, scalar))
            builder.Append(new Rune(scalar).ToString());
    }

    private static bool RemoveScalar(
        List<MemoriaNoteGlobCharacterRange> ranges,
        int scalar)
    {
        for (var index = 0; index < ranges.Count; index++)
        {
            var range = ranges[index];
            if (scalar < range.Start.Value)
                return false;
            if (scalar > range.End.Value)
                continue;

            ranges.RemoveAt(index);
            if (range.Start.Value < scalar)
            {
                ranges.Insert(index++, new MemoriaNoteGlobCharacterRange(
                    range.Start,
                    new Rune(scalar - 1)));
            }
            if (scalar < range.End.Value)
            {
                ranges.Insert(index, new MemoriaNoteGlobCharacterRange(
                    new Rune(scalar + 1),
                    range.End));
            }
            return true;
        }

        return false;
    }
}
