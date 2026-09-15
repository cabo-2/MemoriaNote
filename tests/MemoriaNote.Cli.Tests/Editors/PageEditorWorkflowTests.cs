using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests.Editors;

/// <summary>Verifies page create, edit, rename, and delete editor workflows.</summary>
[TestFixture]
public sealed class PageEditorWorkflowTests
{
    /// <summary>Verifies the name and body exchanges used when create validation fails.</summary>
    [Test]
    public async Task RunAsync_Create_CollectsNameThenBodyAndCreatesPage()
    {
        var application = new StubApplicationService
        {
            ValidateCreateAsyncHandler = (_, _) => Task.FromResult(
                PageOperationResult.ValidationFailed(new[] { PageErrorCode.NameRequired }))
        };
        CreatePageCommand? command = null;
        application.CreateAsyncHandler = (value, _) =>
        {
            command = value;
            return Task.FromResult(PageOperationResult.Succeeded());
        };
        var viewModel = CreateViewModel(application);
        viewModel.EditingState = EditorMode.Create;
        viewModel.EditingTitle = string.Empty;
        viewModel.EditingText = string.Empty;
        var editor = new StubExternalEditor(
            ExternalEditorResult.Changed("Created page" + Environment.NewLine),
            ExternalEditorResult.Changed("Created body"));
        var workflow = CreateWorkflow(editor);

        await workflow.RunAsync(viewModel, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Documents, Has.Count.EqualTo(2));
            Assert.That(editor.Documents[0].FileName, Is.EqualTo("New text"));
            Assert.That(
                editor.Documents[0].Text,
                Does.Contain("#### Enter a name to be created ####"));
            Assert.That(editor.Documents[1].FileName, Is.EqualTo("Created page"));
            Assert.That(command, Is.Not.Null);
            Assert.That(command!.Name, Is.EqualTo("Created page"));
            Assert.That(command.Text, Is.EqualTo("Created body"));
            Assert.That(viewModel.EditingState, Is.EqualTo(EditorMode.None));
        }
    }

    /// <summary>Verifies that body editing updates the selected page.</summary>
    [Test]
    public async Task RunAsync_Edit_ExchangesBodyAndUpdatesPage()
    {
        var application = new StubApplicationService();
        EditPageCommand? command = null;
        application.EditAsyncHandler = (value, _) =>
        {
            command = value;
            return Task.FromResult(PageOperationResult.Succeeded());
        };
        var viewModel = CreateViewModel(application);
        viewModel.EditingState = EditorMode.Edit;
        viewModel.EditingTitle = "Existing page";
        viewModel.EditingText = "Before";
        var editor = new StubExternalEditor(ExternalEditorResult.Changed("After"));
        var workflow = CreateWorkflow(editor);

        await workflow.RunAsync(viewModel, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Documents, Has.Count.EqualTo(1));
            Assert.That(editor.Documents[0].FileName, Is.EqualTo("Existing page"));
            Assert.That(editor.Documents[0].Text, Is.EqualTo("Before"));
            Assert.That(command, Is.Not.Null);
            Assert.That(command!.Text, Is.EqualTo("After"));
            Assert.That(viewModel.EditingState, Is.EqualTo(EditorMode.None));
        }
    }

    /// <summary>Verifies that name editing renames the selected page.</summary>
    [Test]
    public async Task RunAsync_Rename_ExchangesNameAndRenamesPage()
    {
        var application = new StubApplicationService();
        RenamePageCommand? command = null;
        application.RenameAsyncHandler = (value, _) =>
        {
            command = value;
            return Task.FromResult(PageOperationResult.Succeeded());
        };
        var viewModel = CreateViewModel(application);
        viewModel.EditingState = EditorMode.Rename;
        viewModel.EditingTitle = "Existing page";
        var editor = new StubExternalEditor(
            ExternalEditorResult.Changed("Renamed page" + Environment.NewLine));
        var workflow = CreateWorkflow(editor);

        await workflow.RunAsync(viewModel, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Documents.Single().FileName, Is.EqualTo("Rename text"));
            Assert.That(command, Is.Not.Null);
            Assert.That(command!.Name, Is.EqualTo("Renamed page"));
            Assert.That(viewModel.EditingState, Is.EqualTo(EditorMode.None));
        }
    }

    /// <summary>Verifies that the existing name confirmation invokes page deletion.</summary>
    [Test]
    public async Task RunAsync_Delete_ExchangesNameAndDeletesPage()
    {
        var application = new StubApplicationService();
        DeletePageCommand? command = null;
        application.DeleteAsyncHandler = (value, _) =>
        {
            command = value;
            return Task.FromResult(PageOperationResult.Succeeded());
        };
        var viewModel = CreateViewModel(application);
        viewModel.EditingState = EditorMode.Delete;
        viewModel.EditingTitle = "Existing page";
        var editor = new StubExternalEditor(
            ExternalEditorResult.Changed("Existing page" + Environment.NewLine));
        var workflow = CreateWorkflow(editor);

        await workflow.RunAsync(viewModel, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Documents.Single().FileName, Is.EqualTo("Delete text"));
            Assert.That(command, Is.Not.Null);
            Assert.That(viewModel.EditingState, Is.EqualTo(EditorMode.None));
        }
    }

    /// <summary>Verifies that a no-change result retains the existing cancellation notice.</summary>
    [Test]
    public async Task RunAsync_UnchangedBody_DoesNotUpdatePage()
    {
        var application = new StubApplicationService();
        var editCount = 0;
        application.EditAsyncHandler = (_, _) =>
        {
            editCount++;
            return Task.FromResult(PageOperationResult.Succeeded());
        };
        var viewModel = CreateViewModel(application);
        viewModel.EditingState = EditorMode.Edit;
        viewModel.EditingTitle = "Existing page";
        viewModel.EditingText = "Same";
        var editor = new StubExternalEditor(ExternalEditorResult.Unchanged("Same"));
        var workflow = CreateWorkflow(editor);

        await workflow.RunAsync(viewModel, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editCount, Is.Zero);
            Assert.That(viewModel.SearchNotice, Is.EqualTo("A text enter canceled"));
            Assert.That(viewModel.EditingState, Is.EqualTo(EditorMode.None));
        }
    }

    static PageEditorWorkflow CreateWorkflow(IExternalEditor editor)
    {
        return new PageEditorWorkflow(
            editor,
            NullLogger<PageEditorWorkflow>.Instance);
    }

    static MemoriaNoteViewModel CreateViewModel(StubApplicationService application)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"editor-workflow-{Guid.NewGuid():N}.db");
        var notebook = new Notebook(databasePath);
        var notebookId = NotebookId.FromDatabasePath(databasePath);
        var viewModel = new MemoriaNoteViewModel(
            new ConfigurationCli(),
            new Workspace(null, new[] { notebook }, notebook),
            application,
            NullLogger<MemoriaNoteViewModel>.Instance);
        viewModel.OpenedContent = new PageSummary(
            notebookId,
            PageId.FromGuid(Guid.NewGuid()),
            "Existing page",
            1,
            new Dictionary<string, string>(),
            "text/plain",
            DateTime.UtcNow,
            DateTime.UtcNow,
            false);
        return viewModel;
    }

    sealed class StubExternalEditor : IExternalEditor
    {
        readonly Queue<ExternalEditorResult> _results;

        internal StubExternalEditor(params ExternalEditorResult[] results)
        {
            _results = new Queue<ExternalEditorResult>(results);
        }

        internal List<ExternalEditorDocument> Documents { get; } = new();

        public Task<ExternalEditorResult> EditAsync(
            ConfigurationCli configuration,
            ExternalEditorDocument document,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Documents.Add(document);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
