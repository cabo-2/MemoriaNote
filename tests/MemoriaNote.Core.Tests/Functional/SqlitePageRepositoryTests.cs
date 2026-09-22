using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies the page use-case contract implemented by the SQLite note repository.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class SqlitePageRepositoryTests
{
    readonly IPageRepository _repository =
        new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));

    /// <summary>Verifies Page ID prefix lookup is ordered and limited in SQLite.</summary>
    [Test]
    public async Task ListPagesByIdPrefixAsync_FiltersOrdersAndLimitsMatches()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("prefix", "Prefix");
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        using (var context = factory.CreateDbContext(database.DatabasePath))
        {
            var second = Page.Create("Second", "Body");
            second.Guid = Guid.Parse("abcd0000-0000-0000-0000-000000000002");
            var other = Page.Create("Other", "Body");
            other.Guid = Guid.Parse("ffff0000-0000-0000-0000-000000000001");
            var first = Page.Create("First", "Body");
            first.Guid = Guid.Parse("abcd0000-0000-0000-0000-000000000001");
            context.Pages.AddRange(second, other, first);
            await context.SaveChangesAsync();
        }

        var result = await _repository.ListPagesByIdPrefixAsync(
            database.DatabasePath,
            "abcd",
            2,
            CancellationToken.None);
        var limited = await _repository.ListPagesByIdPrefixAsync(
            database.DatabasePath,
            "abcd",
            1,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Select(page => page.Guid),
                Is.EqualTo(new[]
                {
                    Guid.Parse("abcd0000-0000-0000-0000-000000000001"),
                    Guid.Parse("abcd0000-0000-0000-0000-000000000002")
                }));
            Assert.That(limited.Select(page => page.Name), Is.EqualTo(new[] { "First" }));
        }
    }

    /// <summary>
    /// Verifies listing applies binary page-name order, stable tie-breakers, and limit in SQLite.
    /// </summary>
    [Test]
    public async Task ListPageSummariesAsync_OrdersByNameIndexAndPageIdBeforeLimit()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("ordered", "Ordered");
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var firstTieId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondTieId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        using (var context = factory.CreateDbContext(database.DatabasePath))
        {
            var lower = Page.Create("alpha", "lower");
            var upper = Page.Create("Alpha", "upper");
            var secondTie = Page.Create("Same", "second");
            secondTie.Guid = secondTieId;
            secondTie.Index = 1;
            var firstTie = Page.Create("Same", "first");
            firstTie.Guid = firstTieId;
            firstTie.Index = 1;
            context.Pages.AddRange(lower, secondTie, upper, firstTie);
            await context.SaveChangesAsync();
        }

        var result = await _repository.ListPageSummariesAsync(
            database.DatabasePath,
            0,
            3,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Select(page => page.Name), Is.EqualTo(new[]
            {
                "Alpha",
                "Same",
                "Same"
            }));
            Assert.That(
                result.Skip(1).Select(page => page.PageId.Value),
                Is.EqualTo(new[] { firstTieId, secondTieId }));
        }
    }

    /// <summary>
    /// Verifies materialized reads and mutations through the repository interface.
    /// </summary>
    [Test]
    public async Task PageOperations_PreserveManagedIndexesAndReadModels()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("test-note", "Test Note");
        var first = await _repository.CreatePageAsync(
            database.DatabasePath,
            "Daily",
            "First text",
            null!,
            CancellationToken.None);
        var second = await _repository.CreatePageAsync(
            database.DatabasePath,
            "Daily",
            "Second text",
            "journal/2026",
            CancellationToken.None);
        var archive = await _repository.CreatePageAsync(
            database.DatabasePath,
            "Archive",
            "Archive text",
            null!,
            CancellationToken.None);

        second.Index = 99;
        second.Name = "Archive";
        second.Text = "Updated text";
        var updated = await _repository.UpdatePageAsync(
            database.DatabasePath,
            second,
            CancellationToken.None);

        var dailyPages = await _repository.ListPagesByHeadingAsync(
            database.DatabasePath,
            "Daily",
            CancellationToken.None);
        var archivePages = await _repository.ListPagesByHeadingAsync(
            database.DatabasePath,
            "Archive",
            CancellationToken.None);
        var byId = await _repository.FindPageAsync(
            database.DatabasePath,
            second.Guid,
            CancellationToken.None);
        var byName = await _repository.FindPageAsync(
            database.DatabasePath,
            "Archive",
            2,
            CancellationToken.None);
        var contents = await _repository.ListPageSummariesAsync(
            database.DatabasePath,
            0,
            10,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dailyPages.Select(page => page.Guid), Is.EqualTo(new[] { first.Guid }));
            Assert.That(dailyPages.Select(page => page.Index), Is.EqualTo(new[] { 1 }));
            Assert.That(
                archivePages.Select(page => page.Guid),
                Is.EqualTo(new[] { archive.Guid, second.Guid }));
            Assert.That(archivePages.Select(page => page.Index), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(updated.Index, Is.EqualTo(2));
            Assert.That(updated.Text, Is.EqualTo("Updated text"));
            Assert.That(byId?.Guid, Is.EqualTo(second.Guid));
            Assert.That(byName?.Guid, Is.EqualTo(second.Guid));
            Assert.That(contents.Select(summary => summary.PageId.Value), Is.EquivalentTo(new[]
            {
                first.Guid,
                second.Guid,
                archive.Guid
            }));
            Assert.That(
                contents.All(summary =>
                    summary.NotebookId == NotebookId.FromDatabasePath(database.DatabasePath)),
                Is.True);
            Assert.That(
                await _repository.CountPagesAsync(database.DatabasePath, CancellationToken.None),
                Is.EqualTo(3));
        }

        await _repository.DeletePageAsync(
            database.DatabasePath,
            second.Guid,
            CancellationToken.None);
        await _repository.DeletePageAsync(
            database.DatabasePath,
            Guid.NewGuid(),
            CancellationToken.None);

        var remainingArchive = await _repository.ListPagesByHeadingAsync(
            database.DatabasePath,
            "Archive",
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingArchive.Select(page => page.Guid), Is.EqualTo(new[] { archive.Guid }));
            Assert.That(remainingArchive.Select(page => page.Index), Is.EqualTo(new[] { 1 }));
            Assert.That(
                await _repository.CountPagesAsync(database.DatabasePath, CancellationToken.None),
                Is.EqualTo(2));
        }
    }

    /// <summary>
    /// Verifies that colliding row identifiers cannot update or delete another note.
    /// </summary>
    [Test]
    public async Task Mutations_UseDataSourceAndUuidInsteadOfRowId()
    {
        using var firstDatabase = new TemporaryNotebookDatabase();
        using var secondDatabase = new TemporaryNotebookDatabase();
        var firstNote = firstDatabase.CreateNotebook("first", "First Note");
        var secondNote = secondDatabase.CreateNotebook("second", "Second Note");
        var firstPage = firstNote.CreatePage("First", "First text");
        var secondPage = secondNote.CreatePage("Second", "Second text");
        secondPage.Text = "Changed text";

        Assert.That(secondPage.Rowid, Is.EqualTo(firstPage.Rowid));
        Assert.ThrowsAsync<KeyNotFoundException>(new Func<Task>(async () =>
            await _repository.UpdatePageAsync(
                firstDatabase.DatabasePath,
                secondPage,
                CancellationToken.None)));

        await _repository.DeletePageAsync(
            firstDatabase.DatabasePath,
            secondPage.Guid,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstNote.ReadPage(firstPage.Guid)?.Text, Is.EqualTo("First text"));
            Assert.That(secondNote.ReadPage(secondPage.Guid)?.Text, Is.EqualTo("Second text"));
        }
    }

    /// <summary>
    /// Verifies typed deletion reports whether the transaction found its target.
    /// </summary>
    [Test]
    public async Task TryDeletePageAsync_ReturnsTheTransactionalOutcome()
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("typed-delete", "Typed Delete");
        var page = note.CreatePage("Existing", "Text");
        var notebookId = NotebookId.FromDatabasePath(database.DatabasePath);
        var pageId = PageId.FromGuid(page.Guid);

        var deleted = await _repository.TryDeletePageAsync(
            notebookId,
            pageId,
            CancellationToken.None);
        var missing = await _repository.TryDeletePageAsync(
            notebookId,
            pageId,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(missing, Is.False);
            Assert.That(note.ReadPage(page.Guid), Is.Null);
        }
    }

    /// <summary>Verifies page text compare-and-update distinguishes success, conflict, and absence.</summary>
    [Test]
    public async Task TryUpdatePageTextAsync_UsesExpectedTextAtomically()
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("conditional-edit", "Conditional Edit");
        var page = note.CreatePage("Existing", "Original");
        var notebookId = NotebookId.FromDatabasePath(database.DatabasePath);
        var pageId = PageId.FromGuid(page.Guid);

        var updated = await _repository.TryUpdatePageTextAsync(
            notebookId,
            pageId,
            "Original",
            "Updated",
            CancellationToken.None);
        var conflict = await _repository.TryUpdatePageTextAsync(
            notebookId,
            pageId,
            "Original",
            "Overwritten",
            CancellationToken.None);
        var missing = await _repository.TryUpdatePageTextAsync(
            notebookId,
            PageId.FromGuid(Guid.NewGuid()),
            "Original",
            "Replacement",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Status, Is.EqualTo(PageTextUpdateStatus.Success));
            Assert.That(updated.Page?.Guid, Is.EqualTo(page.Guid));
            Assert.That(conflict.Status, Is.EqualTo(PageTextUpdateStatus.Conflict));
            Assert.That(missing.Status, Is.EqualTo(PageTextUpdateStatus.PageNotFound));
            Assert.That(note.ReadPage(page.Guid)?.Text, Is.EqualTo("Updated"));
        }
    }

    /// <summary>
    /// Verifies that a pre-cancelled token prevents repository database access.
    /// </summary>
    [Test]
    public void Operations_PreCancelledToken_ThrowOperationCanceledException()
    {
        using var database = new TemporaryNotebookDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            await _repository.FindPageAsync(
                database.DatabasePath,
                Guid.NewGuid(),
                cancellation.Token)));
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            await _repository.CreatePageAsync(
                database.DatabasePath,
                "Cancelled",
                "Cancelled text",
                null!,
                cancellation.Token)));
        Assert.That(File.Exists(database.DatabasePath), Is.False);
    }
}
