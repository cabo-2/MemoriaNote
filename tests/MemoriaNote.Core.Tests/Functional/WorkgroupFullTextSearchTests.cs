using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies the full-text search contract across every note in a workgroup.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class WorkgroupFullTextSearchTests
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
        var workgroup = CreateWorkgroup(firstNote, secondNote);

        var result = await workgroup.SearchAsync(
            "marker",
            SearchRangeType.Workgroup,
            SearchMethodType.FullText,
            0,
            10,
            CancellationToken.None);

        var expectedPageIds = new[] { firstAlpha.Guid, firstZulu.Guid, secondBeta.Guid };
        var expectedOwners = new[] { firstNote, firstNote, secondNote };
        AssertWorkgroupResult(result, 3, expectedPageIds, expectedOwners);
    }

    /// <summary>
    /// Verifies that an empty query returns every page in workgroup display order.
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
        var workgroup = CreateWorkgroup(firstNote, secondNote);

        var result = await workgroup.SearchAsync(
            "   ",
            SearchRangeType.Workgroup,
            SearchMethodType.FullText,
            0,
            10,
            CancellationToken.None);

        var expectedPageIds = new[] { firstAlpha.Guid, firstZulu.Guid, secondBeta.Guid };
        var expectedOwners = new[] { firstNote, firstNote, secondNote };
        AssertWorkgroupResult(result, 3, expectedPageIds, expectedOwners);
    }

    /// <summary>
    /// Verifies that workgroup searches retain the existing full-text wildcard behavior.
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
        var workgroup = CreateWorkgroup(firstNote, secondNote);

        var result = await workgroup.SearchAsync(
            "orbit*",
            SearchRangeType.Workgroup,
            SearchMethodType.FullText,
            0,
            10,
            CancellationToken.None);

        var expectedPageIds = new[] { firstExact.Guid, secondExact.Guid };
        var expectedOwners = new[] { firstNote, secondNote };
        AssertWorkgroupResult(result, 2, expectedPageIds, expectedOwners);
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
        var workgroup = CreateWorkgroup(firstNote, secondNote);

        var result = await workgroup.SearchAsync(
            "marker",
            SearchRangeType.Workgroup,
            SearchMethodType.FullText,
            1,
            2,
            CancellationToken.None);

        var expectedPageIds = new[] { firstBeta.Guid, secondAlpha.Guid };
        var expectedOwners = new[] { firstNote, secondNote };
        AssertWorkgroupResult(result, 4, expectedPageIds, expectedOwners);
    }

    private static Workgroup CreateWorkgroup(params Note[] notes)
    {
        var workgroup = new Workgroup();
        foreach (var note in notes)
            workgroup.Notes.Add(note);

        workgroup.SelectedNote = notes.FirstOrDefault();
        return workgroup;
    }

    private static void AssertWorkgroupResult(
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
