using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Cli.Editors;

namespace MemoriaNote.Cli
{
    internal enum PageOperationKind
    {
        Create,
        Edit,
        Rename,
        Delete
    }

    internal sealed class PageListItem
    {
        internal PageListItem(NotebookPageSummary summary)
            : this(
                summary?.PageId ?? throw new ArgumentNullException(nameof(summary)),
                summary.Name,
                summary.CreateTimeUtc,
                summary.UpdateTimeUtc)
        {
        }

        internal PageListItem(
            PageId pageId,
            string name,
            DateTimeOffset createTimeUtc,
            DateTimeOffset updateTimeUtc)
        {
            PageId = pageId;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            CreateTimeUtc = createTimeUtc;
            UpdateTimeUtc = updateTimeUtc;
        }

        internal PageId PageId { get; }

        internal string Name { get; }

        internal DateTimeOffset CreateTimeUtc { get; }

        internal DateTimeOffset UpdateTimeUtc { get; }
    }

    internal static class PageCommandFailureMapper
    {
        internal static CliCommandResult ToCliResult(
            PageOperationKind operation,
            NotebookFailure failure)
        {
            if (failure == null)
                throw new ArgumentNullException(nameof(failure));

            return failure.Code switch
            {
                "page.name_conflict" => CliCommandResult.Failure(
                    CliErrorKind.Conflict,
                    "A page with this name already exists in the notebook."),
                "notebook.read_only" => ReadOnly(operation),
                "page.revision_conflict" => CliCommandResult.Failure(
                    CliErrorKind.Conflict,
                    "The page changed while it was being edited. Run the command again " +
                    "to edit the latest content."),
                "page.not_found" => CliCommandResult.Failure(
                    CliErrorKind.NotFound,
                    "The target page was not found."),
                _ => NotebookApplicationFailureMapper.ToCliResult(failure)
            };
        }

        internal static async Task<CliCommandResult> ValidateWritableAsync(
            NotebookTargetSession session,
            PageOperationKind operation,
            CancellationToken cancellationToken)
        {
            var result = await session.Metadata.GetAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
                return NotebookApplicationFailureMapper.ToCliResult(result.Failure);

            return result.Value.Values.TryGetValue("ReadOnly", out var value) &&
                bool.TryParse(value, out var readOnly) &&
                readOnly
                    ? ReadOnly(operation)
                    : null;
        }

        static CliCommandResult ReadOnly(PageOperationKind operation) =>
            CliCommandResult.Failure(
                CliErrorKind.Conflict,
                operation switch
                {
                    PageOperationKind.Create =>
                        "The notebook is read-only; a page cannot be created.",
                    PageOperationKind.Edit =>
                        "The notebook is read-only; the page cannot be edited.",
                    PageOperationKind.Rename =>
                        "The notebook is read-only; the page cannot be renamed.",
                    PageOperationKind.Delete =>
                        "The notebook is read-only; the page cannot be deleted.",
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                });

        internal static CliCommandResult ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "A page name is required.");
            }
            if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "A page name cannot start or end with whitespace.");
            }
            if (name.Any(char.IsControl))
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "A page name cannot contain control characters.");
            }

            return null;
        }
    }

    internal static class PageListCommandParser
    {
        internal static bool TryParseSort(
            string value,
            out NotebookPageQuerySort sort)
        {
            switch (value)
            {
                case null:
                case "name":
                    sort = NotebookPageQuerySort.Name;
                    return true;
                case "updated":
                    sort = NotebookPageQuerySort.Updated;
                    return true;
                case "created":
                    sort = NotebookPageQuerySort.Created;
                    return true;
                default:
                    sort = default;
                    return false;
            }
        }
    }

    internal sealed class EditCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly IEditorOptionsProvider _editorOptionsProvider;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly IExternalEditor _externalEditor;
        readonly ICommandOutput _output;

        internal EditCommandHandler(
            CliCommandExecutor executor,
            IEditorOptionsProvider editorOptionsProvider,
            INotebookTargetSessionResolver targetResolver,
            IExternalEditor externalEditor,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _editorOptionsProvider = editorOptionsProvider ??
                throw new ArgumentNullException(nameof(editorOptionsProvider));
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
            return ExecuteAsync(
                null,
                null,
                name,
                null,
                null,
                null,
                cancellationToken);
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookOption,
            string pageName,
            string pageId,
            string editorPath,
            IEnumerable<string> editorArguments,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (!PageTargetCommandParser.TryCreateSelector(
                    pageName,
                    pageId,
                    out var selector,
                    out var selectorFailure))
                    return selectorFailure;
                if (!EditorCommandOptionParser.TryCreate(
                    editorPath,
                    editorArguments,
                    out var commandOverride,
                    out var editorFailure))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        editorFailure);
                }
                var editorOptions = commandOverride == null
                    ? _editorOptionsProvider.Load()
                    : EditorOptions.Missing;

                await using var session = await _targetResolver.ResolveAsync(
                    workspaceOption,
                    notebookOption,
                    token);
                var resolution = await PageTargetCommandParser.ResolveAsync(
                    session.Pages,
                    selector,
                    token);
                if (!resolution.IsSuccess)
                {
                    return PageTargetCommandParser.ToResolutionFailure(
                        resolution.Failure,
                        selector);
                }

                var page = resolution.Value;
                var writableFailure = await PageCommandFailureMapper.ValidateWritableAsync(
                    session,
                    PageOperationKind.Edit,
                    token);
                if (writableFailure != null)
                    return writableFailure;

                var initialText = page.Text ?? string.Empty;

                var editorResult = await _externalEditor.EditAsync(
                    editorOptions,
                    commandOverride,
                    new ExternalEditorDocument(page.Name, initialText),
                    token);
                if (!editorResult.IsChanged)
                {
                    _output.WriteLine(
                        $"The editor made no changes to page " +
                        $"{CliMessage.Quote(page.Name)} ({page.PageId.Value:D}).");
                    return CliCommandResult.Success();
                }

                var editResult = await session.Pages.ReplaceAsync(
                    page.PageId,
                    page.Revision,
                    new NotebookPageReplaceRequest(
                        page.Name,
                        editorResult.Text ?? string.Empty,
                        page.Tags),
                    token);
                if (!editResult.IsSuccess)
                {
                    return PageCommandFailureMapper.ToCliResult(
                        PageOperationKind.Edit,
                        editResult.Failure);
                }

                var updatedPage = editResult.Value;
                _output.WriteLine(
                    $"Updated page {CliMessage.Quote(updatedPage.Name)} " +
                    $"({page.PageId.Value:D}).");
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class NewCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly IEditorOptionsProvider _editorOptionsProvider;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly IExternalEditor _externalEditor;
        readonly ICommandOutput _output;

        internal NewCommandHandler(
            CliCommandExecutor executor,
            IEditorOptionsProvider editorOptionsProvider,
            INotebookTargetSessionResolver targetResolver,
            IExternalEditor externalEditor,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _editorOptionsProvider = editorOptionsProvider ??
                throw new ArgumentNullException(nameof(editorOptionsProvider));
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
            return ExecuteAsync(
                workspaceOption,
                notebookOption,
                name,
                null,
                null,
                cancellationToken);
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookOption,
            string name,
            string editorPath,
            IEnumerable<string> editorArguments,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (name == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "A page name is required.");
                }
                if (!EditorCommandOptionParser.TryCreate(
                    editorPath,
                    editorArguments,
                    out var commandOverride,
                    out var editorFailure))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        editorFailure);
                }
                var editorOptions = commandOverride == null
                    ? _editorOptionsProvider.Load()
                    : EditorOptions.Missing;

                var nameFailure = PageCommandFailureMapper.ValidateName(name);
                if (nameFailure != null)
                    return nameFailure;

                await using var session = await _targetResolver.ResolveAsync(
                    workspaceOption,
                    notebookOption,
                    token);
                var existing = await session.Pages.GetByNameAsync(name, token)
                    .ConfigureAwait(false);
                if (existing.IsSuccess)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Conflict,
                        "A page with this name already exists in the notebook.");
                }
                if (existing.Failure.Code != "page.not_found")
                {
                    return PageCommandFailureMapper.ToCliResult(
                        PageOperationKind.Create,
                        existing.Failure);
                }
                var writableFailure = await PageCommandFailureMapper.ValidateWritableAsync(
                    session,
                    PageOperationKind.Create,
                    token);
                if (writableFailure != null)
                    return writableFailure;

                var editorResult = await _externalEditor.EditAsync(
                    editorOptions,
                    commandOverride,
                    new ExternalEditorDocument(name, string.Empty),
                    token);
                if (!editorResult.IsChanged)
                {
                    _output.WriteLine(
                        $"Page {CliMessage.Quote(name)} was not created because the " +
                        "editor made no changes.");
                    return CliCommandResult.Success();
                }

                var createResult = await session.Pages.CreateAsync(
                    new NotebookPageCreateRequest(
                        name,
                        editorResult.Text ?? string.Empty,
                        Array.Empty<string>()),
                    token);
                if (!createResult.IsSuccess)
                {
                    return PageCommandFailureMapper.ToCliResult(
                        PageOperationKind.Create,
                        createResult.Failure);
                }

                var createdPage = createResult.Value;
                _output.WriteLine(
                    $"Created page {CliMessage.Quote(createdPage.Name)} " +
                    $"({createdPage.PageId.Value:D}).");
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
            string pageNamePattern,
            string sort,
            bool reverse,
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
                if (!PageListCommandParser.TryParseSort(sort, out var parsedSort))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Sort must be name, updated, or created.");
                }

                await using var session = await _targetResolver.ResolveAsync(
                    workspaceOption,
                    notebookOption,
                    token);
                var started = await session.Pages.StartPageQueryAsync(
                        new NotebookPageQueryRequest(
                            pageNamePattern,
                            parsedSort,
                            reverse,
                            limit),
                        token)
                    .ConfigureAwait(false);
                if (!started.IsSuccess)
                    return NotebookApplicationFailureMapper.ToCliResult(started.Failure);

                await using var query = started.Value;
                var displayedCount = 0;
                var includeHeader = true;
                try
                {
                    while (true)
                    {
                        var read = await query.ReadAsync(token).ConfigureAwait(false);
                        if (!read.IsSuccess)
                        {
                            return displayedCount == 0
                                ? NotebookApplicationFailureMapper.ToCliResult(read.Failure)
                                : PartialFailure(displayedCount);
                        }

                        var summaries = read.Value.Summaries;
                        if (summaries.Count > 0)
                        {
                            _output.WritePageList(
                                summaries.Select(page => new PageListItem(page)).ToArray(),
                                longFormat,
                                includeHeader);
                            displayedCount += summaries.Count;
                            includeHeader = false;
                        }
                        if (read.Value.IsCompleted)
                            return CliCommandResult.Success();
                    }
                }
                catch (OperationCanceledException) when (displayedCount > 0)
                {
                    return PartialFailure(displayedCount);
                }
            }, cancellationToken);
        }

        CliCommandResult PartialFailure(int displayedCount)
        {
            var message =
                $"Listing failed after {displayedCount} page(s); output is incomplete.";
            _output.WriteErrorLine(message);
            return CliCommandResult.Failure(
                CliErrorKind.Partial,
                message,
                alreadyReported: true);
        }
    }

    internal sealed class CatCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly ICommandOutput _output;

        internal CatCommandHandler(
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
            string pageName,
            string pageId,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (!PageTargetCommandParser.TryCreateSelector(
                    pageName,
                    pageId,
                    out var selector,
                    out var selectorFailure))
                    return selectorFailure;

                await using var session = await _targetResolver.ResolveAsync(
                    workspaceOption,
                    notebookOption,
                    token);
                var resolution = await PageTargetCommandParser.ResolveAsync(
                    session.Pages,
                    selector,
                    token);
                if (!resolution.IsSuccess)
                {
                    return PageTargetCommandParser.ToResolutionFailure(
                        resolution.Failure,
                        selector);
                }

                var page = resolution.Value;
                _output.Write(page.Text ?? string.Empty);
                return CliCommandResult.Success();
            }, cancellationToken);
        }

    }

    internal sealed class RenamePageCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly ICommandOutput _output;

        internal RenamePageCommandHandler(
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
            string pageName,
            string pageId,
            string newName,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (!PageTargetCommandParser.TryCreateSelector(
                    pageName,
                    pageId,
                    out var selector,
                    out var selectorFailure))
                    return selectorFailure;
                if (newName == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Specify a new page name.");
                }
                var nameFailure = PageCommandFailureMapper.ValidateName(newName);
                if (nameFailure != null)
                    return nameFailure;

                await using var session = await _targetResolver.ResolveAsync(
                    workspaceOption,
                    notebookOption,
                    token);
                var resolution = await PageTargetCommandParser.ResolveAsync(
                    session.Pages,
                    selector,
                    token);
                if (!resolution.IsSuccess)
                {
                    return PageTargetCommandParser.ToResolutionFailure(
                        resolution.Failure,
                        selector);
                }

                var page = resolution.Value;
                var renameResult = await session.Pages.ReplaceAsync(
                    page.PageId,
                    page.Revision,
                    new NotebookPageReplaceRequest(
                        newName,
                        page.Text,
                        page.Tags),
                    token);
                if (!renameResult.IsSuccess)
                {
                    return PageCommandFailureMapper.ToCliResult(
                        PageOperationKind.Rename,
                        renameResult.Failure);
                }

                _output.WriteLine(
                    $"Renamed page to {CliMessage.Quote(newName)} " +
                    $"({page.PageId.Value:D}).");
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class DeletePageCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly CommandPrompt _prompt;
        readonly ICommandOutput _output;

        internal DeletePageCommandHandler(
            CliCommandExecutor executor,
            INotebookTargetSessionResolver targetResolver,
            CommandPrompt prompt,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _targetResolver = targetResolver ??
                throw new ArgumentNullException(nameof(targetResolver));
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookOption,
            string pageName,
            string pageId,
            bool force,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (!PageTargetCommandParser.TryCreateSelector(
                    pageName,
                    pageId,
                    out var selector,
                    out var selectorFailure))
                    return selectorFailure;

                await using var session = await _targetResolver.ResolveAsync(
                    workspaceOption,
                    notebookOption,
                    token);
                var resolution = await PageTargetCommandParser.ResolveAsync(
                    session.Pages,
                    selector,
                    token);
                if (!resolution.IsSuccess)
                {
                    return PageTargetCommandParser.ToResolutionFailure(
                        resolution.Failure,
                        selector);
                }

                var page = resolution.Value;
                var writableFailure = await PageCommandFailureMapper.ValidateWritableAsync(
                    session,
                    PageOperationKind.Delete,
                    token);
                if (writableFailure != null)
                    return writableFailure;

                var targetDescription = $"page '{page.Name}' ({page.PageId.Value:D})";
                if (dryRun)
                {
                    _output.WriteLine($"Would delete {targetDescription}.");
                    return CliCommandResult.Success();
                }

                if (!force)
                {
                    if (!_prompt.IsInteractive)
                    {
                        return CliCommandResult.Failure(
                            CliErrorKind.Validation,
                            "Interactive confirmation is unavailable. Use --force to delete " +
                            "the resolved page.");
                    }

                    if (!await _prompt.ConfirmPageDeletionAsync(
                            page.Name,
                            page.PageId.Value.ToString("D"),
                            token)
                        .ConfigureAwait(false))
                    {
                        _output.WriteLine("Page deletion canceled.");
                        return CliCommandResult.Success();
                    }
                }

                var deleteResult = await session.Pages.DeleteAsync(
                    page.PageId,
                    page.Revision,
                    token);
                if (!deleteResult.IsSuccess)
                {
                    return PageCommandFailureMapper.ToCliResult(
                        PageOperationKind.Delete,
                        deleteResult.Failure);
                }

                _output.WriteLine(
                    $"Deleted page {CliMessage.Quote(page.Name)} " +
                    $"({page.PageId.Value:D}).");
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }
}
