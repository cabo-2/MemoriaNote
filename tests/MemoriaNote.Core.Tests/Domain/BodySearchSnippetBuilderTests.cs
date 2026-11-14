using MemoriaNote.Domain.Queries;
using MemoriaNote.Domain.Repositories;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>Verifies bounded, sanitized body-search snippets and UTF-16 match spans.</summary>
[TestFixture]
public sealed class BodySearchSnippetBuilderTests
{
    /// <summary>Verifies controls are sanitized without invalidating visible match offsets.</summary>
    [Test]
    public void Build_ControlsAndRepeatedMatches_ReturnsSanitizedStructuredSnippet()
    {
        var snippet = BodySearchSnippetBuilder.Build(
            "before\nneedle\tand needle\u001bafter",
            ["needle"],
            RepositoryBodySearchCaseSensitivity.Sensitive);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet.Text, Is.EqualTo("before needle and needle\uFFFDafter"));
            Assert.That(
                snippet.Matches.Select(match => (match.Start, match.Length)),
                Is.EqualTo(new[] { (7, 6), (18, 6) }));
        }
    }

    /// <summary>Verifies overlapping keyword matches are merged into one ordered range.</summary>
    [Test]
    public void Build_OverlappingMatches_MergesRanges()
    {
        var snippet = BodySearchSnippetBuilder.Build(
            "ababa",
            ["aba", "bab"],
            RepositoryBodySearchCaseSensitivity.Sensitive);

        Assert.That(snippet.Matches, Is.EqualTo(new[] { new BodySearchMatch(0, 5) }));
    }

    /// <summary>Verifies non-BMP text uses UTF-16 offsets without splitting surrogate pairs.</summary>
    [Test]
    public void Build_NonBmpMatch_UsesUtf16Boundaries()
    {
        var snippet = BodySearchSnippetBuilder.Build(
            "x😀key😀",
            ["😀key"],
            RepositoryBodySearchCaseSensitivity.Sensitive);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet.Matches.Single().Start, Is.EqualTo(1));
            Assert.That(snippet.Matches.Single().Length, Is.EqualTo(5));
            Assert.That(snippet.Text.Substring(1, 5), Is.EqualTo("😀key"));
        }
    }

    /// <summary>Verifies long bodies are cut at scalar boundaries around the first match.</summary>
    [Test]
    public void Build_LongBody_ReturnsBoundedWindowContainingMatch()
    {
        var snippet = BodySearchSnippetBuilder.Build(
            new string('a', 500) + "needle" + new string('b', 500),
            ["needle"],
            RepositoryBodySearchCaseSensitivity.Sensitive);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet.Text.EnumerateRunes().Count(),
                Is.LessThanOrEqualTo(BodySearchSnippetBuilder.MaximumScalarCount));
            Assert.That(snippet.Matches, Has.Count.EqualTo(1));
            var match = snippet.Matches.Single();
            Assert.That(snippet.Text.Substring(match.Start, match.Length), Is.EqualTo("needle"));
        }
    }

    /// <summary>Verifies case-insensitive spans use the same comparison as first-match selection.</summary>
    [Test]
    public void Build_OrdinalIgnoreCase_ReturnsEveryCaseVariant()
    {
        var snippet = BodySearchSnippetBuilder.Build(
            "before MICROSOFT and Microsoft after",
            ["microsoft"],
            RepositoryBodySearchCaseSensitivity.Insensitive);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet.Text, Is.EqualTo("before MICROSOFT and Microsoft after"));
            Assert.That(
                snippet.Matches.Select(match => (match.Start, match.Length)),
                Is.EqualTo(new[] { (7, 9), (21, 9) }));
        }
    }
}
