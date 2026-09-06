using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Captures the current heading and full-text search contracts against SQLite.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class SearchContractTests
{
    /// <summary>
    /// Verifies that a heading query without wildcards matches the complete heading only.
    /// </summary>
    [Test]
    public void HeadingSearch_ExactQuery_ReturnsOnlyTheCompleteHeading()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var firstExact = note.CreatePage("Alpha", "First exact heading");
        var secondExact = note.CreatePage("Alpha", "Second exact heading");
        note.CreatePage("Alphabet", "Longer heading");
        note.CreatePage("Beta Alpha", "Term in a longer heading");

        var result = note.SearchContents("Alpha", 0, 10);

        AssertSearchResult(result, note, 2, firstExact.Guid, secondExact.Guid);
    }

    /// <summary>
    /// Verifies the glob-style wildcard behavior of heading searches.
    /// </summary>
    [Test]
    public void HeadingSearch_WildcardQueries_MatchExpectedHeadings()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var alpha = note.CreatePage("Alpha", "First heading");
        var alphabet = note.CreatePage("Alphabet", "Longer heading");
        var alphas = note.CreatePage("Alphas", "Plural heading");
        note.CreatePage("Alpine", "Different prefix");
        note.CreatePage("Beta", "Unrelated heading");

        var prefixResult = note.SearchContents("Alpha*", 0, 10);
        var singleCharacterResult = note.SearchContents("Alpha?", 0, 10);

        AssertSearchResult(prefixResult, note, 3, alpha.Guid, alphabet.Guid, alphas.Guid);
        AssertSearchResult(singleCharacterResult, note, 1, alphas.Guid);
    }

    /// <summary>
    /// Verifies that an empty heading query returns every page in heading and index order.
    /// </summary>
    [Test]
    public void HeadingSearch_EmptyQuery_ReturnsEveryPageInDisplayOrder()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var beta = note.CreatePage("Beta", "Beta text");
        var firstAlpha = note.CreatePage("Alpha", "First alpha text");
        var secondAlpha = note.CreatePage("Alpha", "Second alpha text");
        var gamma = note.CreatePage("Gamma", "Gamma text");

        var result = note.SearchContents(string.Empty, 0, 10);

        AssertSearchResult(
            result,
            note,
            4,
            firstAlpha.Guid,
            secondAlpha.Guid,
            beta.Guid,
            gamma.Guid);
    }

    /// <summary>
    /// Verifies that full-text queries match body tokens and do not search headings.
    /// </summary>
    [Test]
    public void FullTextSearch_ExactQuery_MatchesBodyTokensOnly()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var alpha = note.CreatePage("Alpha", "An amber comet crosses the sky.");
        var beta = note.CreatePage("Beta", "A cobalt comet remains visible.");
        note.CreatePage("Comet", "This body mentions only a planet.");

        var result = note.SearchFullText("comet", 0, 10);

        AssertSearchResult(result, note, 2, alpha.Guid, beta.Guid);
    }

    /// <summary>
    /// Captures that a full-text wildcard currently behaves like an exact FTS token.
    /// </summary>
    [Test]
    public void FullTextSearch_WildcardQuery_CurrentlyDoesNotExpandTheFtsToken()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var exactToken = note.CreatePage("Exact", "The probe entered orbit safely.");
        note.CreatePage("Prefix", "The orbital station received the probe.");

        var result = note.SearchFullText("orbit*", 0, 10);

        AssertSearchResult(result, note, 1, exactToken.Guid);
    }

    /// <summary>
    /// Verifies that an empty full-text query returns every page in heading and index order.
    /// </summary>
    [Test]
    public void FullTextSearch_EmptyQuery_ReturnsEveryPageInDisplayOrder()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var beta = note.CreatePage("Beta", "Beta text");
        var alpha = note.CreatePage("Alpha", "Alpha text");
        var gamma = note.CreatePage("Gamma", "Gamma text");

        var result = note.SearchFullText("   ", 0, 10);

        AssertSearchResult(result, note, 3, alpha.Guid, beta.Guid, gamma.Guid);
    }

    /// <summary>
    /// Verifies that paging returns the requested ordered slice while retaining the total count.
    /// </summary>
    [Test]
    public void SearchMethods_PagingReturnsOrderedSliceAndUnpagedTotalCount()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        note.CreatePage("Delta", "Shared marker in delta.");
        note.CreatePage("Alpha", "Shared marker in alpha.");
        var charlie = note.CreatePage("Charlie", "Shared marker in charlie.");
        var bravo = note.CreatePage("Bravo", "Shared marker in bravo.");

        var headingResult = note.SearchContents("*", 1, 2);
        var fullTextResult = note.SearchFullText("marker", 1, 2);

        AssertSearchResult(headingResult, note, 4, bravo.Guid, charlie.Guid);
        AssertSearchResult(fullTextResult, note, 4, bravo.Guid, charlie.Guid);
    }

    /// <summary>
    /// Verifies that asynchronous heading and full-text results retain their owner identity.
    /// </summary>
    [Test]
    public async Task SearchMethodsAsync_ReturnResultsWithTheirOwner()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var page = note.CreatePage("Async", "Asynchronous owner marker.");

        var headingResult = await note.SearchContentsAsync(
            "Async",
            0,
            10,
            CancellationToken.None);
        var fullTextResult = await note.SearchFullTextAsync(
            "marker",
            0,
            10,
            CancellationToken.None);

        AssertSearchResult(headingResult, note, 1, page.Guid);
        AssertSearchResult(fullTextResult, note, 1, page.Guid);
    }

    private static void AssertSearchResult(
        SearchResult result,
        Note expectedParent,
        int expectedTotalCount,
        params Guid[] expectedPageIds)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(expectedTotalCount));
            Assert.That(
                result.Contents.Select(content => content.Guid),
                Is.EqualTo(expectedPageIds));
            Assert.That(
                result.Contents.Select(content => content.Parent),
                Is.All.SameAs(expectedParent));
            Assert.That(
                result.Contents.Select(content => content.OwnerDataSource),
                Is.All.EqualTo(Path.GetFullPath(expectedParent.DataSource)));
            Assert.That(result.StartTime, Is.LessThanOrEqualTo(result.EndTime));
        }
    }
}
