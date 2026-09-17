using MemoriaNote.Application;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies command handlers through replaceable CLI boundaries.</summary>
[TestFixture]
public sealed class CommandHandlerTests
{
    /// <summary>Verifies that the new-page handler configures UI state without running a TUI.</summary>
    [Test]
    public async Task New_UsesManageEditorAdapterAndPersistsConfiguration()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var executor = new CliCommandExecutor(
            output,
            new CliErrorMapper(),
            NullLogger<CliCommandExecutor>.Instance);
        var configuration = new ConfigurationCli();
        var viewModel = new MemoriaNoteViewModel(
            configuration,
            new Workspace(),
            new StubApplicationService(),
            NullLogger<MemoriaNoteViewModel>.Instance);
        var contextFactory = new StubCommandContextFactory(
            configuration,
            viewModel);
        var terminalUi = new StubTerminalUi();
        var handler = new NewCommandHandler(
            executor,
            contextFactory,
            terminalUi);

        var result = await handler.ExecuteAsync(
            "Roadmap",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(contextFactory.SaveCount, Is.EqualTo(1));
            Assert.That(terminalUi.ManageRunCount, Is.EqualTo(1));
            Assert.That(terminalUi.OpenEditor, Is.True);
            Assert.That(terminalUi.ViewModel, Is.SameAs(viewModel));
            Assert.That(viewModel.SearchEntry, Is.EqualTo("Roadmap"));
            Assert.That(viewModel.SearchRange, Is.EqualTo(SearchRangeType.Notebook));
            Assert.That(viewModel.SearchMethod, Is.EqualTo(SearchMethodType.Heading));
            Assert.That(viewModel.EditingTitle.ToString(), Is.EqualTo("Roadmap"));
            Assert.That(viewModel.EditingState, Is.EqualTo(EditorMode.Create));
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(standardError.ToString(), Is.Empty);
        }
    }

    sealed class StubCommandContextFactory : ICliCommandContextFactory
    {
        readonly ConfigurationCli _configuration;
        readonly MemoriaNoteViewModel _viewModel;

        internal StubCommandContextFactory(
            ConfigurationCli configuration,
            MemoriaNoteViewModel viewModel)
        {
            _configuration = configuration;
            _viewModel = viewModel;
        }

        internal int SaveCount { get; private set; }

        public ConfigurationCli LoadConfiguration()
        {
            return _configuration;
        }

        public Task<ApplicationSession> CreateSessionAsync(
            ConfigurationCli configuration,
            CancellationToken cancellationToken)
        {
            Assert.That(configuration, Is.SameAs(_configuration));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<ApplicationSession>(
                new NotSupportedException("The session path is not used by this test."));
        }

        public Task<MemoriaNoteViewModel> CreateViewModelAsync(
            ConfigurationCli configuration,
            CancellationToken cancellationToken)
        {
            Assert.That(configuration, Is.SameAs(_configuration));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_viewModel);
        }

        public void SaveConfiguration(ConfigurationCli configuration)
        {
            Assert.That(configuration, Is.SameAs(_configuration));
            SaveCount++;
        }
    }

    sealed class StubTerminalUi : ITerminalUi
    {
        internal int ManageRunCount { get; private set; }

        internal bool OpenEditor { get; private set; }

        internal MemoriaNoteViewModel? ViewModel { get; private set; }

        public Task RunHomeAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken)
        {
            Assert.Fail("The home screen was not expected.");
            return Task.CompletedTask;
        }

        public Task RunManageAsync(
            MemoriaNoteViewModel viewModel,
            bool openEditor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ViewModel = viewModel;
            OpenEditor = openEditor;
            ManageRunCount++;
            return Task.CompletedTask;
        }
    }
}
