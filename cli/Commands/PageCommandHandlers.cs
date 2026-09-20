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

        const string TemporarilyUnavailableMessage =
            "The find command is temporarily unavailable. Use 'mn ls' to list page names; " +
            "full-text search will be redesigned after the initial release.";

        internal FindCommandHandler(CliCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        internal Task<int> ExecuteAsync(
            string query,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(
                _ => CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    TemporarilyUnavailableMessage),
                cancellationToken);
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
                {
                    _contextFactory.SaveConfiguration(configuration);
                    _output.WriteLine("No changes.");
                    return CliCommandResult.Success();
                }

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

                _contextFactory.SaveConfiguration(configuration);
                _output.WriteLine(
                    PageOperationMessageMapper.ToSuccessNotification(PageOperationKind.Edit));
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class NewCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly IExternalEditor _externalEditor;
        readonly ICommandOutput _output;

        internal NewCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            INotebookTargetSessionResolver targetResolver,
            IExternalEditor externalEditor,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _targetResolver = targetResolver ??
                throw new ArgumentNullException(nameof(targetResolver));
            _externalEditor = externalEditor ??
                throw new ArgumentNullException(nameof(externalEditor));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string name,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(null, null, name, cancellationToken);
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookOption,
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

                var session = await _targetResolver.ResolveAsync(
                    workspaceOption,
                    notebookOption,
                    token);
                var selectedNotebook = session.Workspace.SelectedNotebook;
                if (selectedNotebook == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No selected notebook");
                }

                var configuration = _contextFactory.LoadConfiguration();
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
                {
                    _output.WriteLine("No changes.");
                    return CliCommandResult.Success();
                }

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

                var createdPage = createResult.Page;
                if (createdPage == null)
                {
                    _output.WriteLine(
                        PageOperationMessageMapper.ToSuccessNotification(
                            PageOperationKind.Create));
                }
                else
                {
                    _output.WriteLine(
                        $"Created page \"{createdPage.Name}\" ({createdPage.Guid:D}).");
                }
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class ListCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly ICommandOutput _output;

        internal ListCommandHandler(
            CliCommandExecutor executor,
            INotebookTargetSessionResolver targetResolver,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _targetResolver = targetResolver ??
                throw new ArgumentNullException(nameof(targetResolver));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookOption,
            int? limit,
            bool longFormat,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (limit <= 0)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Limit must be greater than zero.");
                }

                var session = await _targetResolver.ResolveAsync(
                    workspaceOption,
                    notebookOption,
                    token);
                var selectedNotebook = session.Workspace.SelectedNotebook;
                if (selectedNotebook == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No selected notebook");
                }

                var pages = await session.ApplicationService.ListPagesAsync(
                    new PageListRequest(
                        NotebookId.FromDatabasePath(selectedNotebook.DatabasePath),
                        limit),
                    token);
                _output.WritePageList(pages, longFormat);
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }
}
