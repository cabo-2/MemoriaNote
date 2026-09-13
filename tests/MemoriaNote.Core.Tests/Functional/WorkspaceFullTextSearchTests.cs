using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies the full-text search contract across every note in a workspace.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class WorkspaceFullTextSearchTests
{
    /// <summary>
    /// Verifies that the unified asynchronous search returns matches from every note.
    /// </summary>
    [Test]
    public async Task ExactQuery_ReturnsMatchesFromEveryNote()
    {
        using var firstDatabase = new TemporaryNoteDatabase();
        using var secondDatabase = new TemporaryNoteDatabase();
        var firstNote = firstDatabase.CreateNote("first-note", "First Note");
        var secondNote = secondDatabase.CreateNote("second-note", "Second Note");
        var firstZulu = firstNote.CreatePage("Zulu", "Shared marker in the first note.");
        var firstAlpha = firstNote.CreatePage("Alpha", "Another shared marker.");
        var secondBeta = secondNote.CreatePage("Beta", "Shared marker in the second note.");
        secondNote.CreatePage("Marker", "The heading alone contains the query.");
        var workspace = CreateWorkspace(firstNote, secondNote);

        var result = await workspace.SearchAsync(
            "marker",
            SearchRangeType.Workspace,
            SearchMethodType.FullText,
            0,
            10,
            CancellationToken.None);

        var expectedPageIds = new[] { firstAlpha.Guid, firstZulu.Guid, secondBeta.Guid };
        var expectedOwners = new[] { firstNote, firstNote, secondNote };
        AssertWorkspaceResult(result, 3, expectedPageIds, expectedOwners);
    }

    /// <summary>
    /// Verifies that an empty query returns every page in workspace display order.
    /// </summary>
    [Test]
    public async Task EmptyQuery_ReturnsEveryPage()
    {
        using var firstDatabase = new TemporaryNoteDatabase();
        using var secondDatabase = new TemporaryNoteDatabase();
        var firstNote = firstDatabase.CreateNote("first-note", "First Note");
        var secondNote = secondDatabase.CreateNote("second-note", "Second Note");
        var firstZulu = firstNote.CreatePage("Zulu", "First text");
        var firstAlpha = firstNote.CreatePage("Alpha", "Second text");
        var secondBeta = secondNote.CreatePage("Beta", "Third text");
        var workspace = CreateWorkspace(firstNote, secondNote);

        var result = await workspace.SearchAsync(
            "   ",
            SearchRangeType.Workspace,
            SearchMethodType.FullText,
            0,
            10,
            CancellationToken.None);

        var expectedPageIds = new[] { firstAlpha.Guid, firstZulu.Guid, secondBeta.Guid };
        var expectedOwners = new[] { firstNote, firstNote, secondNote };
        AssertWorkspaceResult(result, 3, expectedPageIds, expectedOwners);
    }

    /// <summary>
    /// Verifies that workspace searches retain the existing full-text wildcard behavior.
    /// </summary>
    [Test]
    public async Task WildcardQuery_RetainsTheNoteSearchContract()
    {
        using var firstDatabase = new TemporaryNoteDatabase();
        using var secondDatabase = new TemporaryNoteDatabase();
        var firstNote = firstDatabase.CreateNote("first-note", "First Note");
        var secondNote = secondDatabase.CreateNote("second-note", "Second Note");
        var firstExact = firstNote.CreatePage("First Exact", "The probe entered orbit safely.");
        firstNote.CreatePage("First Prefix", "The orbital station received the probe.");
        var secondExact = secondNote.CreatePage("Second Exact", "Another probe entered orbit.");
        secondNote.CreatePage("Second Prefix", "An orbiting probe reported back.");
        var workspace = CreateWorkspace(firstNote, secondNote);

        var result = await workspace.SearchAsync(
            "orbit*",
            SearchRangeType.Workspace,
            SearchMethodType.FullText,
            0,
            10,
            CancellationToken.None);

        var expectedPageIds = new[] { firstExact.Guid, secondExact.Guid };
        var expectedOwners = new[] { firstNote, secondNote };
        AssertWorkspaceResult(result, 2, expectedPageIds, expectedOwners);
    }

    /// <summary>
    /// Verifies that paging applies to the combined result sequence across note boundaries.
    /// </summary>
    [Test]
    public async Task PagingAcrossNotes_ReturnsTheGlobalSliceAndUnpagedCount()
    {
        using var firstDatabase = new TemporaryNoteDatabase();
        using var secondDatabase = new TemporaryNoteDatabase();
        var firstNote = firstDatabase.CreateNote("first-note", "First Note");
        var secondNote = secondDatabase.CreateNote("second-note", "Second Note");
        firstNote.CreatePage("Alpha", "Shared marker.");
        var firstBeta = firstNote.CreatePage("Beta", "Shared marker.");
        var secondAlpha = secondNote.CreatePage("Alpha", "Shared marker.");
        secondNote.CreatePage("Beta", "Shared marker.");
        var workspace = CreateWorkspace(firstNote, secondNote);

        var result = await workspace.SearchAsync(
            "marker",
            SearchRangeType.Workspace,
            SearchMethodType.FullText,
            1,
            2,
            CancellationToken.None);

        var expectedPageIds = new[] { firstBeta.Guid, secondAlpha.Guid };
        var expectedOwners = new[] { firstNote, secondNote };
        AssertWorkspaceResult(result, 4, expectedPageIds, expectedOwners);
    }

    private static Workspace CreateWorkspace(params Note[] notes)
    {
        return new Workspace(null, notes, notes.FirstOrDefault());
    }

    private static void AssertWorkspaceResult(
        SearchResult result,
        int expectedTotalCount,
        IEnumerable<Guid> expectedPageIds,
        IEnumerable<Note> expectedOwners)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(expectedTotalCount));
            Assert.That(
                result.Contents.Select(content => content.Guid),
                Is.EqualTo(expectedPageIds));
            Assert.That(
                result.Contents.Select(content => content.OwnerDataSource),
                Is.EqualTo(expectedOwners.Select(note => Path.GetFullPath(note.DataSource))));
            Assert.That(result.StartTime, Is.LessThanOrEqualTo(result.EndTime));
        }
    }
}
