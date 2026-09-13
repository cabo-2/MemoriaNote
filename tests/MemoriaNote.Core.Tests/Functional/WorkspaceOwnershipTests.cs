using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies that application operations use the notebook that owns each search result.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class WorkspaceOwnershipTests
{
    /// <summary>
    /// Verifies that workspace search results carry an explicit notebook identifier.
    /// </summary>
    [Test]
    public async Task WorkspaceSearch_ReturnsTheOwnerForEveryResult()
    {
        using var firstDatabase = new TemporaryNotebookDatabase();
        using var secondDatabase = new TemporaryNotebookDatabase();
        var firstNotebook = firstDatabase.CreateNotebook("first-note", "First Note");
        var secondNotebook = secondDatabase.CreateNotebook("second-note", "Second Note");
        firstNotebook.CreatePage("First", "First text");
        secondNotebook.CreatePage("Second", "Second text");
        var workspace = CreateWorkspace(firstNotebook, firstNotebook, secondNotebook);

        var result = await SearchAsync(workspace, "*", SearchMethodType.Heading);

        Assert.That(result.Items, Has.Count.EqualTo(2));
        Assert.That(
            result.Items.Select(summary => summary.NotebookId),
            Is.EqualTo(new[]
            {
                NotebookId.FromDatabasePath(firstNotebook.DatabasePath),
                NotebookId.FromDatabasePath(secondNotebook.DatabasePath)
            }));
    }

    /// <summary>
    /// Verifies that reading and editing a non-selected notebook result affect only its owner.
    /// </summary>
    [Test]
    public async Task EditPage_UsesTheOwnerInsteadOfTheSelectedNotebook()
    {
        using var selectedDatabase = new TemporaryNotebookDatabase();
        using var ownerDatabase = new TemporaryNotebookDatabase();
        var selectedNotebook = selectedDatabase.CreateNotebook("selected-note", "Selected Note");
        var ownerNotebook = ownerDatabase.CreateNotebook("owner-note", "Owner Note");
        var selectedPage = selectedNotebook.CreatePage("Shared", "Selected text");
        var ownerPage = ownerNotebook.CreatePage("Shared", "Owner text");
        var workspace = CreateWorkspace(selectedNotebook, selectedNotebook, ownerNotebook);
        var application = ApplicationComposition.Compose(workspace).ApplicationService;
        var ownerResult = await FindResultAsync(workspace, ownerPage.Guid);
        var target = new PageReference(ownerResult.NotebookId, ownerResult.PageId);

        var openedPage = await application.ReadAsync(target, CancellationToken.None);
        var result = await application.EditAsync(
            new EditPageCommand(target.NotebookId, target.PageId, "Edited owner text"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(openedPage.Page.Guid, Is.EqualTo(ownerPage.Guid));
            Assert.That(openedPage.Page.Text, Is.EqualTo("Owner text"));
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(selectedNotebook.ReadPage(selectedPage.Guid)?.Text, Is.EqualTo("Selected text"));
            Assert.That(ownerNotebook.ReadPage(ownerPage.Guid)?.Text, Is.EqualTo("Edited owner text"));
        }
    }

    /// <summary>
    /// Verifies that rename validation and persistence both use the owning notebook.
    /// </summary>
    [Test]
    public async Task RenamePage_UsesTheOwnerForDuplicateChecksAndPersistence()
    {
        using var selectedDatabase = new TemporaryNotebookDatabase();
        using var ownerDatabase = new TemporaryNotebookDatabase();
        var selectedNotebook = selectedDatabase.CreateNotebook("selected-note", "Selected Note");
        var ownerNotebook = ownerDatabase.CreateNotebook("owner-note", "Owner Note");
        var selectedPage = selectedNotebook.CreatePage("Existing", "Selected text");
        var ownerPage = ownerNotebook.CreatePage("Original", "Owner text");
        var workspace = CreateWorkspace(selectedNotebook, selectedNotebook, ownerNotebook);
        var application = ApplicationComposition.Compose(workspace).ApplicationService;
        var ownerResult = await FindResultAsync(workspace, ownerPage.Guid);

        var result = await application.RenameAsync(
            new RenamePageCommand(ownerResult.NotebookId, ownerResult.PageId, "Existing"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(selectedNotebook.ReadPage(selectedPage.Guid)?.Name, Is.EqualTo("Existing"));
            Assert.That(ownerNotebook.ReadPage(ownerPage.Guid)?.Name, Is.EqualTo("Existing"));
        }
    }

    /// <summary>
    /// Verifies that colliding database row identifiers cannot redirect a deletion.
    /// </summary>
    [Test]
    public async Task DeletePage_WithCollidingRowIds_DeletesOnlyTheOwnerPage()
    {
        using var selectedDatabase = new TemporaryNotebookDatabase();
        using var ownerDatabase = new TemporaryNotebookDatabase();
        var selectedNotebook = selectedDatabase.CreateNotebook("selected-note", "Selected Note");
        var ownerNotebook = ownerDatabase.CreateNotebook("owner-note", "Owner Note");
        var selectedPage = selectedNotebook.CreatePage("Selected", "Selected text");
        var ownerPage = ownerNotebook.CreatePage("Owner", "Owner text");
        var workspace = CreateWorkspace(selectedNotebook, selectedNotebook, ownerNotebook);
        var application = ApplicationComposition.Compose(workspace).ApplicationService;
        var ownerResult = await FindResultAsync(workspace, ownerPage.Guid);

        Assert.That(ownerPage.Rowid, Is.EqualTo(selectedPage.Rowid));

        var result = await application.DeleteAsync(
            new DeletePageCommand(ownerResult.NotebookId, ownerResult.PageId),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(selectedNotebook.ReadPage(selectedPage.Guid), Is.Not.Null);
            Assert.That(ownerNotebook.ReadPage(ownerPage.Guid), Is.Null);
        }
    }

    /// <summary>
    /// Verifies that write permissions are determined from the owner rather than the selection.
    /// </summary>
    [Test]
    public async Task ManagePage_UsesTheOwnersReadOnlySetting()
    {
        using var selectedDatabase = new TemporaryNotebookDatabase();
        using var ownerDatabase = new TemporaryNotebookDatabase();
        var selectedNotebook = selectedDatabase.CreateNotebook("selected-note", "Selected Note");
        var ownerNotebook = ownerDatabase.CreateNotebook("owner-note", "Owner Note");
        selectedNotebook.CreatePage("Selected", "Selected text");
        var ownerPage = ownerNotebook.CreatePage("Owner", "Owner text");
        var workspace = CreateWorkspace(selectedNotebook, selectedNotebook, ownerNotebook);
        var application = ApplicationComposition.Compose(workspace).ApplicationService;
        var ownerResult = await FindResultAsync(workspace, ownerPage.Guid);

        selectedNotebook.UpdateMetadata(new NotebookMetadataPatch().SetReadOnly(true));
        var allowedResult = await application.EditAsync(
            new EditPageCommand(ownerResult.NotebookId, ownerResult.PageId, "Allowed owner edit"),
            CancellationToken.None);

        selectedNotebook.UpdateMetadata(new NotebookMetadataPatch().SetReadOnly(false));
        ownerNotebook.UpdateMetadata(new NotebookMetadataPatch().SetReadOnly(true));
        var editResult = await application.EditAsync(
            new EditPageCommand(ownerResult.NotebookId, ownerResult.PageId, "Blocked edit"),
            CancellationToken.None);
        var renameResult = await application.RenameAsync(
            new RenamePageCommand(ownerResult.NotebookId, ownerResult.PageId, "Blocked rename"),
            CancellationToken.None);
        var deleteResult = await application.DeleteAsync(
            new DeletePageCommand(ownerResult.NotebookId, ownerResult.PageId),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allowedResult.IsSuccess, Is.True);
            Assert.That(editResult.Status, Is.EqualTo(PageOperationStatus.ReadOnly));
            Assert.That(renameResult.Status, Is.EqualTo(PageOperationStatus.ReadOnly));
            Assert.That(deleteResult.Status, Is.EqualTo(PageOperationStatus.ReadOnly));
            Assert.That(ownerNotebook.ReadPage(ownerPage.Guid)?.Name, Is.EqualTo("Owner"));
            Assert.That(ownerNotebook.ReadPage(ownerPage.Guid)?.Text, Is.EqualTo("Allowed owner edit"));
        }
    }

    /// <summary>
    /// Verifies that an unknown owner rejects all mutations without changing any notebook.
    /// </summary>
    [Test]
    public async Task ManagePage_WithUnknownOwner_RejectsEveryMutation()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("test-note", "Test Note");
        var page = notebook.CreatePage("Original", "Original text");
        var workspace = CreateWorkspace(notebook, notebook);
        var application = ApplicationComposition.Compose(workspace).ApplicationService;
        var unknownNotebookId = NotebookId.FromDatabasePath(
            Path.Combine(database.DirectoryPath, "unknown.db"));
        var pageId = PageId.FromGuid(page.Guid);

        var editResult = await application.EditAsync(
            new EditPageCommand(unknownNotebookId, pageId, "Changed text"),
            CancellationToken.None);
        var renameResult = await application.RenameAsync(
            new RenamePageCommand(unknownNotebookId, pageId, "Changed name"),
            CancellationToken.None);
        var deleteResult = await application.DeleteAsync(
            new DeletePageCommand(unknownNotebookId, pageId),
            CancellationToken.None);

        var persisted = notebook.ReadPage(page.Guid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(editResult.Status, Is.EqualTo(PageOperationStatus.OwnerNotFound));
            Assert.That(renameResult.Status, Is.EqualTo(PageOperationStatus.OwnerNotFound));
            Assert.That(deleteResult.Status, Is.EqualTo(PageOperationStatus.OwnerNotFound));
            Assert.That(persisted?.Name, Is.EqualTo("Original"));
            Assert.That(persisted?.Text, Is.EqualTo("Original text"));
        }
    }

    static Workspace CreateWorkspace(Notebook selectedNotebook, params Notebook[] notebooks)
    {
        return new Workspace(null, notebooks, selectedNotebook);
    }

    static async Task<PageSummary> FindResultAsync(Workspace workspace, Guid pageId)
    {
        var result = await SearchAsync(workspace, "*", SearchMethodType.Heading);
        return result.Items.Single(summary => summary.PageId.Value == pageId);
    }

    static Task<SearchPage> SearchAsync(
        Workspace workspace,
        string query,
        SearchMethodType method)
    {
        var request = SearchRequest.ForWorkspace(
            query,
            method,
            workspace.Notebooks.Select(notebook =>
                NotebookId.FromDatabasePath(notebook.DatabasePath)),
            0,
            10);
        return ApplicationComposition.Compose(workspace)
            .ApplicationService
            .SearchAsync(request, CancellationToken.None);
    }
}
