using System;

namespace MemoriaNote.Cli
{
    internal sealed class FindCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly CliSearchQueryNormalizer _searchQueryNormalizer;
        readonly ITerminalUi _terminalUi;

        internal FindCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            CliSearchQueryNormalizer searchQueryNormalizer,
            ITerminalUi terminalUi)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _searchQueryNormalizer = searchQueryNormalizer ??
                throw new ArgumentNullException(nameof(searchQueryNormalizer));
            _terminalUi = terminalUi ??
                throw new ArgumentNullException(nameof(terminalUi));
        }

        internal int Execute(string name = null)
        {
            return _executor.Execute(() =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                viewModel.SearchEntry = _searchQueryNormalizer.Normalize(name);
                viewModel.SearchRange = configuration.State.SearchRange;
                viewModel.SearchMethod = configuration.State.SearchMethod;

                _terminalUi.RunHome(viewModel);
                _contextFactory.SaveConfiguration(configuration);
                return 0;
            });
        }
    }

    internal sealed class EditCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly ITerminalUi _terminalUi;

        internal EditCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            ITerminalUi terminalUi)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _terminalUi = terminalUi ??
                throw new ArgumentNullException(nameof(terminalUi));
        }

        internal int Execute(string name = null)
        {
            return _executor.Execute(() =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                viewModel.SearchEntry = name;
                viewModel.SearchRange = SearchRangeType.Notebook;
                viewModel.SearchMethod = SearchMethodType.Heading;

                _terminalUi.RunManage(viewModel, openEditor: false);
                _contextFactory.SaveConfiguration(configuration);
                return 0;
            });
        }
    }

    internal sealed class NewCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly ITerminalUi _terminalUi;

        internal NewCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            ITerminalUi terminalUi)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _terminalUi = terminalUi ??
                throw new ArgumentNullException(nameof(terminalUi));
        }

        internal int Execute(string name)
        {
            return _executor.Execute(() =>
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                viewModel.SearchEntry = name;
                viewModel.SearchRange = SearchRangeType.Notebook;
                viewModel.SearchMethod = SearchMethodType.Heading;
                viewModel.EditingTitle = name;
                viewModel.EditingState = EditorMode.Create;

                _terminalUi.RunManage(viewModel, openEditor: true);
                _contextFactory.SaveConfiguration(configuration);
                return 0;
            });
        }
    }

    internal sealed class ListCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly CliSearchQueryNormalizer _searchQueryNormalizer;
        readonly ICommandOutput _output;

        internal ListCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            CliSearchQueryNormalizer searchQueryNormalizer,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _searchQueryNormalizer = searchQueryNormalizer ??
                throw new ArgumentNullException(nameof(searchQueryNormalizer));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal int Execute(string name = null, bool completion = false)
        {
            return _executor.Execute(() =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                if (completion &&
                    configuration.Terminal.Completion == CompletionType.None)
                {
                    return 0;
                }

                var viewModel = _contextFactory.CreateViewModel(configuration);
                viewModel.SearchEntry = _searchQueryNormalizer.Normalize(name);
                viewModel.SearchRange = SearchRangeType.Notebook;
                viewModel.SearchMethod = SearchMethodType.Heading;
                viewModel.ActivateHandler().Wait();

                if (completion)
                {
                    _output.WritePageCompletion(
                        viewModel.Contents,
                        viewModel.ContentsCount);
                }
                else
                {
                    _output.WritePageList(
                        viewModel.Contents,
                        viewModel.ContentsCount);
                }

                _contextFactory.SaveConfiguration(configuration);
                return 0;
            });
        }
    }
}
