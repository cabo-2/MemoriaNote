using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies that workspace operations use the note that owns each search result.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class WorkspaceOwnershipTests
{
    /// <summary>
    /// Verifies that a workspace search preserves each result's normalized owner data source.
    /// </summary>
    [Test]
    public async Task WorkspaceSearch_ReturnsTheOwnerForEveryResult()
    {
        using var firstDatabase = new TemporaryNoteDatabase();
        using var secondDatabase = new TemporaryNoteDatabase();
        var firstNote = firstDatabase.CreateNote("first-note", "First Note");
        var secondNote = secondDatabase.CreateNote("second-note", "Second Note");
        firstNote.CreatePage("First", "First text");
        secondNote.CreatePage("Second", "Second text");
        var workspace = CreateWorkspace(firstNote, firstNote, secondNote);

        var result = await workspace.SearchAsync(
            "*",
            SearchRangeType.Workspace,
            SearchMethodType.Heading,
            0,
            10,
            CancellationToken.None);

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
    public async Task EditText_UsesTheOwnerInsteadOfTheSelectedNotebook()
    {
        using var selectedDatabase = new TemporaryNoteDatabase();
        using var ownerDatabase = new TemporaryNoteDatabase();
        var selectedNotebook = selectedDatabase.CreateNote("selected-note", "Selected Note");
        var ownerNote = ownerDatabase.CreateNote("owner-note", "Owner Note");
        var selectedPage = selectedNotebook.CreatePage("Shared", "Selected text");
        var ownerPage = ownerNote.CreatePage("Shared", "Owner text");
        var workspace = CreateWorkspace(selectedNotebook, selectedNotebook, ownerNote);
        var ownerResult = await FindResultAsync(workspace, ownerNote, ownerPage.Guid);

        var openedPage = workspace.ReadAll(ownerResult);
        var result = workspace.EditText(ownerResult, "Edited owner text");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(openedPage.Guid, Is.EqualTo(ownerPage.Guid));
            Assert.That(openedPage.Text, Is.EqualTo("Owner text"));
            Assert.That(result.Result, Is.True);
            Assert.That(
                result.Content.OwnerDataSource,
                Is.EqualTo(Path.GetFullPath(ownerNote.DataSource)));
            Assert.That(selectedNotebook.ReadPage(selectedPage.Guid)?.Text, Is.EqualTo("Selected text"));
            Assert.That(ownerNote.ReadPage(ownerPage.Guid)?.Text, Is.EqualTo("Edited owner text"));
        }
    }

    /// <summary>
    /// Verifies that rename validation and persistence both use the owning note.
    /// </summary>
    [Test]
    public async Task RenameText_UsesTheOwnerForDuplicateChecksAndPersistence()
    {
        using var selectedDatabase = new TemporaryNoteDatabase();
        using var ownerDatabase = new TemporaryNoteDatabase();
        var selectedNotebook = selectedDatabase.CreateNote("selected-note", "Selected Note");
        var ownerNote = ownerDatabase.CreateNote("owner-note", "Owner Note");
        var selectedPage = selectedNotebook.CreatePage("Existing", "Selected text");
        var ownerPage = ownerNote.CreatePage("Original", "Owner text");
        var workspace = CreateWorkspace(selectedNotebook, selectedNotebook, ownerNote);
        var ownerResult = await FindResultAsync(workspace, ownerNote, ownerPage.Guid);

        var result = workspace.RenameText(ownerResult, "Existing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result, Is.True);
            Assert.That(selectedNotebook.ReadPage(selectedPage.Guid)?.Name, Is.EqualTo("Existing"));
            Assert.That(ownerNote.ReadPage(ownerPage.Guid)?.Name, Is.EqualTo("Existing"));
        }
    }

    /// <summary>
    /// Verifies that deletion cannot use a result's row identifier against another note.
    /// </summary>
    [Test]
    public async Task DeleteText_WithCollidingRowIds_DeletesOnlyTheOwnerPage()
    {
        using var selectedDatabase = new TemporaryNoteDatabase();
        using var ownerDatabase = new TemporaryNoteDatabase();
        var selectedNotebook = selectedDatabase.CreateNote("selected-note", "Selected Note");
        var ownerNote = ownerDatabase.CreateNote("owner-note", "Owner Note");
        var selectedPage = selectedNotebook.CreatePage("Selected", "Selected text");
        var ownerPage = ownerNote.CreatePage("Owner", "Owner text");
        var workspace = CreateWorkspace(selectedNotebook, selectedNotebook, ownerNote);
        var ownerResult = await FindResultAsync(workspace, ownerNote, ownerPage.Guid);

        Assert.That(ownerPage.Rowid, Is.EqualTo(selectedPage.Rowid));

        var result = workspace.DeleteText(ownerResult);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result, Is.True);
            Assert.That(selectedNotebook.ReadPage(selectedPage.Guid), Is.Not.Null);
            Assert.That(ownerNote.ReadPage(ownerPage.Guid), Is.Null);
        }
    }

    /// <summary>
    /// Verifies that write permissions are determined from the owner rather than the selection.
    /// </summary>
    [Test]
    public async Task ManageText_UsesTheOwnersReadOnlySetting()
    {
        using var selectedDatabase = new TemporaryNoteDatabase();
        using var ownerDatabase = new TemporaryNoteDatabase();
        var selectedNotebook = selectedDatabase.CreateNote("selected-note", "Selected Note");
        var ownerNote = ownerDatabase.CreateNote("owner-note", "Owner Note");
        selectedNotebook.CreatePage("Selected", "Selected text");
        var ownerPage = ownerNote.CreatePage("Owner", "Owner text");
        var workspace = CreateWorkspace(selectedNotebook, selectedNotebook, ownerNote);
        var ownerResult = await FindResultAsync(workspace, ownerNote, ownerPage.Guid);

        selectedNotebook.UpdateMetadata(new NoteMetadataUpdate().SetReadOnly(true));
        var allowedResult = workspace.EditText(ownerResult, "Allowed owner edit");

        selectedNotebook.UpdateMetadata(new NoteMetadataUpdate().SetReadOnly(false));
        ownerNote.UpdateMetadata(new NoteMetadataUpdate().SetReadOnly(true));
        var editResult = workspace.EditText(ownerResult, "Blocked edit");
        var renameResult = workspace.RenameText(ownerResult, "Blocked rename");
        var deleteResult = workspace.DeleteText(ownerResult);

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
        var workspace = CreateWorkspace(note, note);
        var content = page.GetContent();
        content.OwnerDataSource = Path.Combine(database.DirectoryPath, "unknown.db");

        var editResult = workspace.EditText(content, "Changed text");
        var renameResult = workspace.RenameText(content, "Changed name");
        var deleteResult = workspace.DeleteText(content);

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

    private static Workspace CreateWorkspace(Note selectedNotebook, params Note[] notes)
    {
        return new Workspace(null, notes, selectedNotebook);
    }

    private static async Task<Content> FindResultAsync(Workspace workspace, Note owner, Guid pageId)
    {
        var result = await workspace.SearchAsync(
            "*",
            SearchRangeType.Workspace,
            SearchMethodType.Heading,
            0,
            10,
            CancellationToken.None);
        var content = result.Contents.Single(item => item.Guid == pageId);
        Assert.That(
            content.OwnerDataSource,
            Is.EqualTo(Path.GetFullPath(owner.DataSource)));
        return content;
    }
}
