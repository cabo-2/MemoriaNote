using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies count aggregation across notes in a workspace search.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class WorkspaceCountAggregationTests
{
    /// <summary>
    /// Verifies that workspace paging uses every note's count for both search methods.
    /// </summary>
    /// <param name="searchMethod">The search method to exercise.</param>
    /// <param name="searchEntry">The matching search entry.</param>
    [TestCase(SearchMethodType.Heading, "*")]
    [TestCase(SearchMethodType.FullText, "marker")]
    public async Task SearchAsync_PagingAcrossNotesRetainsTheUnpagedTotalCount(
        SearchMethodType searchMethod,
        string searchEntry)
    {
        using var firstDatabase = new TemporaryNotebookDatabase();
        using var secondDatabase = new TemporaryNotebookDatabase();
        using var thirdDatabase = new TemporaryNotebookDatabase();
        var firstNote = firstDatabase.CreateNotebook("first-note", "First Note");
        var secondNote = secondDatabase.CreateNotebook("second-note", "Second Note");
        var thirdNote = thirdDatabase.CreateNotebook("third-note", "Third Note");
        firstNote.CreatePage("Alpha", "Shared marker in alpha.");
        var firstBeta = firstNote.CreatePage("Beta", "Shared marker in beta.");
        var secondAlpha = secondNote.CreatePage("Alpha", "Shared marker in the second note.");
        thirdNote.CreatePage("Alpha", "Shared marker in the third note.");
        var workspace = CreateWorkspace(firstNote, secondNote, thirdNote);

        var result = await SearchAsync(
            workspace,
            searchEntry,
            searchMethod,
            1,
            2,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalCount, Is.EqualTo(4));
            Assert.That(
                result.Items.Select(summary => summary.PageId.Value),
                Is.EqualTo(new[] { firstBeta.Guid, secondAlpha.Guid }));
            Assert.That(
                result.Items.Select(summary => summary.NotebookId),
                Is.EqualTo(new[]
                {
                    NotebookId.FromDatabasePath(firstNote.DatabasePath),
                    NotebookId.FromDatabasePath(secondNote.DatabasePath)
                }));
        }
    }

    /// <summary>
    /// Verifies that cancellation reaches workspace count queries for both search methods.
    /// </summary>
    /// <param name="searchMethod">The search method to cancel.</param>
    [TestCase(SearchMethodType.Heading)]
    [TestCase(SearchMethodType.FullText)]
    public async Task SearchAsync_CancelledTokenCancelsCountAggregation(
        SearchMethodType searchMethod)
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("test-note", "Test Note");
        note.CreatePage("Alpha", "Cancellation marker.");
        var workspace = CreateWorkspace(note);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        OperationCanceledException? exception = null;

        try
        {
            await SearchAsync(
                workspace,
                "marker",
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

    private static Workspace CreateWorkspace(params Notebook[] notes)
    {
        return new Workspace(null, notes, notes.FirstOrDefault());
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
