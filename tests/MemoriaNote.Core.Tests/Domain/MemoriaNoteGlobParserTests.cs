using MemoriaNote.Domain.Queries;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>Verifies the provider-independent MemoriaNote glob grammar.</summary>
[TestFixture]
public sealed class MemoriaNoteGlobParserTests
{
    /// <summary>Verifies literals, escapes, wildcards, classes, and fixed prefixes are parsed.</summary>
    [Test]
    public void TryParse_ValidPattern_ReturnsCanonicalTokensAndPrefix()
    {
        var parsed = MemoriaNoteGlobParser.TryParse(
            "project-\\**?[!a-z][^0-9]",
            out var glob,
            out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True);
            Assert.That(error, Is.EqualTo(MemoriaNoteGlobParseError.None));
            Assert.That(glob!.FixedLiteralPrefix, Is.EqualTo("project-*"));
            Assert.That(glob.Tokens.OfType<MemoriaNoteGlobManyToken>().Count(), Is.EqualTo(1));
            Assert.That(glob.Tokens.OfType<MemoriaNoteGlobAnyToken>().Count(), Is.EqualTo(1));
            Assert.That(
                glob.Tokens.OfType<MemoriaNoteGlobCharacterClassToken>()
                    .Select(token => token.IsNegated),
                Is.EqualTo(new[] { true, true }));
        }
    }

    /// <summary>Verifies consecutive many-wildcards canonicalize to one match-all token.</summary>
    [Test]
    public void TryParse_OnlyManyWildcards_ReturnsMatchAll()
    {
        Assert.That(
            MemoriaNoteGlobParser.TryParse("***", out var glob, out _),
            Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(glob!.IsMatchAll, Is.True);
            Assert.That(glob.Tokens, Has.Count.EqualTo(1));
            Assert.That(glob.FixedLiteralPrefix, Is.Empty);
        }
    }

    /// <summary>Verifies escaped class metacharacters can be literals and range endpoints.</summary>
    [Test]
    public void TryParse_EscapedClassMetacharacters_PreservesScalarRanges()
    {
        Assert.That(
            MemoriaNoteGlobParser.TryParse(
                "[\\]-a][\\--0]",
                out var glob,
                out var error),
            Is.True,
            error.ToString());

        var classes = glob!.Tokens.OfType<MemoriaNoteGlobCharacterClassToken>().ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(classes, Has.Length.EqualTo(2));
            Assert.That(classes[0].Ranges.Single().Start.Value, Is.EqualTo(']'));
            Assert.That(classes[0].Ranges.Single().End.Value, Is.EqualTo('a'));
            Assert.That(classes[1].Ranges.Single().Start.Value, Is.EqualTo('-'));
            Assert.That(classes[1].Ranges.Single().End.Value, Is.EqualTo('0'));
        }
    }

    /// <summary>Verifies invalid syntax and unsafe values produce stable parse reasons.</summary>
    [TestCase("", "Empty")]
    [TestCase("abc\\", "DanglingEscape")]
    [TestCase("[abc", "UnclosedCharacterClass")]
    [TestCase("[]", "EmptyCharacterClass")]
    [TestCase("[!]", "EmptyCharacterClass")]
    [TestCase("[a-]", "InvalidRange")]
    [TestCase("[-a]", "InvalidRange")]
    [TestCase("[z-a]", "ReversedRange")]
    [TestCase("a\u0001b", "ControlCharacter")]
    public void TryParse_InvalidPattern_ReturnsReason(
        string source,
        string expected)
    {
        var parsed = MemoriaNoteGlobParser.TryParse(source, out var glob, out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.False);
            Assert.That(glob, Is.Null);
            Assert.That(error.ToString(), Is.EqualTo(expected));
        }
    }

    /// <summary>Verifies invalid UTF-16 and the source scalar bound are rejected.</summary>
    [Test]
    public void TryParse_InvalidUnicodeOrOversizedSource_ReturnsReason()
    {
        Assert.That(
            MemoriaNoteGlobParser.TryParse("\ud800", out _, out var unicodeError),
            Is.False);
        Assert.That(unicodeError, Is.EqualTo(MemoriaNoteGlobParseError.InvalidUnicode));

        Assert.That(
            MemoriaNoteGlobParser.TryParse(
                new string('a', MemoriaNoteGlob.MaximumSourceScalarCount + 1),
                out _,
                out var lengthError),
            Is.False);
        Assert.That(lengthError, Is.EqualTo(MemoriaNoteGlobParseError.SourceTooLong));
    }

    /// <summary>Verifies one supplementary character is one wildcard scalar.</summary>
    [Test]
    public void TryParse_SupplementaryLiteral_CountsOneScalar()
    {
        Assert.That(
            MemoriaNoteGlobParser.TryParse("😀?", out var glob, out _),
            Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(glob!.Tokens, Has.Count.EqualTo(2));
            Assert.That(glob.FixedLiteralPrefix, Is.EqualTo("😀"));
            Assert.That(
                ((MemoriaNoteGlobLiteralToken)glob.Tokens[0]).Value.Value,
                Is.EqualTo(0x1f600));
        }
    }
}
