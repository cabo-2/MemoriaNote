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
        using var firstDatabase = new TemporaryNotebookDatabase();
        using var secondDatabase = new TemporaryNotebookDatabase();
        var firstNote = firstDatabase.CreateNotebook("first-note", "First Note");
        var secondNote = secondDatabase.CreateNotebook("second-note", "Second Note");
        var firstZulu = firstNote.CreatePage("Zulu", "Shared marker in the first note.");
        var firstAlpha = firstNote.CreatePage("Alpha", "Another shared marker.");
        var secondBeta = secondNote.CreatePage("Beta", "Shared marker in the second note.");
        secondNote.CreatePage("Marker", "The heading alone contains the query.");
        var workspace = CreateWorkspace(firstNote, secondNote);

        var result = await SearchAsync(
            workspace,
            "marker",
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
        using var firstDatabase = new TemporaryNotebookDatabase();
        using var secondDatabase = new TemporaryNotebookDatabase();
        var firstNote = firstDatabase.CreateNotebook("first-note", "First Note");
        var secondNote = secondDatabase.CreateNotebook("second-note", "Second Note");
        var firstZulu = firstNote.CreatePage("Zulu", "First text");
        var firstAlpha = firstNote.CreatePage("Alpha", "Second text");
        var secondBeta = secondNote.CreatePage("Beta", "Third text");
        var workspace = CreateWorkspace(firstNote, secondNote);

        var result = await SearchAsync(
            workspace,
            "   ",
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
        using var firstDatabase = new TemporaryNotebookDatabase();
        using var secondDatabase = new TemporaryNotebookDatabase();
        var firstNote = firstDatabase.CreateNotebook("first-note", "First Note");
        var secondNote = secondDatabase.CreateNotebook("second-note", "Second Note");
        var firstExact = firstNote.CreatePage("First Exact", "The probe entered orbit safely.");
        firstNote.CreatePage("First Prefix", "The orbital station received the probe.");
        var secondExact = secondNote.CreatePage("Second Exact", "Another probe entered orbit.");
        secondNote.CreatePage("Second Prefix", "An orbiting probe reported back.");
        var workspace = CreateWorkspace(firstNote, secondNote);

        var result = await SearchAsync(
            workspace,
            "orbit*",
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
        using var firstDatabase = new TemporaryNotebookDatabase();
        using var secondDatabase = new TemporaryNotebookDatabase();
        var firstNote = firstDatabase.CreateNotebook("first-note", "First Note");
        var secondNote = secondDatabase.CreateNotebook("second-note", "Second Note");
        firstNote.CreatePage("Alpha", "Shared marker.");
        var firstBeta = firstNote.CreatePage("Beta", "Shared marker.");
        var secondAlpha = secondNote.CreatePage("Alpha", "Shared marker.");
        secondNote.CreatePage("Beta", "Shared marker.");
        var workspace = CreateWorkspace(firstNote, secondNote);

        var result = await SearchAsync(
            workspace,
            "marker",
            SearchMethodType.FullText,
            1,
            2,
            CancellationToken.None);

        var expectedPageIds = new[] { firstBeta.Guid, secondAlpha.Guid };
        var expectedOwners = new[] { firstNote, secondNote };
        AssertWorkspaceResult(result, 4, expectedPageIds, expectedOwners);
    }

    private static Workspace CreateWorkspace(params Notebook[] notes)
    {
        return new Workspace(null, notes, notes.FirstOrDefault());
    }

    private static void AssertWorkspaceResult(
        SearchPage result,
        int expectedTotalCount,
        IEnumerable<Guid> expectedPageIds,
        IEnumerable<Notebook> expectedOwners)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalCount, Is.EqualTo(expectedTotalCount));
            Assert.That(
                result.Items.Select(summary => summary.PageId.Value),
                Is.EqualTo(expectedPageIds));
            Assert.That(
                result.Items.Select(summary => summary.NotebookId),
                Is.EqualTo(expectedOwners.Select(note =>
                    NotebookId.FromDatabasePath(note.DatabasePath))));
        }
    }

    private static Task<SearchPage> SearchAsync(
        Workspace workspace,
        string query,
        SearchMethodType method,
        int offset,
        int limit,
        CancellationToken token)
    {
        var request = SearchRequest.ForWorkspace(
            query,
            method,
            workspace.Notebooks.Select(notebook =>
                NotebookId.FromDatabasePath(notebook.DatabasePath)),
            offset,
            limit);
        return ApplicationComposition.Compose(workspace)
            .ApplicationService
            .SearchAsync(request, token);
    }
}
