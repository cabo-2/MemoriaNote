using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies that workgroup operations use the note that owns each search result.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class WorkgroupOwnershipTests
{
    /// <summary>
    /// Verifies that a workgroup search preserves each result's normalized owner data source.
    /// </summary>
    [Test]
    public void WorkgroupSearch_ReturnsTheOwnerForEveryResult()
    {
        using var firstDatabase = new TemporaryNoteDatabase();
        using var secondDatabase = new TemporaryNoteDatabase();
        var firstNote = firstDatabase.CreateNote("first-note", "First Note");
        var secondNote = secondDatabase.CreateNote("second-note", "Second Note");
        firstNote.CreatePage("First", "First text");
        secondNote.CreatePage("Second", "Second text");
        var workgroup = CreateWorkgroup(firstNote, firstNote, secondNote);

        var result = workgroup.SearchWorkgroupContents("*", 0, 10);

        Assert.That(result.Contents, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Contents[0].OwnerDataSource,
                Is.EqualTo(Path.GetFullPath(firstNote.DataSource)));
            Assert.That(
                result.Contents[1].OwnerDataSource,
                Is.EqualTo(Path.GetFullPath(secondNote.DataSource)));
        }
    }

    /// <summary>
    /// Verifies that reading and editing a non-selected note result affect only its owner.
    /// </summary>
    [Test]
    public void EditText_UsesTheOwnerInsteadOfTheSelectedNote()
    {
        using var selectedDatabase = new TemporaryNoteDatabase();
        using var ownerDatabase = new TemporaryNoteDatabase();
        var selectedNote = selectedDatabase.CreateNote("selected-note", "Selected Note");
        var ownerNote = ownerDatabase.CreateNote("owner-note", "Owner Note");
        var selectedPage = selectedNote.CreatePage("Shared", "Selected text");
        var ownerPage = ownerNote.CreatePage("Shared", "Owner text");
        var workgroup = CreateWorkgroup(selectedNote, selectedNote, ownerNote);
        var ownerResult = FindResult(workgroup, ownerNote, ownerPage.Guid);

        var openedPage = workgroup.ReadAll(ownerResult);
        var result = workgroup.EditText(ownerResult, "Edited owner text");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(openedPage.Guid, Is.EqualTo(ownerPage.Guid));
            Assert.That(openedPage.Text, Is.EqualTo("Owner text"));
            Assert.That(result.Result, Is.True);
            Assert.That(
                result.Content.OwnerDataSource,
                Is.EqualTo(Path.GetFullPath(ownerNote.DataSource)));
            Assert.That(selectedNote.ReadPage(selectedPage.Guid)?.Text, Is.EqualTo("Selected text"));
            Assert.That(ownerNote.ReadPage(ownerPage.Guid)?.Text, Is.EqualTo("Edited owner text"));
        }
    }

    /// <summary>
    /// Verifies that rename validation and persistence both use the owning note.
    /// </summary>
    [Test]
    public void RenameText_UsesTheOwnerForDuplicateChecksAndPersistence()
    {
        using var selectedDatabase = new TemporaryNoteDatabase();
        using var ownerDatabase = new TemporaryNoteDatabase();
        var selectedNote = selectedDatabase.CreateNote("selected-note", "Selected Note");
        var ownerNote = ownerDatabase.CreateNote("owner-note", "Owner Note");
        var selectedPage = selectedNote.CreatePage("Existing", "Selected text");
        var ownerPage = ownerNote.CreatePage("Original", "Owner text");
        var workgroup = CreateWorkgroup(selectedNote, selectedNote, ownerNote);
        var ownerResult = FindResult(workgroup, ownerNote, ownerPage.Guid);

        var result = workgroup.RenameText(ownerResult, "Existing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result, Is.True);
            Assert.That(selectedNote.ReadPage(selectedPage.Guid)?.Name, Is.EqualTo("Existing"));
            Assert.That(ownerNote.ReadPage(ownerPage.Guid)?.Name, Is.EqualTo("Existing"));
        }
    }

    /// <summary>
    /// Verifies that deletion cannot use a result's row identifier against another note.
    /// </summary>
    [Test]
    public void DeleteText_WithCollidingRowIds_DeletesOnlyTheOwnerPage()
    {
        using var selectedDatabase = new TemporaryNoteDatabase();
        using var ownerDatabase = new TemporaryNoteDatabase();
        var selectedNote = selectedDatabase.CreateNote("selected-note", "Selected Note");
        var ownerNote = ownerDatabase.CreateNote("owner-note", "Owner Note");
        var selectedPage = selectedNote.CreatePage("Selected", "Selected text");
        var ownerPage = ownerNote.CreatePage("Owner", "Owner text");
        var workgroup = CreateWorkgroup(selectedNote, selectedNote, ownerNote);
        var ownerResult = FindResult(workgroup, ownerNote, ownerPage.Guid);

        Assert.That(ownerPage.Rowid, Is.EqualTo(selectedPage.Rowid));

        var result = workgroup.DeleteText(ownerResult);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result, Is.True);
            Assert.That(selectedNote.ReadPage(selectedPage.Guid), Is.Not.Null);
            Assert.That(ownerNote.ReadPage(ownerPage.Guid), Is.Null);
        }
    }

    /// <summary>
    /// Verifies that write permissions are determined from the owner rather than the selection.
    /// </summary>
    [Test]
    public void ManageText_UsesTheOwnersReadOnlySetting()
    {
        using var selectedDatabase = new TemporaryNoteDatabase();
        using var ownerDatabase = new TemporaryNoteDatabase();
        var selectedNote = selectedDatabase.CreateNote("selected-note", "Selected Note");
        var ownerNote = ownerDatabase.CreateNote("owner-note", "Owner Note");
        selectedNote.CreatePage("Selected", "Selected text");
        var ownerPage = ownerNote.CreatePage("Owner", "Owner text");
        var workgroup = CreateWorkgroup(selectedNote, selectedNote, ownerNote);
        var ownerResult = FindResult(workgroup, ownerNote, ownerPage.Guid);

        selectedNote.Metadata.ReadOnly = true;
        var allowedResult = workgroup.EditText(ownerResult, "Allowed owner edit");

        selectedNote.Metadata.ReadOnly = false;
        ownerNote.Metadata.ReadOnly = true;
        var editResult = workgroup.EditText(ownerResult, "Blocked edit");
        var renameResult = workgroup.RenameText(ownerResult, "Blocked rename");
        var deleteResult = workgroup.DeleteText(ownerResult);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allowedResult.Result, Is.True);
            Assert.That(editResult.Result, Is.False);
            Assert.That(renameResult.Result, Is.False);
            Assert.That(deleteResult.Result, Is.False);
            Assert.That(ownerNote.ReadPage(ownerPage.Guid)?.Name, Is.EqualTo("Owner"));
            Assert.That(ownerNote.ReadPage(ownerPage.Guid)?.Text, Is.EqualTo("Allowed owner edit"));
        }
    }

    /// <summary>
    /// Verifies that an unknown owner rejects all mutations without changing any note.
    /// </summary>
    [Test]
    public void ManageText_WithUnknownOwner_RejectsEveryMutation()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var page = note.CreatePage("Original", "Original text");
        var workgroup = CreateWorkgroup(note, note);
        var content = page.GetContent();
        content.OwnerDataSource = Path.Combine(database.DirectoryPath, "unknown.db");

        var editResult = workgroup.EditText(content, "Changed text");
        var renameResult = workgroup.RenameText(content, "Changed name");
        var deleteResult = workgroup.DeleteText(content);

        var persisted = note.ReadPage(page.Guid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(editResult.Result, Is.False);
            Assert.That(renameResult.Result, Is.False);
            Assert.That(deleteResult.Result, Is.False);
            Assert.That(persisted?.Name, Is.EqualTo("Original"));
            Assert.That(persisted?.Text, Is.EqualTo("Original text"));
        }
    }

    private static Workgroup CreateWorkgroup(Note selectedNote, params Note[] notes)
    {
        var workgroup = new Workgroup();
        foreach (var note in notes)
            workgroup.Notes.Add(note);

        workgroup.SelectedNote = selectedNote;
        return workgroup;
    }

    private static Content FindResult(Workgroup workgroup, Note owner, Guid pageId)
    {
        var result = workgroup.SearchWorkgroupContents("*", 0, 10);
        var content = result.Contents.Single(item => item.Guid == pageId);
        Assert.That(
            content.OwnerDataSource,
            Is.EqualTo(Path.GetFullPath(owner.DataSource)));
        return content;
    }
}
