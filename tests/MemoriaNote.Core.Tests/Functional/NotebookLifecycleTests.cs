using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies the core note lifecycle against a migrated SQLite database.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class NotebookLifecycleTests
{
    /// <summary>
    /// Verifies that page changes remain synchronized with heading and full-text search.
    /// </summary>
    [Test]
    public async Task NewDatabase_CanCompletePageLifecycleAndSynchronizeSearchIndexes()
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("test-note", "Test Note");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(database.DatabasePath), Is.True);
            Assert.That(note.Metadata.Name, Is.EqualTo("test-note"));
            Assert.That(note.Metadata.Title, Is.EqualTo("Test Note"));
            Assert.That(note.Metadata.Version, Is.EqualTo(NotebookDbContext.CurrentVersion));
        }

        using (var context = new NotebookDbContext(database.DatabasePath))
        {
            Assert.That(
                context.Database.GetAppliedMigrations(),
                Is.EqualTo(new[]
                {
                    "20230404004629_InitialCreate",
                    "20230404004813_FtsIndexTable"
                }));
        }

        var page = note.CreatePage("Welcome", "A quasar appears in this note.");

        AssertSingleResult(await SearchAsync(note, "Welcome", SearchMethodType.Heading), page.Guid);
        AssertSingleResult(await SearchAsync(note, "quasar", SearchMethodType.FullText), page.Guid);

        var storedPage = note.ReadPage(page.Guid);
        storedPage.Name = "Renamed";
        storedPage.Text = "A nebula remains in this note.";
        note.UpdatePage(storedPage);

        AssertEmptyResult(await SearchAsync(note, "Welcome", SearchMethodType.Heading));
        AssertSingleResult(await SearchAsync(note, "Renamed", SearchMethodType.Heading), page.Guid);
        AssertEmptyResult(await SearchAsync(note, "quasar", SearchMethodType.FullText));
        AssertSingleResult(await SearchAsync(note, "nebula", SearchMethodType.FullText), page.Guid);

        note.DeletePage(storedPage.Guid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(note.ReadPage(page.Guid), Is.Null);
            Assert.That(note.Count, Is.Zero);
        }
        AssertEmptyResult(await SearchAsync(note, "Renamed", SearchMethodType.Heading));
        AssertEmptyResult(await SearchAsync(note, "nebula", SearchMethodType.FullText));

        using var finalContext = new NotebookDbContext(database.DatabasePath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finalContext.Pages.Count(), Is.Zero);
            Assert.That(finalContext.Contents.Count(), Is.Zero);
        }
    }

    private static Task<SearchPage> SearchAsync(
        Notebook note,
        string searchEntry,
        SearchMethodType searchMethod)
    {
        var notebookId = NotebookId.FromDatabasePath(note.DatabasePath);
        var repository = new SqlitePageSearchRepository(
            new SqliteNotebookDbContextFactory(
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance));
        return new SearchUseCase(repository).SearchAsync(
            SearchRequest.ForNotebook(
                searchEntry,
                searchMethod,
                notebookId,
                0,
                10),
            CancellationToken.None);
    }

    private static void AssertSingleResult(SearchPage result, Guid expectedPageId)
    {
        Assert.That(result.TotalCount, Is.EqualTo(1));
        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(result.Items[0].PageId.Value, Is.EqualTo(expectedPageId));
    }

    private static void AssertEmptyResult(SearchPage result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalCount, Is.Zero);
            Assert.That(result.Items, Is.Empty);
        }
    }
}
