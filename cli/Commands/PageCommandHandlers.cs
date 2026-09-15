using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Cli.Editors;

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

        internal Task<int> ExecuteAsync(
            string name,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = await _contextFactory.CreateViewModelAsync(
                    configuration,
                    token);
                viewModel.SearchEntry = _searchQueryNormalizer.Normalize(name);
                viewModel.SearchRange = configuration.State.SearchRange;
                viewModel.SearchMethod = configuration.State.SearchMethod;

                await _terminalUi.RunHomeAsync(viewModel, token);
                _contextFactory.SaveConfiguration(configuration);
                return CliCommandResult.Success();
            }, cancellationToken);
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

        internal Task<int> ExecuteAsync(
            string name,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = await _contextFactory.CreateViewModelAsync(
                    configuration,
                    token);
                viewModel.SearchEntry = name;
                viewModel.SearchRange = SearchRangeType.Notebook;
                viewModel.SearchMethod = SearchMethodType.Heading;

                await _terminalUi.RunManageAsync(
                    viewModel,
                    openEditor: false,
                    token);
                _contextFactory.SaveConfiguration(configuration);
                return CliCommandResult.Success();
            }, cancellationToken);
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

        internal Task<int> ExecuteAsync(
            string name,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (name == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "No name");
                }

                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = await _contextFactory.CreateViewModelAsync(
                    configuration,
                    token);
                viewModel.SearchEntry = name;
                viewModel.SearchRange = SearchRangeType.Notebook;
                viewModel.SearchMethod = SearchMethodType.Heading;
                viewModel.EditingTitle = name;
                viewModel.EditingState = EditorMode.Create;

                await _terminalUi.RunManageAsync(
                    viewModel,
                    openEditor: true,
                    token);
                _contextFactory.SaveConfiguration(configuration);
                return CliCommandResult.Success();
            }, cancellationToken);
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

        internal Task<int> ExecuteAsync(
            string name,
            bool completion,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                if (completion &&
                    configuration.Terminal.Completion == CompletionType.None)
                {
                    return CliCommandResult.Success();
                }

                var viewModel = await _contextFactory.CreateViewModelAsync(
                    configuration,
                    token);
                viewModel.SearchEntry = _searchQueryNormalizer.Normalize(name);
                viewModel.SearchRange = SearchRangeType.Notebook;
                viewModel.SearchMethod = SearchMethodType.Heading;
                await viewModel.ActivateHandler();

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
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }
}
