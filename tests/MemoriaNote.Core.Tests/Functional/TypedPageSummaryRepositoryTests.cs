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
    readonly SqliteNoteDatabaseFactory _databaseFactory =
        new SqliteNoteDatabaseFactory(NullLoggerFactory.Instance);

    /// <summary>
    /// Verifies that list results map every persisted summary field without navigation state.
    /// </summary>
    [Test]
    public async Task ReadPageSummariesAsync_MapsAllPersistedFieldsAndOwner()
    {
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("typed-summary", "Typed Summary");
        var repository = new SqliteNoteRepository(_databaseFactory);
        var created = await repository.CreatePageAsync(
            database.DatabasePath,
            "Daily",
            "Body excluded from summary",
            "journal/2026",
            CancellationToken.None);

        var summaries = await repository.ReadPageSummariesAsync(
            database.DatabasePath,
            0,
            10,
            CancellationToken.None);
        var summary = summaries.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.NoteId, Is.EqualTo(NoteId.FromDataSource(database.DatabasePath)));
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
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("typed-page", "Typed Page");
        var repository = new SqliteNoteRepository(_databaseFactory);
        var created = await repository.CreatePageAsync(
            database.DatabasePath,
            "Typed",
            "Typed body",
            null!,
            CancellationToken.None);

        var read = await repository.ReadPageAsync(
            database.DatabasePath,
            PageId.FromGuid(created.Guid),
            CancellationToken.None);

        Assert.That(read?.Guid, Is.EqualTo(created.Guid));
        using var context = _databaseFactory.Create(database.DatabasePath);
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
    public async Task SearchPageSummariesAsync_ReturnsTypedOwner(
        SearchMethodType searchMethod)
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("typed-search", "Typed Search");
        var created = note.CreatePage("Marker", "Marker body");
        var repository = new SqliteNoteSearchRepository(_databaseFactory);

        var result = await repository.SearchPageSummariesAsync(
            database.DatabasePath,
            "Marker",
            searchMethod,
            0,
            10,
            CancellationToken.None);
        var summary = result.PageSummaries.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(summary.NoteId, Is.EqualTo(NoteId.FromDataSource(database.DatabasePath)));
            Assert.That(summary.PageId.Value, Is.EqualTo(created.Guid));
        }
    }
}
