using System.Collections.Generic;
using System.IO;
using System.Text;
using MemoriaNote.Application;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies the stateless CLI command contracts at the handler boundary.
/// </summary>
[TestFixture]
public sealed class CliCommandContractTests
{
    /// <summary>Verifies every public ls sort token maps to the Core query contract.</summary>
    [TestCase(null, NotebookPageQuerySort.Name)]
    [TestCase("name", NotebookPageQuerySort.Name)]
    [TestCase("updated", NotebookPageQuerySort.Updated)]
    [TestCase("created", NotebookPageQuerySort.Created)]
    public void ListSortParser_WithSupportedValue_ReturnsCoreSort(
        string? value,
        NotebookPageQuerySort expected)
    {
        var parsed = PageListCommandParser.TryParseSort(value, out var sort);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True);
            Assert.That(sort, Is.EqualTo(expected));
        }
    }

    /// <summary>Verifies that edit resolves a unique page and uses the external editor.</summary>
    [Test]
    public async Task Edit_ResolvesUniqueTargetAndUsesExternalEditor()
    {
        var fixture = CommandFixture.Create();
        var summary = CreatePageSummary(fixture.NotebookId, "Existing page");
        var page = Page.Create(summary.Name, "Before");
        page.Guid = summary.PageId.Value;
        fixture.Application.ResolvePageAsyncHandler = (_, _) => Task.FromResult(
            PageTargetResolution.Succeeded(
                new PageReference(summary.NotebookId, summary.PageId)));
        fixture.Application.ReadAsyncHandler = (_, _) => Task.FromResult(
            PageOperationResult.Succeeded(page));
        fixture.Editor.Results.Enqueue(ExternalEditorResult.Changed("After"));
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Edit.ExecuteAsync(
            "Existing page",
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.EditorOptionsProvider.LoadCount, Is.EqualTo(1));
            Assert.That(fixture.TargetResolver.ResolveCount, Is.EqualTo(1));
            Assert.That(fixture.Application.ReadAsyncCallCount, Is.EqualTo(1));
            Assert.That(fixture.Editor.Documents, Has.Count.EqualTo(1));
            Assert.That(fixture.Editor.Documents[0].FileName, Is.EqualTo("Existing page"));
            Assert.That(fixture.Editor.Documents[0].Text, Is.EqualTo("Before"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Updated page \"Existing page\""));
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies that edit requires a page name before application startup.</summary>
    [Test]
    public async Task Edit_WithoutName_ReturnsValidationFailureBeforeStartup()
    {
        var fixture = CommandFixture.Create();

        var result = await fixture.Edit.ExecuteAsync(
            null,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.EditorOptionsProvider.LoadCount, Is.Zero);
            Assert.That(fixture.Editor.Documents, Is.Empty);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo(
                    "Error: Specify a page name or --id." + Environment.NewLine));
        }
    }

    /// <summary>
    /// Verifies that the new handler rejects a missing name before loading the application context.
    /// </summary>
    [Test]
    public async Task New_WithoutName_ReturnsValidationFailureBeforeStartup()
    {
        var fixture = CommandFixture.Create();

        var result = await fixture.New.ExecuteAsync(
            null,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.EditorOptionsProvider.LoadCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo("Error: A page name is required." + Environment.NewLine));
        }
    }

    /// <summary>Verifies ls passes its complete query without configuration I/O.</summary>
    [Test]
    public async Task List_PassesTargetQueryAndFormatWithoutConfigurationAccess()
    {
        var fixture = CommandFixture.Create();
        NotebookPageQueryRequest? request = null;
        CancellationToken applicationToken = default;
        fixture.Application.QueryPagesAsyncHandler = (value, token) =>
        {
            request = value;
            applicationToken = token;
            return Task.FromResult<IReadOnlyList<PageSummary>>(
                new[] { CreatePageSummary(fixture.NotebookId, "Roadmap") });
        };
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.List.ExecuteAsync(
            "workspace",
            "notes.mnote",
            "project-[A-Z]*",
            "updated",
            reverse: true,
            25,
            longFormat: true,
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.TargetResolver.WorkspaceOption, Is.EqualTo("workspace"));
            Assert.That(fixture.TargetResolver.NotebookOption, Is.EqualTo("notes.mnote"));
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.Pattern, Is.EqualTo("project-[A-Z]*"));
            Assert.That(request.Sort, Is.EqualTo(NotebookPageQuerySort.Updated));
            Assert.That(request.Reverse, Is.True);
            Assert.That(request.Limit, Is.EqualTo(25));
            Assert.That(applicationToken, Is.EqualTo(cancellation.Token));
            Assert.That(fixture.EditorOptionsProvider.LoadCount, Is.Zero);
            Assert.That(fixture.Output.PageListCallCount, Is.EqualTo(1));
            Assert.That(fixture.Output.PageListLongFormat, Is.True);
            Assert.That(fixture.Application.ListPagesAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies an omitted limit requests every page.</summary>
    [Test]
    public async Task List_WithoutLimit_RequestsAllPages()
    {
        var fixture = CommandFixture.Create();
        NotebookPageQueryRequest? request = null;
        fixture.Application.QueryPagesAsyncHandler = (value, _) =>
        {
            request = value;
            return Task.FromResult<IReadOnlyList<PageSummary>>(Array.Empty<PageSummary>());
        };

        var result = await fixture.List.ExecuteAsync(
            null,
            null,
            null,
            null,
            reverse: false,
            null,
            longFormat: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.Pattern, Is.Null);
            Assert.That(request.Sort, Is.EqualTo(NotebookPageQuerySort.Name));
            Assert.That(request.Reverse, Is.False);
            Assert.That(request!.Limit, Is.Null);
            Assert.That(fixture.Output.PageListLongFormat, Is.False);
        }
    }

    /// <summary>Verifies an invalid limit fails before resolving a notebook.</summary>
    [TestCase(0)]
    [TestCase(-1)]
    public async Task List_WithInvalidLimit_ReturnsValidationFailure(int limit)
    {
        var fixture = CommandFixture.Create();

        var result = await fixture.List.ExecuteAsync(
            null,
            null,
            null,
            null,
            reverse: false,
            limit,
            longFormat: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.TargetResolver.ResolveCount, Is.Zero);
            Assert.That(fixture.Application.QueryPagesAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo("Error: Limit must be greater than zero." + Environment.NewLine));
        }
    }

    /// <summary>Verifies an invalid sort fails before resolving a notebook.</summary>
    [Test]
    public async Task List_WithInvalidSort_ReturnsValidationFailure()
    {
        var fixture = CommandFixture.Create();

        var result = await fixture.List.ExecuteAsync(
            null,
            null,
            null,
            "relevance",
            reverse: false,
            limit: null,
            longFormat: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.TargetResolver.ResolveCount, Is.Zero);
            Assert.That(fixture.Application.QueryPagesAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo(
                    "Error: Sort must be name, updated, or created." +
                    Environment.NewLine));
        }
    }

    /// <summary>Verifies list maps storage failures without writing page output.</summary>
    [Test]
    public async Task List_WhenReadFails_UsesCommonStorageErrorMapping()
    {
        var fixture = CommandFixture.Create();
        fixture.Application.QueryPagesAsyncHandler = (_, _) =>
            throw new IOException("database is unavailable");

        var result = await fixture.List.ExecuteAsync(
            null,
            null,
            null,
            null,
            reverse: false,
            null,
            longFormat: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Storage));
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo(
                    "Error: The command could not complete because of a storage error." +
                    Environment.NewLine));
            Assert.That(
                fixture.Output.StandardError,
                Does.Not.Contain("database is unavailable"));
        }
    }

    /// <summary>Verifies list passes cancellation through the application boundary.</summary>
    [Test]
    public async Task List_WhenReadIsCanceled_ReturnsCanceled()
    {
        var fixture = CommandFixture.Create();
        using var cancellation = new CancellationTokenSource();
        fixture.Application.QueryPagesAsyncHandler = (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<IReadOnlyList<PageSummary>>(token);
        };

        var result = await fixture.List.ExecuteAsync(
            null,
            null,
            null,
            null,
            reverse: false,
            null,
            longFormat: false,
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Canceled));
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo(
                    "Canceled: The operation was interrupted." +
                    Environment.NewLine));
        }
    }

    static PageSummary CreatePageSummary(NotebookId notebookId, string name)
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
            Workspace workspace,
            Notebook notebook,
            StubApplicationService application,
            RecordingEditorOptionsProvider editorOptionsProvider,
            StubNotebookTargetSessionResolver targetResolver,
            RecordingExternalEditor editor,
            RecordingCommandOutput output,
            EditCommandHandler edit,
            NewCommandHandler @new,
            ListCommandHandler list)
        {
            Workspace = workspace;
            Notebook = notebook;
            Application = application;
            EditorOptionsProvider = editorOptionsProvider;
            TargetResolver = targetResolver;
            Editor = editor;
            Output = output;
            Edit = edit;
            New = @new;
            List = list;
        }

        internal Workspace Workspace { get; }

        internal Notebook Notebook { get; }

        internal NotebookId NotebookId => NotebookId.FromDatabasePath(Notebook.DatabasePath);

        internal StubApplicationService Application { get; }

        internal RecordingEditorOptionsProvider EditorOptionsProvider { get; }

        internal StubNotebookTargetSessionResolver TargetResolver { get; }

        internal RecordingExternalEditor Editor { get; }

        internal RecordingCommandOutput Output { get; }

        internal EditCommandHandler Edit { get; }

        internal NewCommandHandler New { get; }

        internal ListCommandHandler List { get; }

        internal static CommandFixture Create()
        {
            var editorOptions = new EditorOptions(true, "EDITOR", "configured-editor");
            var notebookPath = Path.Combine(
                Path.GetTempPath(),
                $"memoria-characterization-{Guid.NewGuid():N}.db");
            var notebook = new Notebook(notebookPath);
            var workspace = new Workspace(
                "characterization",
                new[] { notebook },
                notebook);
            var application = new StubApplicationService();
            var output = new RecordingCommandOutput();
            var session = new ApplicationSession(workspace, application);
            var editorOptionsProvider = new RecordingEditorOptionsProvider(editorOptions);
            var targetResolver = new StubNotebookTargetSessionResolver(session);
            var editor = new RecordingExternalEditor();
            var executor = new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance);
            return new CommandFixture(
                workspace,
                notebook,
                application,
                editorOptionsProvider,
                targetResolver,
                editor,
                output,
                new EditCommandHandler(
                    executor,
                    editorOptionsProvider,
                    targetResolver,
                    editor,
                    output),
                new NewCommandHandler(
                    executor,
                    editorOptionsProvider,
                    targetResolver,
                    editor,
                    output),
                new ListCommandHandler(
                    executor,
                    targetResolver,
                    output));
        }
    }

    sealed class RecordingEditorOptionsProvider : IEditorOptionsProvider
    {
        readonly EditorOptions _options;

        internal RecordingEditorOptionsProvider(EditorOptions options)
        {
            _options = options;
        }

        internal int LoadCount { get; private set; }

        public EditorOptions Load()
        {
            LoadCount++;
            return _options;
        }
    }

    sealed class RecordingExternalEditor : IExternalEditor
    {
        internal Queue<ExternalEditorResult> Results { get; } = new();

        internal List<ExternalEditorDocument> Documents { get; } = new();

        public Task<ExternalEditorResult> EditAsync(
            EditorOptions options,
            ExternalEditorCommand commandOverride,
            ExternalEditorDocument document,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Documents.Add(document);
            return Task.FromResult(
                Results.Count == 0
                    ? ExternalEditorResult.Unchanged(document.Text)
                    : Results.Dequeue());
        }
    }

    sealed class RecordingCommandOutput : ICommandOutput
    {
        readonly StringBuilder _standardOutput = new();

        internal string StandardError { get; private set; } = string.Empty;

        internal string StandardOutput => _standardOutput.ToString();

        internal int PageListCallCount { get; private set; }

        internal bool PageListLongFormat { get; private set; }

        public void Write(string value)
        {
            _standardOutput.Append(value);
        }

        public void WriteLine(string value)
        {
            _standardOutput.AppendLine(value);
        }

        public void WriteError(string value)
        {
            StandardError += value;
        }

        public void WriteErrorLine(string value)
        {
            StandardError += value + Environment.NewLine;
        }

        public void WritePageList(IReadOnlyList<PageSummary> pages, bool longFormat)
        {
            PageListCallCount++;
            PageListLongFormat = longFormat;
        }

        public void WriteWorkspaceNotebookList(
            IReadOnlyList<WorkspaceNotebookListEntry> entries,
            bool longFormat)
        {
        }
    }
}
