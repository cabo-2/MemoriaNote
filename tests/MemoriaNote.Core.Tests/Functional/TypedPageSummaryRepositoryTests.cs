using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies owner-qualified page summaries at the SQLite persistence boundary.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class TypedPageSummaryRepositoryTests
{
    readonly SqliteNotebookDbContextFactory _databaseFactory =
        new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);

    /// <summary>
    /// Verifies that list results map every persisted summary field without navigation state.
    /// </summary>
    [Test]
    public async Task ReadPageSummariesAsync_MapsAllPersistedFieldsAndOwner()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("typed-summary", "Typed Summary");
        var repository = new SqlitePageRepository(_databaseFactory);
        var created = await repository.CreatePageAsync(
            database.DatabasePath,
            "Daily",
            "Body excluded from summary",
            "journal/2026",
            CancellationToken.None);

        var summaries = await repository.ListPageSummariesAsync(
            database.DatabasePath,
            0,
            10,
            CancellationToken.None);
        var summary = summaries.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.NotebookId, Is.EqualTo(NotebookId.FromDatabasePath(database.DatabasePath)));
            Assert.That(summary.PageId.Value, Is.EqualTo(created.Guid));
            Assert.That(summary.Name, Is.EqualTo(created.Name));
            Assert.That(summary.Index, Is.EqualTo(created.Index));
            Assert.That(summary.Tags[PageTag.Dir], Is.EqualTo("journal/2026"));
            Assert.That(summary.ContentType, Is.EqualTo(created.ContentType));
            Assert.That(summary.CreateTime, Is.EqualTo(created.CreateTime));
            Assert.That(summary.UpdateTime, Is.EqualTo(created.UpdateTime));
            Assert.That(summary.IsErased, Is.EqualTo(created.IsErased));
        }
    }

    /// <summary>
    /// Verifies typed UUID reads and the persisted UUID round trip.
    /// </summary>
    [Test]
    public async Task ReadPageAsync_PageId_RoundTripsPersistedUuid()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("typed-page", "Typed Page");
        var repository = new SqlitePageRepository(_databaseFactory);
        var created = await repository.CreatePageAsync(
            database.DatabasePath,
            "Typed",
            "Typed body",
            null!,
            CancellationToken.None);

        var read = await repository.FindPageAsync(
            database.DatabasePath,
            PageId.FromGuid(created.Guid),
            CancellationToken.None);

        Assert.That(read?.Guid, Is.EqualTo(created.Guid));
        using var context = _databaseFactory.CreateDbContext(database.DatabasePath);
        Assert.That(
            context.Pages.Single().Uuid,
            Is.EqualTo(PageId.FromGuid(created.Guid).ToString()));
    }

    /// <summary>
    /// Verifies both heading and full-text searches return typed owner-qualified summaries.
    /// </summary>
    /// <param name="searchMethod">The SQLite search path to exercise.</param>
    [TestCase(SearchMethodType.Heading)]
    [TestCase(SearchMethodType.FullText)]
    public async Task SearchAsync_ReturnsTypedOwner(
        SearchMethodType searchMethod)
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("typed-search", "Typed Search");
        var created = note.CreatePage("Marker", "Marker body");
        var repository = new SqlitePageSearchRepository(_databaseFactory);

        var notebookId = NotebookId.FromDatabasePath(database.DatabasePath);
        var result = await repository.SearchAsync(
            notebookId,
            "Marker",
            searchMethod,
            0,
            10,
            CancellationToken.None);
        var summary = result.Single();
        var count = await repository.CountMatchesAsync(
            notebookId,
            "Marker",
            searchMethod,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(summary.NotebookId, Is.EqualTo(NotebookId.FromDatabasePath(database.DatabasePath)));
            Assert.That(summary.PageId.Value, Is.EqualTo(created.Guid));
        }
    }
}
