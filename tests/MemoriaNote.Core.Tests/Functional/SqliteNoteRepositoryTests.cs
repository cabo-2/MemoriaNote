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
public sealed class SqliteNoteRepositoryTests
{
    readonly INoteRepository _repository =
        new SqliteNoteRepository(
            new SqliteNoteDatabaseFactory(NullLoggerFactory.Instance));

    /// <summary>
    /// Verifies materialized reads and mutations through the repository interface.
    /// </summary>
    [Test]
    public async Task PageOperations_PreserveManagedIndexesAndReadModels()
    {
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("test-note", "Test Note");
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

        var dailyPages = await _repository.ReadPagesAsync(
            database.DatabasePath,
            "Daily",
            CancellationToken.None);
        var archivePages = await _repository.ReadPagesAsync(
            database.DatabasePath,
            "Archive",
            CancellationToken.None);
        var byId = await _repository.ReadPageAsync(
            database.DatabasePath,
            second.Guid,
            CancellationToken.None);
        var byName = await _repository.ReadPageAsync(
            database.DatabasePath,
            "Archive",
            2,
            CancellationToken.None);
        var contents = await _repository.ReadContentsAsync(
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
            Assert.That(contents.Select(content => content.Guid), Is.EquivalentTo(new[]
            {
                first.Guid,
                second.Guid,
                archive.Guid
            }));
            Assert.That(
                contents.All(content => content.OwnerDataSource == Path.GetFullPath(database.DatabasePath)),
                Is.True);
            Assert.That(
                await _repository.CountAsync(database.DatabasePath, CancellationToken.None),
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

        var remainingArchive = await _repository.ReadPagesAsync(
            database.DatabasePath,
            "Archive",
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingArchive.Select(page => page.Guid), Is.EqualTo(new[] { archive.Guid }));
            Assert.That(remainingArchive.Select(page => page.Index), Is.EqualTo(new[] { 1 }));
            Assert.That(
                await _repository.CountAsync(database.DatabasePath, CancellationToken.None),
                Is.EqualTo(2));
        }
    }

    /// <summary>
    /// Verifies that colliding row identifiers cannot update or delete another note.
    /// </summary>
    [Test]
    public async Task Mutations_UseDataSourceAndUuidInsteadOfRowId()
    {
        using var firstDatabase = new TemporaryNoteDatabase();
        using var secondDatabase = new TemporaryNoteDatabase();
        var firstNote = firstDatabase.CreateNote("first", "First Note");
        var secondNote = secondDatabase.CreateNote("second", "Second Note");
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
    /// Verifies that a pre-cancelled token prevents repository database access.
    /// </summary>
    [Test]
    public void Operations_PreCancelledToken_ThrowOperationCanceledException()
    {
        using var database = new TemporaryNoteDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            await _repository.ReadPageAsync(
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
