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
    /// <summary>
    /// Verifies that find reports its intentional pause without starting the application.
    /// </summary>
    [Test]
    public async Task Find_ReportsTemporaryPauseWithoutStartingApplication()
    {
        var fixture = CommandFixture.Create();

        var result = await fixture.Find.ExecuteAsync(
            "Roadmap",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Context.LoadCount, Is.Zero);
            Assert.That(fixture.Context.CreateSessionCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo(
                    "Error: The find command is temporarily unavailable. Use 'mn ls' to list page names; " +
                    "full-text search will be redesigned after the initial release." +
                    Environment.NewLine));
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
            Assert.That(fixture.Context.LoadCount, Is.EqualTo(1));
            Assert.That(fixture.Context.CreateSessionCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(fixture.TargetResolver.ResolveCount, Is.EqualTo(1));
            Assert.That(fixture.Application.ReadAsyncCallCount, Is.EqualTo(1));
            Assert.That(fixture.Editor.Documents, Has.Count.EqualTo(1));
            Assert.That(fixture.Editor.Documents[0].FileName, Is.EqualTo("Existing page"));
            Assert.That(fixture.Editor.Documents[0].Text, Is.EqualTo("Before"));
            Assert.That(fixture.Output.StandardOutput, Does.Contain("updated successfully"));
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
            Assert.That(fixture.Context.LoadCount, Is.Zero);
            Assert.That(fixture.Context.CreateSessionCount, Is.Zero);
            Assert.That(fixture.Editor.Documents, Is.Empty);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
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
            Assert.That(fixture.Context.LoadCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo("Error: No name" + Environment.NewLine));
        }
    }

    /// <summary>Verifies ls passes the explicit target and optional limit without configuration I/O.</summary>
    [Test]
    public async Task List_PassesTargetLimitAndFormatWithoutConfigurationAccess()
    {
        var fixture = CommandFixture.Create();
        PageListRequest? request = null;
        CancellationToken applicationToken = default;
        fixture.Application.ListPagesAsyncHandler = (value, token) =>
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
            25,
            longFormat: true,
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.TargetResolver.WorkspaceOption, Is.EqualTo("workspace"));
            Assert.That(fixture.TargetResolver.NotebookOption, Is.EqualTo("notes.mnote"));
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.NotebookId, Is.EqualTo(fixture.NotebookId));
            Assert.That(request.Limit, Is.EqualTo(25));
            Assert.That(applicationToken, Is.EqualTo(cancellation.Token));
            Assert.That(fixture.Context.LoadCount, Is.Zero);
            Assert.That(fixture.Context.CreateSessionCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(fixture.Output.PageListCallCount, Is.EqualTo(1));
            Assert.That(fixture.Output.PageListLongFormat, Is.True);
            Assert.That(fixture.Application.SearchAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies an omitted limit requests every page.</summary>
    [Test]
    public async Task List_WithoutLimit_RequestsAllPages()
    {
        var fixture = CommandFixture.Create();
        PageListRequest? request = null;
        fixture.Application.ListPagesAsyncHandler = (value, _) =>
        {
            request = value;
            return Task.FromResult<IReadOnlyList<PageSummary>>(Array.Empty<PageSummary>());
        };

        var result = await fixture.List.ExecuteAsync(
            null,
            null,
            null,
            longFormat: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(request, Is.Not.Null);
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
            limit,
            longFormat: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.TargetResolver.ResolveCount, Is.Zero);
            Assert.That(fixture.Application.ListPagesAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo("Error: Limit must be greater than zero." + Environment.NewLine));
        }
    }

    /// <summary>Verifies list maps storage failures without writing page output.</summary>
    [Test]
    public async Task List_WhenReadFails_UsesCommonStorageErrorMapping()
    {
        var fixture = CommandFixture.Create();
        fixture.Application.ListPagesAsyncHandler = (_, _) =>
            throw new IOException("database is unavailable");

        var result = await fixture.List.ExecuteAsync(
            null,
            null,
            null,
            longFormat: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Storage));
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo("Error: database is unavailable" + Environment.NewLine));
        }
    }

    /// <summary>Verifies list passes cancellation through the application boundary.</summary>
    [Test]
    public async Task List_WhenReadIsCanceled_ReturnsCanceled()
    {
        var fixture = CommandFixture.Create();
        using var cancellation = new CancellationTokenSource();
        fixture.Application.ListPagesAsyncHandler = (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<IReadOnlyList<PageSummary>>(token);
        };

        var result = await fixture.List.ExecuteAsync(
            null,
            null,
            null,
            longFormat: false,
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Canceled));
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo("Error: Operation was canceled" + Environment.NewLine));
        }
    }

    /// <summary>
    /// Verifies that find ignores the query and keeps the same paused command contract.
    /// </summary>
    [Test]
    public async Task Find_WithoutQuery_HasTheSamePausedContract()
    {
        var fixture = CommandFixture.Create();

        var result = await fixture.Find.ExecuteAsync(
            null,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Context.LoadCount, Is.Zero);
            Assert.That(fixture.Context.CreateSessionCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo(
                    "Error: The find command is temporarily unavailable. Use 'mn ls' to list page names; " +
                    "full-text search will be redesigned after the initial release." +
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
            ConfigurationCli configuration,
            Workspace workspace,
            Notebook notebook,
            StubApplicationService application,
            RecordingContextFactory context,
            StubNotebookTargetSessionResolver targetResolver,
            RecordingExternalEditor editor,
            RecordingCommandOutput output,
            FindCommandHandler find,
            EditCommandHandler edit,
            NewCommandHandler @new,
            ListCommandHandler list)
        {
            Configuration = configuration;
            Workspace = workspace;
            Notebook = notebook;
            Application = application;
            Context = context;
            TargetResolver = targetResolver;
            Editor = editor;
            Output = output;
            Find = find;
            Edit = edit;
            New = @new;
            List = list;
        }

        internal ConfigurationCli Configuration { get; }

        internal Workspace Workspace { get; }

        internal Notebook Notebook { get; }

        internal NotebookId NotebookId => NotebookId.FromDatabasePath(Notebook.DatabasePath);

        internal StubApplicationService Application { get; }

        internal RecordingContextFactory Context { get; }

        internal StubNotebookTargetSessionResolver TargetResolver { get; }

        internal RecordingExternalEditor Editor { get; }

        internal RecordingCommandOutput Output { get; }

        internal FindCommandHandler Find { get; }

        internal EditCommandHandler Edit { get; }

        internal NewCommandHandler New { get; }

        internal ListCommandHandler List { get; }

        internal static CommandFixture Create()
        {
            var configuration = new ConfigurationCli();
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
            var context = new RecordingContextFactory(configuration, session);
            var targetResolver = new StubNotebookTargetSessionResolver(session);
            var editor = new RecordingExternalEditor();
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
                targetResolver,
                editor,
                output,
                new FindCommandHandler(executor),
                new EditCommandHandler(
                    executor,
                    context,
                    targetResolver,
                    editor,
                    output),
                new NewCommandHandler(
                    executor,
                    context,
                    targetResolver,
                    editor,
                    output),
                new ListCommandHandler(
                    executor,
                    targetResolver,
                    output));
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

        internal CancellationToken LastSessionCancellationToken { get; private set; }

        internal Exception? CreateSessionException { get; set; }

        public ConfigurationCli LoadConfiguration()
        {
            LoadCount++;
            return _configuration;
        }

        public Task<ApplicationSession> CreateSessionAsync(
            ConfigurationCli configuration,
            CancellationToken cancellationToken)
        {
            Assert.That(configuration, Is.SameAs(_configuration));
            CreateSessionCount++;
            LastSessionCancellationToken = cancellationToken;
            if (CreateSessionException != null)
                return Task.FromException<ApplicationSession>(CreateSessionException);

            return Task.FromResult(_session);
        }

        public void SaveConfiguration(ConfigurationCli configuration)
        {
            Assert.That(configuration, Is.SameAs(_configuration));
            SaveCount++;
        }
    }

    sealed class RecordingExternalEditor : IExternalEditor
    {
        internal Queue<ExternalEditorResult> Results { get; } = new();

        internal List<ExternalEditorDocument> Documents { get; } = new();

        public Task<ExternalEditorResult> EditAsync(
            ConfigurationCli configuration,
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

        public void WriteErrorLine(string value)
        {
            StandardError += value + Environment.NewLine;
        }

        public void WritePageList(IReadOnlyList<PageSummary> pages, bool longFormat)
        {
            PageListCallCount++;
            PageListLongFormat = longFormat;
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
