using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies count aggregation across notes in a workgroup search.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class WorkgroupCountAggregationTests
{
    /// <summary>
    /// Verifies that workgroup paging uses every note's count for both search methods.
    /// </summary>
    /// <param name="searchMethod">The search method to exercise.</param>
    /// <param name="searchEntry">The matching search entry.</param>
    [TestCase(SearchMethodType.Heading, "*")]
    [TestCase(SearchMethodType.FullText, "marker")]
    public async Task SearchAsync_PagingAcrossNotesRetainsTheUnpagedTotalCount(
        SearchMethodType searchMethod,
        string searchEntry)
    {
        using var firstDatabase = new TemporaryNoteDatabase();
        using var secondDatabase = new TemporaryNoteDatabase();
        using var thirdDatabase = new TemporaryNoteDatabase();
        var firstNote = firstDatabase.CreateNote("first-note", "First Note");
        var secondNote = secondDatabase.CreateNote("second-note", "Second Note");
        var thirdNote = thirdDatabase.CreateNote("third-note", "Third Note");
        firstNote.CreatePage("Alpha", "Shared marker in alpha.");
        var firstBeta = firstNote.CreatePage("Beta", "Shared marker in beta.");
        var secondAlpha = secondNote.CreatePage("Alpha", "Shared marker in the second note.");
        thirdNote.CreatePage("Alpha", "Shared marker in the third note.");
        var workgroup = CreateWorkgroup(firstNote, secondNote, thirdNote);

        var result = await workgroup.SearchAsync(
            searchEntry,
            SearchRangeType.Workgroup,
            searchMethod,
            1,
            2,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(4));
            Assert.That(
                result.Contents.Select(content => content.Guid),
                Is.EqualTo(new[] { firstBeta.Guid, secondAlpha.Guid }));
            Assert.That(
                result.Contents.Select(content => content.OwnerDataSource),
                Is.EqualTo(new[]
                {
                    Path.GetFullPath(firstNote.DataSource),
                    Path.GetFullPath(secondNote.DataSource)
                }));
        }
    }

    /// <summary>
    /// Verifies that cancellation reaches workgroup count queries for both search methods.
    /// </summary>
    /// <param name="searchMethod">The search method to cancel.</param>
    [TestCase(SearchMethodType.Heading)]
    [TestCase(SearchMethodType.FullText)]
    public async Task SearchAsync_CancelledTokenCancelsCountAggregation(
        SearchMethodType searchMethod)
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        note.CreatePage("Alpha", "Cancellation marker.");
        var workgroup = CreateWorkgroup(note);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        OperationCanceledException? exception = null;

        try
        {
            await workgroup.SearchAsync(
                "marker",
                SearchRangeType.Workgroup,
                searchMethod,
                0,
                10,
                cancellation.Token);
        }
        catch (OperationCanceledException caught)
        {
            exception = caught;
        }

        Assert.That(exception, Is.Not.Null);
    }

    private static Workgroup CreateWorkgroup(params Note[] notes)
    {
        var workgroup = new Workgroup();
        foreach (var note in notes)
            workgroup.Notes.Add(note);

        workgroup.SelectedNote = notes.FirstOrDefault();
        return workgroup;
    }
}
