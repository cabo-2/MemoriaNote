using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Domain;
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
        readonly IExternalEditor _externalEditor;
        readonly ICommandOutput _output;

        internal EditCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            IExternalEditor externalEditor,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _externalEditor = externalEditor ??
                throw new ArgumentNullException(nameof(externalEditor));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string name,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "No name");
                }

                var configuration = _contextFactory.LoadConfiguration();
                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                var selectedNotebook = session.Workspace.SelectedNotebook;
                if (selectedNotebook == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No selected notebook");
                }

                var notebookId = NotebookId.FromDatabasePath(selectedNotebook.DatabasePath);
                var searchResult = await session.ApplicationService.SearchAsync(
                    SearchRequest.ForNotebook(
                        name,
                        SearchMethodType.Heading,
                        notebookId,
                        offset: 0,
                        limit: 2),
                    token);
                if (searchResult.TotalCount == 0 || searchResult.Items.Count == 0)
                {
                    return PageCommandResultMapper.NotFound(PageOperationKind.Edit);
                }

                if (searchResult.TotalCount >= 2 || searchResult.Items.Count >= 2)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Conflict,
                        "More than one text matched the supplied name.");
                }

                var summary = searchResult.Items[0];
                var target = new PageReference(summary.NotebookId, summary.PageId);
                var readResult = await session.ApplicationService.ReadAsync(target, token);
                if (!readResult.IsSuccess)
                {
                    return PageCommandResultMapper.ToCliResult(
                        PageOperationKind.Edit,
                        readResult);
                }

                var page = readResult.Page ??
                    throw new InvalidOperationException("A successful page read returned no page.");
                var initialCommand = new EditPageCommand(
                    target.NotebookId,
                    target.PageId,
                    page.Text);
                var initialValidation = await session.ApplicationService.ValidateEditAsync(
                    initialCommand,
                    token);
                if (!initialValidation.IsSuccess)
                {
                    return PageCommandResultMapper.ToCliResult(
                        PageOperationKind.Edit,
                        initialValidation);
                }

                var editorResult = await _externalEditor.EditAsync(
                    configuration,
                    new ExternalEditorDocument(page.Name, page.Text),
                    token);
                if (!editorResult.IsChanged)
                    return CliCommandResult.Success();

                var editedCommand = new EditPageCommand(
                    target.NotebookId,
                    target.PageId,
                    editorResult.Text);
                var editedValidation = await session.ApplicationService.ValidateEditAsync(
                    editedCommand,
                    token);
                if (!editedValidation.IsSuccess)
                {
                    return PageCommandResultMapper.ToCliResult(
                        PageOperationKind.Edit,
                        editedValidation);
                }

                var editResult = await session.ApplicationService.EditAsync(
                    editedCommand,
                    token);
                if (!editResult.IsSuccess)
                {
                    return PageCommandResultMapper.ToCliResult(
                        PageOperationKind.Edit,
                        editResult);
                }

                _output.WriteLine(
                    PageOperationMessageMapper.ToSuccessNotification(PageOperationKind.Edit));
                _contextFactory.SaveConfiguration(configuration);
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class NewCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly IExternalEditor _externalEditor;
        readonly ICommandOutput _output;

        internal NewCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            IExternalEditor externalEditor,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _externalEditor = externalEditor ??
                throw new ArgumentNullException(nameof(externalEditor));
            _output = output ?? throw new ArgumentNullException(nameof(output));
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
                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                var selectedNotebook = session.Workspace.SelectedNotebook;
                if (selectedNotebook == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No selected notebook");
                }

                var notebookId = NotebookId.FromDatabasePath(selectedNotebook.DatabasePath);
                var initialCommand = new CreatePageCommand(
                    notebookId,
                    name,
                    string.Empty);
                var initialValidation = await session.ApplicationService.ValidateCreateAsync(
                    initialCommand,
                    token);
                if (!initialValidation.IsSuccess)
                {
                    return PageCommandResultMapper.ToCliResult(
                        PageOperationKind.Create,
                        initialValidation);
                }

                var editorResult = await _externalEditor.EditAsync(
                    configuration,
                    new ExternalEditorDocument(name, string.Empty),
                    token);
                if (!editorResult.IsChanged)
                    return CliCommandResult.Success();

                var editedCommand = new CreatePageCommand(
                    notebookId,
                    name,
                    editorResult.Text);
                var editedValidation = await session.ApplicationService.ValidateCreateAsync(
                    editedCommand,
                    token);
                if (!editedValidation.IsSuccess)
                {
                    return PageCommandResultMapper.ToCliResult(
                        PageOperationKind.Create,
                        editedValidation);
                }

                var createResult = await session.ApplicationService.CreateAsync(
                    editedCommand,
                    token);
                if (!createResult.IsSuccess)
                {
                    return PageCommandResultMapper.ToCliResult(
                        PageOperationKind.Create,
                        createResult);
                }

                _output.WriteLine(
                    PageOperationMessageMapper.ToSuccessNotification(PageOperationKind.Create));
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

                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                var selectedNotebook = session.Workspace.SelectedNotebook;
                if (selectedNotebook == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No selected notebook");
                }

                var request = SearchRequest.ForNotebook(
                    _searchQueryNormalizer.Normalize(name),
                    SearchMethodType.Heading,
                    NotebookId.FromDatabasePath(selectedNotebook.DatabasePath),
                    offset: 0,
                    limit: 1000);
                var page = await session.ApplicationService.SearchAsync(
                    request,
                    token);

                if (completion)
                {
                    _output.WritePageCompletion(
                        page.Items,
                        page.TotalCount);
                }
                else
                {
                    _output.WritePageList(
                        page.Items,
                        page.TotalCount);
                }

                _contextFactory.SaveConfiguration(configuration);
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }
}
