using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Adapts legacy workspace APIs to the application use cases.
    /// </summary>
    internal sealed class WorkspaceCompatibilityFacade
    {
        readonly Func<IEnumerable<Notebook>> _notebooks;
        readonly Func<Notebook> _selectedNotebook;
        readonly WorkspaceNotebookContextResolver _contextResolver;
        readonly IPageUseCase _pageUseCase;
        readonly ISearchUseCase _searchUseCase;

        internal WorkspaceCompatibilityFacade(
            Func<IEnumerable<Notebook>> notebooks,
            Func<Notebook> selectedNotebook)
        {
            _notebooks = notebooks ?? throw new ArgumentNullException(nameof(notebooks));
            _selectedNotebook = selectedNotebook ?? throw new ArgumentNullException(nameof(selectedNotebook));
            _contextResolver = new WorkspaceNotebookContextResolver(notebooks);
            _pageUseCase = new PageUseCase(_contextResolver, new PageValidationPolicy());
            _searchUseCase = new SearchUseCase(ResolveSearchRepository);
        }

        internal async Task<SearchResult> SearchAsync(
            string searchEntry,
            SearchRangeType searchRange,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            if (!Enum.IsDefined(typeof(SearchRangeType), searchRange))
                throw new ArgumentOutOfRangeException(nameof(searchRange));

            var startTime = DateTime.UtcNow;
            var selectedNotebook = _selectedNotebook();
            var request = searchRange == SearchRangeType.Notebook
                ? SearchRequest.ForNotebook(
                    searchEntry,
                    searchMethod,
                    selectedNotebook == null
                        ? null
                        : NotebookId.FromDatabasePath(selectedNotebook.DatabasePath),
                    skipCount,
                    takeCount)
                : SearchRequest.ForWorkspace(
                    searchEntry,
                    searchMethod,
                    _notebooks().Select(notebook => NotebookId.FromDatabasePath(notebook.DatabasePath)),
                    skipCount,
                    takeCount);
            var result = await _searchUseCase.SearchAsync(request, token).ConfigureAwait(false);
            return new SearchResult
            {
                Contents = result.Items.Select(ToCompatibilityContent).ToList(),
                Count = result.TotalCount,
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };
        }

        internal Page Read(PageReference target)
        {
            if (target == null)
                return null;

            var result = _pageUseCase.ReadAsync(target, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return result.IsSuccess ? SetParent(result.Page, target.NotebookId) : null;
        }

        internal PageOperationResult ValidateCreate(CreatePageCommand command)
        {
            return command == null
                ? MissingOwner()
                : _pageUseCase.ValidateCreateAsync(command, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
        }

        internal PageOperationResult ValidateEdit(EditPageCommand command)
        {
            return command == null
                ? PageNotSelected()
                : _pageUseCase.ValidateEditAsync(command, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
        }

        internal PageOperationResult ValidateRename(RenamePageCommand command)
        {
            return command == null
                ? PageNotSelected()
                : _pageUseCase.ValidateRenameAsync(command, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
        }

        internal PageOperationResult ValidateDelete(DeletePageCommand command)
        {
            return command == null
                ? PageNotSelected()
                : _pageUseCase.ValidateDeleteAsync(command, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
        }

        internal TextManageResult Create(CreatePageCommand command)
        {
            var result = command == null
                ? MissingOwner()
                : _pageUseCase.CreateAsync(command, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            return ToTextManageResult(TextManageType.Create, result, command?.NotebookId);
        }

        internal TextManageResult Edit(EditPageCommand command)
        {
            var result = command == null
                ? PageNotSelected()
                : _pageUseCase.EditAsync(command, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            return ToTextManageResult(TextManageType.Edit, result, command?.NotebookId);
        }

        internal TextManageResult Rename(RenamePageCommand command)
        {
            var result = command == null
                ? PageNotSelected()
                : _pageUseCase.RenameAsync(command, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            return ToTextManageResult(TextManageType.Rename, result, command?.NotebookId);
        }

        internal TextManageResult Delete(DeletePageCommand command, IContent originalContent)
        {
            var result = command == null
                ? PageNotSelected()
                : _pageUseCase.DeleteAsync(command, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            var compatibilityResult = ToTextManageResult(
                TextManageType.Delete,
                result,
                command?.NotebookId);
            if (!compatibilityResult.Result)
                compatibilityResult.Content = originalContent?.GetContent();

            return compatibilityResult;
        }

        internal CreatePageCommand CreateCreateCommand(string name, string text)
        {
            var selectedNotebook = _selectedNotebook();
            return selectedNotebook == null
                ? null
                : new CreatePageCommand(
                    NotebookId.FromDatabasePath(selectedNotebook.DatabasePath),
                    name,
                    text);
        }

        internal static PageReference CreateReference(IContent content)
        {
            if (content == null ||
                string.IsNullOrWhiteSpace(content.OwnerDataSource) ||
                content.Guid == Guid.Empty)
            {
                return null;
            }

            return new PageReference(
                NotebookId.FromDatabasePath(content.OwnerDataSource),
                PageId.FromGuid(content.Guid));
        }

        internal static List<string> ToErrorMessages(
            TextManageType operation,
            PageOperationResult result)
        {
            return result.Errors
                .Select(error => PageOperationMessageMapper.ToErrorMessage(operation, error))
                .ToList();
        }

        IPageSearchRepository ResolveSearchRepository(NotebookId notebookId)
        {
            return ResolveSearchNote(notebookId)?.SearchRepository;
        }

        Content ToCompatibilityContent(PageSummary summary)
        {
            var content = PageSummaryContentAdapter.ToContent(summary);
            content.Parent = ResolveSearchNote(summary.NotebookId);
            return content;
        }

        Notebook ResolveSearchNote(NotebookId notebookId)
        {
            var notebook = _contextResolver.ResolveNotebook(notebookId);
            if (notebook != null)
                return notebook;

            var selectedNotebook = _selectedNotebook();
            return selectedNotebook != null &&
                NotebookId.FromDatabasePath(selectedNotebook.DatabasePath) == notebookId
                    ? selectedNotebook
                    : null;
        }

        T SetParent<T>(T content, NotebookId notebookId) where T : class, IContent
        {
            if (content != null)
                content.Parent = _contextResolver.ResolveNotebook(notebookId);

            return content;
        }

        TextManageResult ToTextManageResult(
            TextManageType operation,
            PageOperationResult result,
            NotebookId notebookId)
        {
            var page = result.IsSuccess ? SetParent(result.Page, notebookId) : null;
            return new TextManageResult
            {
                Operation = operation,
                Result = result.IsSuccess,
                Content = page?.GetContent(),
                Errors = ToErrorMessages(operation, result),
                Notification = result.IsSuccess
                    ? PageOperationMessageMapper.ToSuccessNotification(operation)
                    : PageOperationMessageMapper.ToFailureNotification(operation)
            };
        }

        static PageOperationResult MissingOwner()
        {
            return PageOperationResult.Failed(
                PageOperationStatus.OwnerNotFound,
                PageErrorCode.OwnerNotFound);
        }

        static PageOperationResult PageNotSelected()
        {
            return PageOperationResult.ValidationFailed(
                new[] { PageErrorCode.PageNotSelected });
        }
    }
}
