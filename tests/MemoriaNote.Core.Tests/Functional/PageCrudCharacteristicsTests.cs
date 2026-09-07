using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies dictionary sense index management and page CRUD behavior against SQLite.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class PageCrudCharacteristicsTests
{
    /// <summary>
    /// Verifies that a created page can be read through every public identity-based API.
    /// </summary>
    [Test]
    public void CreatePage_PersistsFieldsAndSupportsAllReadPaths()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");

        var created = note.CreatePage("Entry", "Initial text", "journal/2026");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Rowid, Is.GreaterThan(0));
            Assert.That(created.Guid, Is.Not.EqualTo(Guid.Empty));
            Assert.That(created.Index, Is.EqualTo(1));
            // Content.Create<T> currently stores the generic parameter name.
            Assert.That(created.ContentType, Is.EqualTo("T"));
            Assert.That(created.TagDict[PageTag.Dir], Is.EqualTo("journal/2026"));
            Assert.That(created.CreateTime, Is.EqualTo(created.UpdateTime));
            Assert.That(created.IsErased, Is.False);
            Assert.That(note.Count, Is.EqualTo(1));
        }

        AssertPage(note.ReadPage("Entry", 1), created, "Initial text");
        AssertPage(note.ReadPage(created.Guid), created, "Initial text");
        AssertPage(note.ReadPage((IContent)created), created, "Initial text");

        var pagesByName = note.ReadPage("Entry").ToList();
        Assert.That(pagesByName, Has.Count.EqualTo(1));
        AssertPage(pagesByName[0], created, "Initial text");
    }

    /// <summary>
    /// Verifies that editing persists mutable fields without changing the sense index.
    /// </summary>
    [Test]
    public void UpdatePage_PersistsChangesAndPreservesSenseIndexes()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var first = note.CreatePage("Daily", "First text");
        var second = note.CreatePage("Daily", "Second text");
        var originalCreateTime = second.CreateTime;
        var originalUpdateTime = second.UpdateTime;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Index, Is.EqualTo(1));
            Assert.That(second.Index, Is.EqualTo(2));
        }

        second.Text = "Edited text";
        second.TagDict["Status"] = "Reviewed";
        note.UpdatePage(second);

        var relocatedSecond = note.ReadPage(second.Guid);
        var relocatedFirst = note.ReadPage(first.Guid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(relocatedSecond.Text, Is.EqualTo("Edited text"));
            Assert.That(relocatedSecond.TagDict["Status"], Is.EqualTo("Reviewed"));
            Assert.That(relocatedSecond.CreateTime, Is.EqualTo(originalCreateTime));
            Assert.That(relocatedSecond.UpdateTime, Is.GreaterThanOrEqualTo(originalUpdateTime));
            Assert.That(relocatedSecond.Index, Is.EqualTo(2));
            Assert.That(relocatedFirst.Index, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Verifies that renaming appends to the destination and compacts the source indexes.
    /// </summary>
    [Test]
    public void UpdatePage_WhenRenamed_AppendsAndCompactsAffectedGroups()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var firstDaily = note.CreatePage("Daily", "First daily text");
        var renamed = note.CreatePage("Daily", "Second daily text");
        var existingArchive = note.CreatePage("Archive", "Archived text");

        renamed.Name = "Archive";
        note.UpdatePage(renamed);

        var dailyPages = note.ReadPage("Daily").ToList();
        var archivePages = note.ReadPage("Archive").ToList();
        using var context = new NoteDbContext(database.DatabasePath);
        var dailyContents = context.Contents.Where(content => content.Name == "Daily").ToList();
        var archiveContents = context.Contents.Where(content => content.Name == "Archive").ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dailyPages, Has.Count.EqualTo(1));
            Assert.That(dailyPages[0].Guid, Is.EqualTo(firstDaily.Guid));
            Assert.That(dailyPages[0].Index, Is.EqualTo(1));
            Assert.That(archivePages, Has.Count.EqualTo(2));
            Assert.That(archivePages.Single(page => page.Guid == existingArchive.Guid).Index, Is.EqualTo(1));
            Assert.That(archivePages.Single(page => page.Guid == renamed.Guid).Index, Is.EqualTo(2));
            Assert.That(note.ReadPage(renamed.Guid)?.Name, Is.EqualTo("Archive"));
            Assert.That(renamed.Index, Is.EqualTo(2));
            Assert.That(dailyContents.Single().Index, Is.EqualTo(1));
            Assert.That(archiveContents.Select(content => content.Index), Is.EquivalentTo(new[] { 1, 2 }));
        }
    }

    /// <summary>
    /// Verifies that deletion removes pages and compacts the remaining indexes.
    /// </summary>
    [Test]
    public void DeletePage_RemovesTargetsAndCompactsRemainingIndexes()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var first = note.CreatePage("Daily", "First text");
        var second = note.CreatePage("Daily", "Second text");
        var separate = note.CreatePage("Separate", "Separate text");

        note.DeletePage((IContent)first);
        using var context = new NoteDbContext(database.DatabasePath);
        var remainingContent = context.Contents.Single(content => content.Uuid == second.Uuid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(note.ReadPage(first.Guid), Is.Null);
            Assert.That(note.ReadPage("Daily", 1)?.Guid, Is.EqualTo(second.Guid));
            Assert.That(note.ReadPage("Daily", 2), Is.Null);
            Assert.That(note.Count, Is.EqualTo(2));
            Assert.That(remainingContent.Index, Is.EqualTo(1));
        }

        note.DeletePage(separate.Rowid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(note.ReadPage(separate.Guid), Is.Null);
            Assert.That(note.ReadPage(second.Guid), Is.Not.Null);
            Assert.That(note.Count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Verifies that caller-supplied indexes cannot reorder dictionary senses during an edit.
    /// </summary>
    [Test]
    public void UpdatePage_WhenIndexIsChangedByCaller_PreservesManagedOrder()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var first = note.CreatePage("Term", "First meaning");
        var second = note.CreatePage("Term", "Second meaning");
        var third = note.CreatePage("Term", "Third meaning");

        second.Index = 99;
        second.Text = "Edited second meaning";
        note.UpdatePage(second);

        var pages = note.ReadPage("Term").OrderBy(page => page.Index).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pages.Select(page => page.Guid), Is.EqualTo(new[]
            {
                first.Guid,
                second.Guid,
                third.Guid
            }));
            Assert.That(pages.Select(page => page.Index), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(second.Index, Is.EqualTo(2));
        }
    }

    /// <summary>
    /// Verifies that a touched name group repairs existing gaps before appending a page.
    /// </summary>
    [Test]
    public void CreatePage_WhenIndexesContainGap_NormalizesTheGroup()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var first = note.CreatePage("Term", "First meaning");
        var second = note.CreatePage("Term", "Second meaning");

        using (var context = new NoteDbContext(database.DatabasePath))
        {
            context.Pages.Single(page => page.Uuid == second.Uuid).Index = 4;
            context.SaveChanges();
        }

        var third = note.CreatePage("Term", "Third meaning");
        var pages = note.ReadPage("Term").OrderBy(page => page.Index).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pages.Select(page => page.Guid), Is.EqualTo(new[]
            {
                first.Guid,
                second.Guid,
                third.Guid
            }));
            Assert.That(pages.Select(page => page.Index), Is.EqualTo(new[] { 1, 2, 3 }));
        }
    }

    /// <summary>
    /// Verifies that a failed rename rolls back the page and every affected index.
    /// </summary>
    [Test]
    public void UpdatePage_WhenPersistenceFails_RollsBackRenameAndIndexes()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var first = note.CreatePage("Source", "First source meaning");
        var renamed = note.CreatePage("Source", "Second source meaning");
        var destination = note.CreatePage("Destination", "Destination meaning");
        var renamedGuid = renamed.Guid;
        var renamedUpdateTime = renamed.UpdateTime;

        using (var context = new NoteDbContext(database.DatabasePath))
        {
            context.Database.ExecuteSqlRaw($@"
                CREATE TRIGGER FailPageRename
                BEFORE UPDATE ON Pages
                WHEN NEW.Uuid = '{renamed.Uuid:D}'
                BEGIN
                    SELECT RAISE(ABORT, 'forced page update failure');
                END;");
        }

        renamed.Name = "Destination";

        Action updatePage = () => note.UpdatePage(renamed);
        Assert.Catch<DbUpdateException>(updatePage);

        var sourcePages = note.ReadPage("Source").OrderBy(page => page.Index).ToList();
        var destinationPages = note.ReadPage("Destination").ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sourcePages.Select(page => page.Guid), Is.EqualTo(new[]
            {
                first.Guid,
                renamedGuid
            }));
            Assert.That(sourcePages.Select(page => page.Index), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(sourcePages[1].UpdateTime, Is.EqualTo(renamedUpdateTime));
            Assert.That(destinationPages, Has.Count.EqualTo(1));
            Assert.That(destinationPages[0].Guid, Is.EqualTo(destination.Guid));
        }
    }

    private static void AssertPage(Page? actual, Page expected, string expectedText)
    {
        Assert.That(actual, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual.Rowid, Is.EqualTo(expected.Rowid));
            Assert.That(actual.Guid, Is.EqualTo(expected.Guid));
            Assert.That(actual.Name, Is.EqualTo(expected.Name));
            Assert.That(actual.Index, Is.EqualTo(expected.Index));
            Assert.That(actual.Text, Is.EqualTo(expectedText));
            Assert.That(actual.TagDict, Is.EqualTo(expected.TagDict));
            Assert.That(actual.ContentType, Is.EqualTo(expected.ContentType));
            Assert.That(actual.CreateTime, Is.EqualTo(expected.CreateTime));
            Assert.That(actual.UpdateTime, Is.EqualTo(expected.UpdateTime));
            Assert.That(actual.IsErased, Is.EqualTo(expected.IsErased));
        }
    }
}
