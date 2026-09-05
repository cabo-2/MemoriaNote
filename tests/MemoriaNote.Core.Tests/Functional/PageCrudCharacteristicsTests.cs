using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Captures the current page CRUD and index relocation behavior against SQLite.
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
    /// Verifies that editing persists mutable fields and moves the newest page to index one.
    /// </summary>
    [Test]
    public void UpdatePage_PersistsChangesAndRelocatesSameNamePages()
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
            Assert.That(relocatedSecond.Index, Is.EqualTo(1));
            Assert.That(relocatedFirst.Index, Is.EqualTo(2));
        }
    }

    /// <summary>
    /// Captures the incomplete index relocation that currently occurs when renaming a page.
    /// </summary>
    [Test]
    public void UpdatePage_WhenRenamed_LeavesGapAndDuplicateIndex()
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dailyPages, Has.Count.EqualTo(1));
            Assert.That(dailyPages[0].Guid, Is.EqualTo(firstDaily.Guid));
            Assert.That(dailyPages[0].Index, Is.EqualTo(2));
            Assert.That(archivePages, Has.Count.EqualTo(2));
            Assert.That(
                archivePages.Select(page => page.Guid),
                Is.EquivalentTo(new[] { renamed.Guid, existingArchive.Guid }));
            Assert.That(archivePages.Select(page => page.Index), Is.All.EqualTo(1));
            Assert.That(note.ReadPage(renamed.Guid)?.Name, Is.EqualTo("Archive"));
        }
    }

    /// <summary>
    /// Verifies that deletion removes pages but currently leaves the remaining indexes unchanged.
    /// </summary>
    [Test]
    public void DeletePage_RemovesTargetsWithoutRelocatingRemainingPages()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var first = note.CreatePage("Daily", "First text");
        var second = note.CreatePage("Daily", "Second text");
        var separate = note.CreatePage("Separate", "Separate text");

        note.DeletePage((IContent)first);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(note.ReadPage(first.Guid), Is.Null);
            Assert.That(note.ReadPage("Daily", 1), Is.Null);
            Assert.That(note.ReadPage("Daily", 2)?.Guid, Is.EqualTo(second.Guid));
            Assert.That(note.Count, Is.EqualTo(2));
        }

        note.DeletePage(separate.Rowid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(note.ReadPage(separate.Guid), Is.Null);
            Assert.That(note.ReadPage(second.Guid), Is.Not.Null);
            Assert.That(note.Count, Is.EqualTo(1));
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
