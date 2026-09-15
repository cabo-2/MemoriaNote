using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies command handlers through replaceable CLI boundaries.</summary>
[TestFixture]
public sealed class CommandHandlerTests
{
    /// <summary>Verifies that the new-page handler configures UI state without running a TUI.</summary>
    [Test]
    public void New_UsesManageEditorAdapterAndPersistsConfiguration()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var executor = new CliCommandExecutor(
            output,
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

        var result = handler.Execute("Roadmap");

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

        public MemoriaNoteViewModel CreateViewModel(ConfigurationCli configuration)
        {
            Assert.That(configuration, Is.SameAs(_configuration));
            return _viewModel;
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

        public void RunHome(MemoriaNoteViewModel viewModel)
        {
            Assert.Fail("The home screen was not expected.");
        }

        public void RunManage(MemoriaNoteViewModel viewModel, bool openEditor)
        {
            ViewModel = viewModel;
            OpenEditor = openEditor;
            ManageRunCount++;
        }
    }
}
