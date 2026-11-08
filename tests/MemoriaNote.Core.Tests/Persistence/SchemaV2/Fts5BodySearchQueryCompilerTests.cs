using MemoriaNote.Domain.Queries;
using MemoriaNote.Persistence.SchemaV2;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies safe FTS5 query compilation and snippet normalization.</summary>
[TestFixture]
public sealed class Fts5BodySearchQueryCompilerTests
{
    /// <summary>Verifies every keyword is quoted and joined without exposing FTS operators.</summary>
    [Test]
    public void Compile_OperatorLikeKeywords_QuotesEveryLiteral()
    {
        var query = Parse("AND OR \"quoted\" column:*");

        var compiled = Fts5BodySearchQueryCompiler.Compile(query);

        Assert.That(
            compiled,
            Is.EqualTo("\"AND\" AND \"OR\" AND \"\"\"quoted\"\"\" AND \"column:*\""));
    }

    /// <summary>Verifies marked snippets become sanitized text with UTF-16 spans.</summary>
    [Test]
    public void Parse_MarkedControlText_ReturnsStructuredSnippet()
    {
        var snippet = Fts5BodySearchSnippetParser.Parse(
            "before\n<s>SQLITE</e>\tand <s>sqlite</e>\u001bafter",
            "<s>",
            "</e>",
            sourceContainsMarker: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet.Text, Is.EqualTo("before SQLITE and sqlite\uFFFDafter"));
            Assert.That(
                snippet.Matches.Select(match => (match.Start, match.Length)),
                Is.EqualTo(new[] { (7, 6), (18, 6) }));
        }
    }

    /// <summary>Verifies source marker collisions fail instead of producing ambiguous spans.</summary>
    [Test]
    public void Parse_SourceContainsMarker_ThrowsInvalidData()
    {
        Assert.That(
            () => Fts5BodySearchSnippetParser.Parse(
                "<s>match</e>",
                "<s>",
                "</e>",
                sourceContainsMarker: true),
            Throws.InstanceOf<InvalidDataException>());
    }

    /// <summary>Verifies snippet bounds preserve scalar and match boundaries.</summary>
    [Test]
    public void Parse_LongUnicodeText_BoundsWithoutSplittingMatch()
    {
        var marked = new string('x', 200) + "<s>😀needle</e>" + new string('y', 200);

        var snippet = Fts5BodySearchSnippetParser.Parse(
            marked,
            "<s>",
            "</e>",
            sourceContainsMarker: false);

        var match = snippet.Matches.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet.Text.EnumerateRunes().Count(), Is.EqualTo(240));
            Assert.That(snippet.Text.Substring(match.Start, match.Length), Is.EqualTo("😀needle"));
            Assert.That(char.IsLowSurrogate(snippet.Text[match.Start]), Is.False);
        }
    }

    private static BodySearchQuery Parse(string source)
    {
        Assert.That(
            BodySearchQueryParser.TryParse(source, out var query, out var error),
            Is.True,
            error.ToString());
        return query!;
    }
}
