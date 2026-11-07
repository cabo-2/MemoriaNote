#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using MemoriaNote.Application;

namespace MemoriaNote.Domain.Queries;

internal enum BodySearchQueryParseError
{
    None,
    Empty,
    InvalidUnicode,
    ControlCharacter,
    QueryTooLong,
    KeywordTooLong,
    TooManyKeywords
}

/// <summary>Represents validated literal body-search keywords.</summary>
internal sealed class BodySearchQuery
{
    private readonly IReadOnlyList<string> _keywords;

    internal BodySearchQuery(IReadOnlyList<string> keywords)
    {
        _keywords = new ReadOnlyCollection<string>([.. keywords]);
    }

    internal IReadOnlyList<string> Keywords => _keywords;
}

/// <summary>Parses the provider-independent body-search query contract.</summary>
internal static class BodySearchQueryParser
{
    internal static bool TryParse(
        string source,
        out BodySearchQuery? query,
        out BodySearchQueryParseError error)
    {
        ArgumentNullException.ThrowIfNull(source);

        var keywords = new List<string>();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var keyword = new StringBuilder();
        var keywordScalarCount = 0;
        var queryScalarCount = 0;
        var remaining = source.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                query = null;
                error = BodySearchQueryParseError.InvalidUnicode;
                return false;
            }

            queryScalarCount++;
            if (queryScalarCount > NotebookBodySearchRequest.MaximumQueryScalarCount)
            {
                query = null;
                error = BodySearchQueryParseError.QueryTooLong;
                return false;
            }

            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                query = null;
                error = BodySearchQueryParseError.ControlCharacter;
                return false;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                if (!TryAddKeyword(
                        keywords,
                        distinct,
                        keyword,
                        ref keywordScalarCount,
                        out error))
                {
                    query = null;
                    return false;
                }
            }
            else
            {
                keyword.Append(rune.ToString());
                keywordScalarCount++;
                if (keywordScalarCount > NotebookBodySearchRequest.MaximumKeywordScalarCount)
                {
                    query = null;
                    error = BodySearchQueryParseError.KeywordTooLong;
                    return false;
                }
            }

            remaining = remaining[consumed..];
        }

        if (!TryAddKeyword(
                keywords,
                distinct,
                keyword,
                ref keywordScalarCount,
                out error))
        {
            query = null;
            return false;
        }

        if (keywords.Count == 0)
        {
            query = null;
            error = BodySearchQueryParseError.Empty;
            return false;
        }

        query = new BodySearchQuery(keywords);
        error = BodySearchQueryParseError.None;
        return true;
    }

    private static bool TryAddKeyword(
        ICollection<string> keywords,
        ISet<string> distinct,
        StringBuilder keyword,
        ref int keywordScalarCount,
        out BodySearchQueryParseError error)
    {
        if (keywordScalarCount == 0)
        {
            error = BodySearchQueryParseError.None;
            return true;
        }

        var value = keyword.ToString();
        keyword.Clear();
        keywordScalarCount = 0;
        if (!distinct.Add(value))
        {
            error = BodySearchQueryParseError.None;
            return true;
        }

        if (keywords.Count == NotebookBodySearchRequest.MaximumKeywordCount)
        {
            error = BodySearchQueryParseError.TooManyKeywords;
            return false;
        }

        keywords.Add(value);
        error = BodySearchQueryParseError.None;
        return true;
    }
}
