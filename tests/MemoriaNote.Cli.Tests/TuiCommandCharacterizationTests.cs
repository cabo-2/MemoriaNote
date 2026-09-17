using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Records the command-boundary behavior that is allowed to change while the TUI is removed.
/// </summary>
[TestFixture]
public sealed class TuiCommandCharacterizationTests
{
    /// <summary>
    /// Verifies that find normalizes its name and copies persisted search options before opening the home UI adapter.
    /// </summary>
    [Test]
    public async Task Find_ProjectsNameAndConfiguredSearchOptionsIntoHomeUi()
    {
        var fixture = CommandFixture.Create();
        fixture.Configuration.State.SearchRange = SearchRangeType.Workspace;
        fixture.Configuration.State.SearchMethod = SearchMethodType.FullText;
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Find.ExecuteAsync(
            "Roadmap",
            cancellation.Token);

        var viewModel = fixture.Context.LastViewModel;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Context.LoadCount, Is.EqualTo(1));
            Assert.That(fixture.Context.SaveCount, Is.EqualTo(1));
            Assert.That(fixture.Context.LastCancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(fixture.TerminalUi.HomeRunCount, Is.EqualTo(1));
            Assert.That(fixture.TerminalUi.HomeViewModel, Is.SameAs(viewModel));
            Assert.That(fixture.TerminalUi.HomeCancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(viewModel, Is.Not.Null);
            Assert.That(viewModel!.SearchEntry, Is.EqualTo("Roadmap*"));
            Assert.That(viewModel.SearchRange, Is.EqualTo(SearchRangeType.Workspace));
            Assert.That(viewModel.SearchMethod, Is.EqualTo(SearchMethodType.FullText));
            Assert.That(fixture.TerminalUi.ManageRunCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies that edit passes the supplied name as a notebook-scoped heading search to the manage UI adapter.
    /// </summary>
    [Test]
    public async Task Edit_ProjectsNameIntoNotebookHeadingSearchWithoutOpeningEditor()
    {
        var fixture = CommandFixture.Create();
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Edit.ExecuteAsync(
            "Existing page",
            cancellation.Token);

        var viewModel = fixture.Context.LastViewModel;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Context.LoadCount, Is.EqualTo(1));
            Assert.That(fixture.Context.SaveCount, Is.EqualTo(1));
            Assert.That(fixture.Context.LastCancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(fixture.TerminalUi.ManageRunCount, Is.EqualTo(1));
            Assert.That(fixture.TerminalUi.OpenEditor, Is.False);
            Assert.That(fixture.TerminalUi.ManageViewModel, Is.SameAs(viewModel));
            Assert.That(fixture.TerminalUi.ManageCancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(viewModel, Is.Not.Null);
            Assert.That(viewModel!.SearchEntry, Is.EqualTo("Existing page"));
            Assert.That(viewModel.SearchRange, Is.EqualTo(SearchRangeType.Notebook));
            Assert.That(viewModel.SearchMethod, Is.EqualTo(SearchMethodType.Heading));
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies the current edit behavior when no name is supplied before target resolution is introduced.
    /// </summary>
    [Test]
    public async Task Edit_WithoutName_StillStartsManageUiInCurrentHandler()
    {
        var fixture = CommandFixture.Create();

        var result = await fixture.Edit.ExecuteAsync(
            null,
            CancellationToken.None);

        var viewModel = fixture.Context.LastViewModel;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.TerminalUi.ManageRunCount, Is.EqualTo(1));
            Assert.That(fixture.TerminalUi.OpenEditor, Is.False);
            Assert.That(viewModel, Is.Not.Null);
            Assert.That(viewModel!.SearchEntry, Is.Null);
            Assert.That(viewModel.SearchRange, Is.EqualTo(SearchRangeType.Notebook));
            Assert.That(viewModel.SearchMethod, Is.EqualTo(SearchMethodType.Heading));
            Assert.That(fixture.Context.SaveCount, Is.EqualTo(1));
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
            Assert.That(fixture.Context.CreateCount, Is.Zero);
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(fixture.TerminalUi.ManageRunCount, Is.Zero);
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
            Assert.That(applicationTokens[0].CanBeCanceled, Is.True);
            Assert.That(fixture.Context.LoadCount, Is.EqualTo(1));
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
    /// Verifies that list uses the completion output path and currently reads the first result body during activation.
    /// </summary>
    [Test]
    public async Task List_Completion_WritesCompletionOutputAfterActivation()
    {
        var fixture = CommandFixture.Create();
        var first = CreatePageSummary(fixture.NotebookId, "Beta page");
        var second = CreatePageSummary(fixture.NotebookId, "Alpha page");
        var readTargets = new List<PageReference>();
        fixture.Application.SearchAsyncHandler = (request, _) => Task.FromResult(
            new SearchPage(
                new[] { first, second },
                2,
                request.Offset,
                request.Limit));
        fixture.Application.ReadAsyncHandler = (target, _) =>
        {
            readTargets.Add(target);
            return Task.FromResult(PageOperationResult.Succeeded(Page.Create("Alpha page", "body")));
        };

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
            Assert.That(readTargets, Has.Count.EqualTo(1));
            Assert.That(readTargets[0].NotebookId, Is.EqualTo(fixture.NotebookId));
            Assert.That(readTargets[0].PageId, Is.EqualTo(first.PageId));
            Assert.That(fixture.Context.SaveCount, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Verifies the current empty-target behavior that will be changed when list leaves the ViewModel path.
    /// </summary>
    [Test]
    public async Task List_WithoutSelectedNotebook_UsesEmptyNotebookScope()
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
            Assert.That(result, Is.Zero);
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.Scope, Is.EqualTo(SearchRangeType.Notebook));
            Assert.That(request.NotebookIds, Is.Empty);
            Assert.That(fixture.Output.PageListCallCount, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Verifies that startup failures use the common not-found error mapping and do not save configuration.
    /// </summary>
    [Test]
    public async Task Find_WhenStartupFails_UsesCommonErrorMapping()
    {
        var fixture = CommandFixture.Create();
        fixture.Context.CreateViewModelException = new FileNotFoundException("database is missing");

        var result = await fixture.Find.ExecuteAsync(
            "Roadmap",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(fixture.Context.LoadCount, Is.EqualTo(1));
            Assert.That(fixture.Context.CreateCount, Is.EqualTo(1));
            Assert.That(fixture.Context.SaveCount, Is.Zero);
            Assert.That(fixture.TerminalUi.HomeRunCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo("Error: database is missing" + Environment.NewLine));
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
            RecordingTerminalUi terminalUi,
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
            TerminalUi = terminalUi;
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

        internal RecordingTerminalUi TerminalUi { get; }

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
            var context = new RecordingContextFactory(
                configuration,
                token => new MemoriaNoteViewModel(
                    configuration,
                    workspace,
                    application,
                    NullLogger<MemoriaNoteViewModel>.Instance,
                    token));
            var terminalUi = new RecordingTerminalUi();
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
                terminalUi,
                output,
                new FindCommandHandler(
                    executor,
                    context,
                    normalizer,
                    terminalUi),
                new EditCommandHandler(executor, context, terminalUi),
                new NewCommandHandler(executor, context, terminalUi),
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
        readonly Func<CancellationToken, MemoriaNoteViewModel> _viewModelFactory;

        internal RecordingContextFactory(
            ConfigurationCli configuration,
            Func<CancellationToken, MemoriaNoteViewModel> viewModelFactory)
        {
            _configuration = configuration;
            _viewModelFactory = viewModelFactory;
        }

        internal int LoadCount { get; private set; }

        internal int CreateCount { get; private set; }

        internal int SaveCount { get; private set; }

        internal CancellationToken LastCancellationToken { get; private set; }

        internal MemoriaNoteViewModel? LastViewModel { get; private set; }

        internal Exception? CreateViewModelException { get; set; }

        public ConfigurationCli LoadConfiguration()
        {
            LoadCount++;
            return _configuration;
        }

        public Task<MemoriaNoteViewModel> CreateViewModelAsync(
            ConfigurationCli configuration,
            CancellationToken cancellationToken)
        {
            Assert.That(configuration, Is.SameAs(_configuration));
            CreateCount++;
            LastCancellationToken = cancellationToken;
            if (CreateViewModelException != null)
                return Task.FromException<MemoriaNoteViewModel>(CreateViewModelException);

            LastViewModel = _viewModelFactory(cancellationToken);
            return Task.FromResult(LastViewModel);
        }

        public void SaveConfiguration(ConfigurationCli configuration)
        {
            Assert.That(configuration, Is.SameAs(_configuration));
            SaveCount++;
        }
    }

    sealed class RecordingTerminalUi : ITerminalUi
    {
        internal int HomeRunCount { get; private set; }

        internal int ManageRunCount { get; private set; }

        internal bool OpenEditor { get; private set; }

        internal MemoriaNoteViewModel? HomeViewModel { get; private set; }

        internal MemoriaNoteViewModel? ManageViewModel { get; private set; }

        internal CancellationToken HomeCancellationToken { get; private set; }

        internal CancellationToken ManageCancellationToken { get; private set; }

        public Task RunHomeAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken)
        {
            HomeRunCount++;
            HomeViewModel = viewModel;
            HomeCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task RunManageAsync(
            MemoriaNoteViewModel viewModel,
            bool openEditor,
            CancellationToken cancellationToken)
        {
            ManageRunCount++;
            ManageViewModel = viewModel;
            OpenEditor = openEditor;
            ManageCancellationToken = cancellationToken;
            return Task.CompletedTask;
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
