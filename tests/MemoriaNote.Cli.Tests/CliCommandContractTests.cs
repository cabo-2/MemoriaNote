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
                    "Error: The find command is temporarily unavailable. Use 'mn list [name]' to list page names; " +
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
        fixture.Application.SearchAsyncHandler = (request, _) => Task.FromResult(
            new SearchPage(new[] { summary }, 1, request.Offset, request.Limit));
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
            Assert.That(fixture.Context.CreateSessionCount, Is.EqualTo(1));
            Assert.That(fixture.Context.SaveCount, Is.EqualTo(1));
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
                Is.EqualTo("Error: No name" + Environment.NewLine));
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

    /// <summary>
    /// Verifies the current list request normalization, notebook scope, method, and paging values.
    /// </summary>
    [TestCase("Roadmap", "Roadmap*")]
    [TestCase("Road map", "Road map")]
    [TestCase("*Roadmap", "*Roadmap")]
    public async Task List_PassesCharacterizedSearchRequest(
        string name,
        string expectedQuery)
    {
        var fixture = CommandFixture.Create();
        var requests = new List<SearchRequest>();
        var applicationTokens = new List<CancellationToken>();
        fixture.Application.SearchAsyncHandler = (request, token) =>
        {
            requests.Add(request);
            applicationTokens.Add(token);
            return Task.FromResult(
                new SearchPage(
                    Array.Empty<PageSummary>(),
                    0,
                    request.Offset,
                    request.Limit));
        };
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.List.ExecuteAsync(
            name,
            completion: false,
            cancellation.Token);

        Assert.That(requests, Has.Count.EqualTo(1));
        var request = requests[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(request.Query, Is.EqualTo(expectedQuery));
            Assert.That(request.Scope, Is.EqualTo(SearchRangeType.Notebook));
            Assert.That(request.Method, Is.EqualTo(SearchMethodType.Heading));
            Assert.That(request.NotebookIds, Has.Count.EqualTo(1));
            Assert.That(request.NotebookIds[0], Is.EqualTo(fixture.NotebookId));
            Assert.That(request.Offset, Is.Zero);
            Assert.That(request.Limit, Is.EqualTo(1000));
            Assert.That(applicationTokens, Has.Count.EqualTo(1));
            Assert.That(applicationTokens[0], Is.EqualTo(cancellation.Token));
            Assert.That(fixture.Context.LoadCount, Is.EqualTo(1));
            Assert.That(fixture.Context.CreateSessionCount, Is.EqualTo(1));
            Assert.That(fixture.Context.LastSessionCancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(fixture.Context.SaveCount, Is.EqualTo(1));
            Assert.That(fixture.Output.PageListCallCount, Is.EqualTo(1));
            Assert.That(fixture.Output.PageListTotalCount, Is.Zero);
            Assert.That(fixture.Output.PageCompletionCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies that a missing list name becomes the empty query in the current application request.
    /// </summary>
    [Test]
    public async Task List_WithoutName_UsesEmptyQuery()
    {
        var fixture = CommandFixture.Create();
        SearchRequest? request = null;
        fixture.Application.SearchAsyncHandler = (value, _) =>
        {
            request = value;
            return Task.FromResult(new SearchPage(Array.Empty<PageSummary>(), 0, 0, 1000));
        };

        var result = await fixture.List.ExecuteAsync(
            null,
            completion: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.Query, Is.Empty);
            Assert.That(request.Scope, Is.EqualTo(SearchRangeType.Notebook));
            Assert.That(request.Method, Is.EqualTo(SearchMethodType.Heading));
            Assert.That(fixture.Output.PageListCallCount, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Verifies that list uses completion output without reading page bodies through the application service.
    /// </summary>
    [Test]
    public async Task List_Completion_WritesCompletionOutputWithoutReadingPageBody()
    {
        var fixture = CommandFixture.Create();
        var first = CreatePageSummary(fixture.NotebookId, "Beta page");
        var second = CreatePageSummary(fixture.NotebookId, "Alpha page");
        fixture.Application.SearchAsyncHandler = (request, _) => Task.FromResult(
            new SearchPage(
                new[] { first, second },
                2,
                request.Offset,
                request.Limit));

        var result = await fixture.List.ExecuteAsync(
            "page",
            completion: true,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Output.PageCompletionCallCount, Is.EqualTo(1));
            Assert.That(fixture.Output.PageCompletionPages, Is.EqualTo(new[] { first, second }));
            Assert.That(fixture.Output.PageCompletionTotalCount, Is.EqualTo(2));
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(fixture.Application.ReadAsyncCallCount, Is.Zero);
            Assert.That(fixture.Context.CreateSessionCount, Is.EqualTo(1));
            Assert.That(fixture.Context.SaveCount, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Verifies that list fails when startup returns a workspace without a selected notebook.
    /// </summary>
    [Test]
    public async Task List_WithoutSelectedNotebook_ReturnsNotFoundWithoutSearching()
    {
        var fixture = CommandFixture.Create();
        fixture.Workspace.SelectNotebook(null);
        SearchRequest? request = null;
        fixture.Application.SearchAsyncHandler = (value, _) =>
        {
            request = value;
            return Task.FromResult(new SearchPage(Array.Empty<PageSummary>(), 0, 0, 1000));
        };

        var result = await fixture.List.ExecuteAsync(
            null,
            completion: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(request, Is.Null);
            Assert.That(fixture.Application.SearchAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo("Error: No selected notebook" + Environment.NewLine));
        }
    }

    /// <summary>
    /// Verifies that list maps application storage failures without writing success output or saving configuration.
    /// </summary>
    [Test]
    public async Task List_WhenSearchFails_UsesCommonStorageErrorMapping()
    {
        var fixture = CommandFixture.Create();
        fixture.Application.SearchAsyncHandler = (_, _) =>
            throw new IOException("database is unavailable");

        var result = await fixture.List.ExecuteAsync(
            "Roadmap",
            completion: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Storage));
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(fixture.Output.PageCompletionCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo("Error: database is unavailable" + Environment.NewLine));
        }
    }

    /// <summary>
    /// Verifies that list passes the caller cancellation token through application search.
    /// </summary>
    [Test]
    public async Task List_WhenSearchIsCanceled_ReturnsCanceledWithoutSaving()
    {
        var fixture = CommandFixture.Create();
        using var cancellation = new CancellationTokenSource();
        fixture.Application.SearchAsyncHandler = (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<SearchPage>(token);
        };

        var result = await fixture.List.ExecuteAsync(
            "Roadmap",
            completion: false,
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Canceled));
            Assert.That(fixture.Output.PageListCallCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
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
                    "Error: The find command is temporarily unavailable. Use 'mn list [name]' to list page names; " +
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
            var editor = new RecordingExternalEditor();
            var executor = new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance);
            var normalizer = new CliSearchQueryNormalizer();

            return new CommandFixture(
                configuration,
                workspace,
                notebook,
                application,
                context,
                editor,
                output,
                new FindCommandHandler(executor),
                new EditCommandHandler(executor, context, editor, output),
                new NewCommandHandler(executor, context, editor, output),
                new ListCommandHandler(
                    executor,
                    context,
                    normalizer,
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

        internal int PageListTotalCount { get; private set; }

        internal int PageCompletionCallCount { get; private set; }

        internal int PageCompletionTotalCount { get; private set; }

        internal IReadOnlyList<PageSummary>? PageCompletionPages { get; private set; }

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
            PageListCallCount++;
            PageListTotalCount = totalCount;
        }

        public void WritePageCompletion(
            IReadOnlyList<PageSummary> pages,
            int totalCount)
        {
            PageCompletionCallCount++;
            PageCompletionPages = pages.ToList();
            PageCompletionTotalCount = totalCount;
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
