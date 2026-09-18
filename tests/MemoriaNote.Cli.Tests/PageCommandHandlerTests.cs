using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the stateless external-editor page command contracts.</summary>
[TestFixture]
public sealed class PageCommandHandlerTests
{
    /// <summary>Verifies that a missing new-page name does not start the application.</summary>
    [Test]
    public async Task New_WithoutName_DoesNotStartEditorOrMutation()
    {
        var fixture = CommandFixture.Create();

        var result = await fixture.New.ExecuteAsync(null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Context.LoadCount, Is.Zero);
            Assert.That(fixture.Editor.Documents, Is.Empty);
            Assert.That(fixture.Application.CreateAsyncCallCount, Is.Zero);
        }
    }

    /// <summary>Verifies that new fails before editing when no notebook is selected.</summary>
    [Test]
    public async Task New_WithoutSelectedNotebook_DoesNotStartEditorOrMutation()
    {
        var fixture = CommandFixture.Create(selectedNotebook: false);

        var result = await fixture.New.ExecuteAsync("New page", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(fixture.Context.CreateSessionCount, Is.EqualTo(1));
            Assert.That(fixture.Application.ValidateCreateAsyncCallCount, Is.Zero);
            Assert.That(fixture.Editor.Documents, Is.Empty);
            Assert.That(fixture.Application.CreateAsyncCallCount, Is.Zero);
        }
    }

    /// <summary>Verifies that creation validation runs before the external editor.</summary>
    [Test]
    public async Task New_WhenInitialValidationFails_DoesNotStartEditorOrMutation()
    {
        var fixture = CommandFixture.Create();
        fixture.Application.ValidateCreateAsyncHandler = (_, _) => Task.FromResult(
            PageOperationResult.ValidationFailed(new[] { PageErrorCode.DuplicateName }));

        var result = await fixture.New.ExecuteAsync("Existing page", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Application.ValidateCreateAsyncCallCount, Is.EqualTo(1));
            Assert.That(fixture.Editor.Documents, Is.Empty);
            Assert.That(fixture.Application.CreateAsyncCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies that an unchanged new-page document is treated as a successful no-op.</summary>
    [Test]
    public async Task New_WhenEditorIsUnchanged_DoesNotCreatePage()
    {
        var fixture = CommandFixture.Create();
        fixture.Editor.Results.Enqueue(ExternalEditorResult.Unchanged(string.Empty));

        var result = await fixture.New.ExecuteAsync("New page", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Editor.Documents, Has.Count.EqualTo(1));
            Assert.That(fixture.Editor.Documents[0].FileName, Is.EqualTo("New page"));
            Assert.That(fixture.Editor.Documents[0].Text, Is.Empty);
            Assert.That(fixture.Application.CreateAsyncCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
        }
    }

    /// <summary>Verifies that an editor process failure prevents page creation.</summary>
    [Test]
    public async Task New_WhenEditorFails_ReturnsStorageFailureWithoutCreatingPage()
    {
        var fixture = CommandFixture.Create();
        fixture.Editor.EditAsyncHandler = (_, _, _) => Task.FromException<ExternalEditorResult>(
            new ExternalEditorProcessException("editor", 17));

        var result = await fixture.New.ExecuteAsync("New page", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Storage));
            Assert.That(fixture.Application.CreateAsyncCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardError,
                Does.Contain("External editor 'editor' exited with code 17"));
        }
    }

    /// <summary>Verifies that changed text is validated again before creation.</summary>
    [Test]
    public async Task New_WhenEditedTextFailsValidation_DoesNotCreatePage()
    {
        var fixture = CommandFixture.Create();
        var validationCount = 0;
        fixture.Application.ValidateCreateAsyncHandler = (_, _) =>
        {
            validationCount++;
            return Task.FromResult(
                validationCount == 1
                    ? PageOperationResult.Succeeded()
                    : PageOperationResult.ValidationFailed(
                        new[] { PageErrorCode.DuplicateName }));
        };
        fixture.Editor.Results.Enqueue(ExternalEditorResult.Changed("Body"));

        var result = await fixture.New.ExecuteAsync("New page", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Application.ValidateCreateAsyncCallCount, Is.EqualTo(2));
            Assert.That(fixture.Application.CreateAsyncCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies that creation passes the requested owner, name, and edited body once.</summary>
    [Test]
    public async Task New_WhenEditorChangesText_CreatesPageOnce()
    {
        var fixture = CommandFixture.Create();
        CreatePageCommand? createdCommand = null;
        fixture.Application.CreateAsyncHandler = (command, _) =>
        {
            createdCommand = command;
            return Task.FromResult(PageOperationResult.Succeeded());
        };
        fixture.Editor.Results.Enqueue(ExternalEditorResult.Changed("Body"));

        var result = await fixture.New.ExecuteAsync("New page", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Application.ValidateCreateAsyncCallCount, Is.EqualTo(2));
            Assert.That(fixture.Application.CreateAsyncCallCount, Is.EqualTo(1));
            Assert.That(createdCommand, Is.Not.Null);
            Assert.That(createdCommand!.NotebookId, Is.EqualTo(fixture.NotebookId));
            Assert.That(createdCommand.Name, Is.EqualTo("New page"));
            Assert.That(createdCommand.Text, Is.EqualTo("Body"));
            Assert.That(fixture.Context.SaveCount, Is.EqualTo(1));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("The text created successfully."));
        }
    }

    /// <summary>Verifies that edit requires a page name before startup.</summary>
    [Test]
    public async Task Edit_WithoutName_DoesNotStartApplication()
    {
        var fixture = CommandFixture.Create();

        var result = await fixture.Edit.ExecuteAsync(null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Context.LoadCount, Is.Zero);
            Assert.That(fixture.Context.CreateSessionCount, Is.Zero);
            Assert.That(fixture.Editor.Documents, Is.Empty);
        }
    }

    /// <summary>Verifies that edit reports no matching page without opening the editor.</summary>
    [Test]
    public async Task Edit_WhenTargetIsMissing_DoesNotReadOrEditPage()
    {
        var fixture = CommandFixture.Create();
        fixture.Application.SearchAsyncHandler = (request, _) => Task.FromResult(
            new SearchPage(Array.Empty<PageSummary>(), 0, request.Offset, request.Limit));

        var result = await fixture.Edit.ExecuteAsync("Missing page", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(fixture.Application.SearchAsyncCallCount, Is.EqualTo(1));
            Assert.That(fixture.Application.ReadAsyncCallCount, Is.Zero);
            Assert.That(fixture.Editor.Documents, Is.Empty);
            Assert.That(fixture.Application.EditAsyncCallCount, Is.Zero);
        }
    }

    /// <summary>Verifies that edit rejects multiple matching pages instead of choosing one.</summary>
    [Test]
    public async Task Edit_WhenTargetIsAmbiguous_DoesNotReadOrEditPage()
    {
        var fixture = CommandFixture.Create();
        var first = CreateSummary(fixture.NotebookId, "Daily");
        var second = CreateSummary(fixture.NotebookId, "Daily");
        fixture.Application.SearchAsyncHandler = (request, _) => Task.FromResult(
            new SearchPage(new[] { first, second }, 2, request.Offset, request.Limit));

        var result = await fixture.Edit.ExecuteAsync("Daily", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(fixture.Application.ReadAsyncCallCount, Is.Zero);
            Assert.That(fixture.Editor.Documents, Is.Empty);
            Assert.That(fixture.Application.EditAsyncCallCount, Is.Zero);
        }
    }

    /// <summary>Verifies that edit uses the owner-qualified search result and current body.</summary>
    [Test]
    public async Task Edit_WhenEditorChangesText_UsesOwnerQualifiedTargetAndUpdatesOnce()
    {
        var fixture = CommandFixture.Create();
        var summary = CreateSummary(fixture.NotebookId, "Daily");
        var page = Page.Create(summary.Name, "Current body");
        page.Guid = summary.PageId.Value;
        SearchRequest? searchRequest = null;
        PageReference? readTarget = null;
        EditPageCommand? editCommand = null;
        fixture.Application.SearchAsyncHandler = (request, _) =>
        {
            searchRequest = request;
            return Task.FromResult(
                new SearchPage(new[] { summary }, 1, request.Offset, request.Limit));
        };
        fixture.Application.ReadAsyncHandler = (target, _) =>
        {
            readTarget = target;
            return Task.FromResult(PageOperationResult.Succeeded(page));
        };
        fixture.Application.EditAsyncHandler = (command, _) =>
        {
            editCommand = command;
            return Task.FromResult(PageOperationResult.Succeeded());
        };
        fixture.Editor.Results.Enqueue(ExternalEditorResult.Changed("Edited body"));

        var result = await fixture.Edit.ExecuteAsync("Daily", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(searchRequest, Is.Not.Null);
            Assert.That(searchRequest!.Query, Is.EqualTo("Daily"));
            Assert.That(searchRequest.Scope, Is.EqualTo(SearchRangeType.Notebook));
            Assert.That(searchRequest.Method, Is.EqualTo(SearchMethodType.Heading));
            Assert.That(searchRequest.Offset, Is.Zero);
            Assert.That(searchRequest.Limit, Is.EqualTo(2));
            Assert.That(readTarget, Is.Not.Null);
            Assert.That(readTarget!.NotebookId, Is.EqualTo(summary.NotebookId));
            Assert.That(readTarget.PageId, Is.EqualTo(summary.PageId));
            Assert.That(fixture.Editor.Documents, Has.Count.EqualTo(1));
            Assert.That(fixture.Editor.Documents[0].FileName, Is.EqualTo("Daily"));
            Assert.That(fixture.Editor.Documents[0].Text, Is.EqualTo("Current body"));
            Assert.That(fixture.Application.ValidateEditAsyncCallCount, Is.EqualTo(2));
            Assert.That(fixture.Application.EditAsyncCallCount, Is.EqualTo(1));
            Assert.That(editCommand, Is.Not.Null);
            Assert.That(editCommand!.NotebookId, Is.EqualTo(summary.NotebookId));
            Assert.That(editCommand.PageId, Is.EqualTo(summary.PageId));
            Assert.That(editCommand.Text, Is.EqualTo("Edited body"));
            Assert.That(fixture.Context.SaveCount, Is.EqualTo(1));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("The text updated successfully."));
        }
    }

    /// <summary>Verifies that an unchanged edit does not update or save configuration.</summary>
    [Test]
    public async Task Edit_WhenEditorIsUnchanged_DoesNotUpdatePage()
    {
        var fixture = CreateEditFixture("Current body");
        fixture.Editor.Results.Enqueue(ExternalEditorResult.Unchanged("Current body"));

        var result = await fixture.Edit.ExecuteAsync("Daily", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Application.ValidateEditAsyncCallCount, Is.EqualTo(1));
            Assert.That(fixture.Application.EditAsyncCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies that an editor process failure does not update a page.</summary>
    [Test]
    public async Task Edit_WhenEditorFails_DoesNotUpdatePage()
    {
        var fixture = CreateEditFixture("Current body");
        fixture.Editor.EditAsyncHandler = (_, _, _) => Task.FromException<ExternalEditorResult>(
            new ExternalEditorProcessException("editor", 17));

        var result = await fixture.Edit.ExecuteAsync("Daily", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Storage));
            Assert.That(fixture.Application.EditAsyncCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies that editor cancellation does not update a page.</summary>
    [Test]
    public async Task Edit_WhenEditorIsCanceled_DoesNotUpdatePage()
    {
        var fixture = CreateEditFixture("Current body");
        using var cancellation = new CancellationTokenSource();
        fixture.Editor.EditAsyncHandler = (_, _, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<ExternalEditorResult>(token);
        };

        var result = await fixture.Edit.ExecuteAsync("Daily", cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Canceled));
            Assert.That(fixture.Application.EditAsyncCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies that post-editor validation failure prevents an update.</summary>
    [Test]
    public async Task Edit_WhenEditedTextFailsValidation_DoesNotUpdatePage()
    {
        var fixture = CreateEditFixture("Current body");
        var validationCount = 0;
        fixture.Application.ValidateEditAsyncHandler = (_, _) =>
        {
            validationCount++;
            return Task.FromResult(
                validationCount == 1
                    ? PageOperationResult.Succeeded()
                    : PageOperationResult.Failed(
                        PageOperationStatus.PageNotFound,
                        PageErrorCode.PageNotFound));
        };
        fixture.Editor.Results.Enqueue(ExternalEditorResult.Changed("Edited body"));

        var result = await fixture.Edit.ExecuteAsync("Daily", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(fixture.Application.ValidateEditAsyncCallCount, Is.EqualTo(2));
            Assert.That(fixture.Application.EditAsyncCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies that a page removed while editing is reported as not found.</summary>
    [Test]
    public async Task Edit_WhenPageIsRemovedDuringEditor_DoesNotUpdatePage()
    {
        var fixture = CreateEditFixture("Current body");
        var validationCount = 0;
        fixture.Application.ValidateEditAsyncHandler = (_, _) =>
        {
            validationCount++;
            return Task.FromResult(
                validationCount == 1
                    ? PageOperationResult.Succeeded()
                    : PageOperationResult.Failed(
                        PageOperationStatus.PageNotFound,
                        PageErrorCode.PageNotFound));
        };
        fixture.Editor.Results.Enqueue(ExternalEditorResult.Changed("Edited body"));

        var result = await fixture.Edit.ExecuteAsync("Daily", CancellationToken.None);

        Assert.That(result, Is.EqualTo((int)CliExitCode.NotFound));
        Assert.That(fixture.Application.EditAsyncCallCount, Is.Zero);
    }

    static CommandFixture CreateEditFixture(string currentText)
    {
        var fixture = CommandFixture.Create();
        var summary = CreateSummary(fixture.NotebookId, "Daily");
        var page = Page.Create(summary.Name, currentText);
        page.Guid = summary.PageId.Value;
        fixture.Application.SearchAsyncHandler = (request, _) => Task.FromResult(
            new SearchPage(new[] { summary }, 1, request.Offset, request.Limit));
        fixture.Application.ReadAsyncHandler = (_, _) => Task.FromResult(
            PageOperationResult.Succeeded(page));
        return fixture;
    }

    static PageSummary CreateSummary(NotebookId notebookId, string name)
    {
        var now = DateTime.UtcNow;
        return new PageSummary(
            notebookId,
            PageId.FromGuid(Guid.NewGuid()),
            name,
            1,
            new Dictionary<string, string>(),
            "text/plain",
            now,
            now,
            false);
    }

    sealed class CommandFixture
    {
        CommandFixture(
            ConfigurationCli configuration,
            Workspace workspace,
            Notebook notebook,
            StubApplicationService application,
            RecordingContextFactory context,
            RecordingExternalEditor editor,
            RecordingCommandOutput output,
            NewCommandHandler @new,
            EditCommandHandler edit)
        {
            Configuration = configuration;
            Workspace = workspace;
            Notebook = notebook;
            Application = application;
            Context = context;
            Editor = editor;
            Output = output;
            New = @new;
            Edit = edit;
        }

        internal ConfigurationCli Configuration { get; }

        internal Workspace Workspace { get; }

        internal Notebook Notebook { get; }

        internal NotebookId NotebookId => NotebookId.FromDatabasePath(Notebook.DatabasePath);

        internal StubApplicationService Application { get; }

        internal RecordingContextFactory Context { get; }

        internal RecordingExternalEditor Editor { get; }

        internal RecordingCommandOutput Output { get; }

        internal NewCommandHandler New { get; }

        internal EditCommandHandler Edit { get; }

        internal static CommandFixture Create(bool selectedNotebook = true)
        {
            var configuration = new ConfigurationCli();
            var notebookPath = Path.Combine(
                Path.GetTempPath(),
                $"memoria-page-command-{Guid.NewGuid():N}.db");
            var notebook = new Notebook(notebookPath);
            var workspace = new Workspace(
                "page-command-tests",
                new[] { notebook },
                notebook);
            if (!selectedNotebook)
                workspace.SelectNotebook(null);

            var application = new StubApplicationService();
            var session = new ApplicationSession(workspace, application);
            var context = new RecordingContextFactory(configuration, session);
            var editor = new RecordingExternalEditor();
            var output = new RecordingCommandOutput();
            var executor = new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance);

            return new CommandFixture(
                configuration,
                workspace,
                notebook,
                application,
                context,
                editor,
                output,
                new NewCommandHandler(executor, context, editor, output),
                new EditCommandHandler(executor, context, editor, output));
        }
    }

    sealed class RecordingContextFactory : ICliCommandContextFactory
    {
        readonly ConfigurationCli _configuration;
        readonly ApplicationSession _session;

        internal RecordingContextFactory(
            ConfigurationCli configuration,
            ApplicationSession session)
        {
            _configuration = configuration;
            _session = session;
        }

        internal int LoadCount { get; private set; }

        internal int CreateSessionCount { get; private set; }

        internal int SaveCount { get; private set; }

        public ConfigurationCli LoadConfiguration()
        {
            LoadCount++;
            return _configuration;
        }

        public Task<ApplicationSession> CreateSessionAsync(
            ConfigurationCli configuration,
            CancellationToken cancellationToken)
        {
            CreateSessionCount++;
            return Task.FromResult(_session);
        }

        public Task<MemoriaNoteViewModel> CreateViewModelAsync(
            ConfigurationCli configuration,
            CancellationToken cancellationToken)
        {
            return Task.FromException<MemoriaNoteViewModel>(
                new NotSupportedException("Page command tests must not create a ViewModel."));
        }

        public void SaveConfiguration(ConfigurationCli configuration)
        {
            SaveCount++;
        }
    }

    sealed class RecordingExternalEditor : IExternalEditor
    {
        internal Queue<ExternalEditorResult> Results { get; } = new();

        internal List<ExternalEditorDocument> Documents { get; } = new();

        internal Func<
            ConfigurationCli,
            ExternalEditorDocument,
            CancellationToken,
            Task<ExternalEditorResult>>? EditAsyncHandler { get; set; }

        public Task<ExternalEditorResult> EditAsync(
            ConfigurationCli configuration,
            ExternalEditorDocument document,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Documents.Add(document);
            if (EditAsyncHandler != null)
                return EditAsyncHandler(configuration, document, cancellationToken);

            return Results.Count == 0
                ? Task.FromResult(ExternalEditorResult.Unchanged(document.Text))
                : Task.FromResult(Results.Dequeue());
        }
    }

    sealed class RecordingCommandOutput : ICommandOutput
    {
        readonly StringBuilder _standardOutput = new();

        internal string StandardOutput => _standardOutput.ToString();

        internal string StandardError { get; private set; } = string.Empty;

        public void Write(string value)
        {
            _standardOutput.Append(value);
        }

        public void WriteLine(string value)
        {
            _standardOutput.AppendLine(value);
        }

        public void WriteErrorLine(string value)
        {
            StandardError += value + Environment.NewLine;
        }

        public void WritePageList(IReadOnlyList<PageSummary> pages, int totalCount)
        {
        }

        public void WritePageCompletion(
            IReadOnlyList<PageSummary> pages,
            int totalCount)
        {
        }

        public void WriteNotebookList(
            IEnumerable<Notebook> notebooks,
            Notebook selectedNotebook)
        {
        }

        public void WriteNotebookCompletion(IEnumerable<Notebook> notebooks)
        {
        }
    }
}
