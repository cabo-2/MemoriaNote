using MemoriaNote.Application;
using MemoriaNote.Domain.Queries;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>Verifies provider-independent body-search query parsing and bounds.</summary>
[TestFixture]
public sealed class BodySearchQueryParserTests
{
    /// <summary>Verifies Unicode whitespace splitting, ordinal deduplication, and literal text.</summary>
    [Test]
    public void TryParse_ValidQuery_ReturnsDistinctLiteralKeywords()
    {
        var success = BodySearchQueryParser.TryParse(
            "  sqlite\u00A0migration sqlite AND ' OR 1=1 --  ",
            out var query,
            out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(success, Is.True);
            Assert.That(error, Is.EqualTo(BodySearchQueryParseError.None));
            Assert.That(
                query!.Keywords,
                Is.EqualTo(new[] { "sqlite", "migration", "AND", "'", "OR", "1=1", "--" }));
        }
    }

    /// <summary>Verifies empty and unsafe Unicode input is rejected before storage access.</summary>
    /// <param name="source">The invalid source query.</param>
    /// <param name="expected">The expected parse failure.</param>
    [TestCase("   \u00A0", (int)BodySearchQueryParseError.Empty)]
    [TestCase("safe\tunsafe", (int)BodySearchQueryParseError.ControlCharacter)]
    [TestCase("safe\0unsafe", (int)BodySearchQueryParseError.ControlCharacter)]
    public void TryParse_InvalidOrEmptyQuery_ReturnsStableFailure(
        string source,
        int expectedValue)
    {
        var expected = (BodySearchQueryParseError)expectedValue;
        var success = BodySearchQueryParser.TryParse(source, out var query, out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(success, Is.False);
            Assert.That(query, Is.Null);
            Assert.That(error, Is.EqualTo(expected));
        }
    }

    /// <summary>Verifies an unpaired surrogate is rejected as invalid UTF-16.</summary>
    [Test]
    public void TryParse_UnpairedSurrogate_ReturnsInvalidUnicode()
    {
        var source = new string(['\uD800']);

        AssertFailure(source, BodySearchQueryParseError.InvalidUnicode);
    }

    /// <summary>Verifies each independently documented resource bound.</summary>
    [Test]
    public void TryParse_ResourceBounds_ReturnStableFailures()
    {
        var longKeyword = new string(
            'a',
            NotebookBodySearchRequest.MaximumKeywordScalarCount + 1);
        var tooManyKeywords = string.Join(
            ' ',
            Enumerable.Range(0, NotebookBodySearchRequest.MaximumKeywordCount + 1)
                .Select(index => $"k{index}"));
        var longQuery = string.Join(
            ' ',
            Enumerable.Repeat(
                new string('a', NotebookBodySearchRequest.MaximumKeywordScalarCount),
                8));

        AssertFailure(longKeyword, BodySearchQueryParseError.KeywordTooLong);
        AssertFailure(tooManyKeywords, BodySearchQueryParseError.TooManyKeywords);
        AssertFailure(longQuery, BodySearchQueryParseError.QueryTooLong);
    }

    private static void AssertFailure(string source, BodySearchQueryParseError expected)
    {
        Assert.That(
            BodySearchQueryParser.TryParse(source, out _, out var error),
            Is.False);
        Assert.That(error, Is.EqualTo(expected));
    }
}
