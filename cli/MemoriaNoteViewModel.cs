using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive;
using System.Reactive.Linq;
using MemoriaNote.Application;
using MemoriaNote.Cli.Editors;
using MemoriaNote.Domain;
using MemoriaNote.Models;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Represents the reactive presentation state for the CLI application.
    /// </summary>
    public class MemoriaNoteViewModel : ReactiveObject
    {
        readonly IMemoriaNoteApplicationService _applicationService;
        readonly ILogger<MemoriaNoteViewModel> _logger;
        readonly CancellationToken _applicationCancellationToken;
        readonly CliErrorMapper _errorMapper = new CliErrorMapper();

        /// <summary>
        /// Initializes the view model from an explicitly composed application session.
        /// </summary>
        /// <param name="configuration">The persisted CLI configuration.</param>
        /// <param name="session">The composed application session.</param>
        /// <param name="logger">The presentation logger.</param>
        /// <param name="cancellationToken">Cancels application operations.</param>
        public MemoriaNoteViewModel(
            ConfigurationCli configuration,
            ApplicationSession session,
            ILogger<MemoriaNoteViewModel> logger,
            CancellationToken cancellationToken = default)
            : this(
                configuration,
                session?.Workspace ?? throw new ArgumentNullException(nameof(session)),
                session.ApplicationService,
                logger,
                cancellationToken)
        {
        }

        /// <summary>
        /// Initializes the view model with explicit presentation dependencies.
        /// </summary>
        /// <param name="configuration">The persisted CLI configuration.</param>
        /// <param name="workspace">The workspace used by the service.</param>
        /// <param name="applicationService">The UI-independent application service.</param>
        /// <param name="logger">The presentation logger.</param>
        /// <param name="cancellationToken">Cancels application operations.</param>
        public MemoriaNoteViewModel(
            ConfigurationCli configuration,
            Workspace workspace,
            IMemoriaNoteApplicationService applicationService,
            ILogger<MemoriaNoteViewModel> logger,
            CancellationToken cancellationToken = default)
        {
            Configuration = configuration ??
                throw new ArgumentNullException(nameof(configuration));
            Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _applicationService = applicationService ??
                throw new ArgumentNullException(nameof(applicationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _applicationCancellationToken = cancellationToken;

            ActivateHandler = async () =>
            {
                try
                {
                    _applicationCancellationToken.ThrowIfCancellationRequested();
                    OnActivate();
                }
                catch (Exception exception) when (_errorMapper.IsStorageException(exception))
                {
                    LogInfrastructureError(exception, "Activation");
                    return;
                }

                await OnSearchContentsAsync(
                    SearchEntry,
                    SearchRange,
                    SearchMethod,
                    0,
                    _applicationCancellationToken);
            };
            Activate = ReactiveCommand.CreateFromTask(ActivateHandler);

            SearchHandler = () => OnSearchContentsAsync(
                SearchEntry,
                SearchRange,
                SearchMethod,
                0,
                _applicationCancellationToken);
            Search = ReactiveCommand.CreateFromTask(SearchHandler);

            var canPageNext = this.WhenAnyValue(
                x => x.ContentsViewPageIndex,
                x => x.MaxViewResultCount,
                x => x.ContentsCount,
                (pi, maxView, count) =>
                    _applicationService.GetNextPageOffset(
                        ViewPageIndexToContentsIndex(pi.Item1, pi.Item2, maxView),
                        maxView,
                        count).HasValue);

            PageNext = ReactiveCommand.CreateFromTask(
                () => OnSearchContentsAsync(SearchEntry, SearchRange, SearchMethod,
                        _applicationService.GetNextPageOffset(
                            SelectedContentsIndex,
                            MaxViewResultCount,
                            ContentsCount) ?? SelectedContentsIndex,
                        _applicationCancellationToken),
                canPageNext
            );

            var canPagePrev = this.WhenAnyValue(
                x => x.ContentsViewPageIndex,
                x => x.MaxViewResultCount,
                x => x.ContentsCount,
                (pi, maxView, count) =>
                    _applicationService.GetPreviousPageOffset(
                        ViewPageIndexToContentsIndex(pi.Item1, pi.Item2, maxView),
                        maxView).HasValue);

            PagePrev = ReactiveCommand.CreateFromTask(
                () => OnSearchContentsAsync(SearchEntry, SearchRange, SearchMethod,
                        _applicationService.GetPreviousPageOffset(
                            SelectedContentsIndex,
                            MaxViewResultCount) ?? SelectedContentsIndex,
                        _applicationCancellationToken),
                canPagePrev
            );

            OpenTextHandler = () => ExecuteInfrastructureOperationAsync(
                () => OnSelectedContextsIndexChangedAsync(_applicationCancellationToken),
                "Open text");
            OpenText = ReactiveCommand.CreateFromTask(OpenTextHandler);

            CreateTextHandler = () => ExecutePageOperationAsync(
                () => CreateTextAsync(
                        EditingTitle.ToString(),
                        EditingText.ToString(),
                        _applicationCancellationToken),
                PageOperationKind.Create,
                "Create text");
            CreateText = ReactiveCommand.CreateFromTask(CreateTextHandler);

            EditTextHandler = () => ExecutePageOperationAsync(
                () => EditTextAsync(
                        OpenedContent,
                        EditingText.ToString(),
                        _applicationCancellationToken),
                PageOperationKind.Edit,
                "Edit text");
            EditText = ReactiveCommand.CreateFromTask(EditTextHandler);

            RenameTextHandler = () => ExecutePageOperationAsync(
                () => RenameTextAsync(
                        OpenedContent,
                        EditingTitle.ToString(),
                        _applicationCancellationToken),
                PageOperationKind.Rename,
                "Rename text");
            RenameText = ReactiveCommand.CreateFromTask(RenameTextHandler);

            DeleteTextHandler = () => ExecutePageOperationAsync(
                () => DeleteTextAsync(
                        OpenedContent,
                        _applicationCancellationToken),
                PageOperationKind.Delete,
                "Delete text");
            DeleteText = ReactiveCommand.CreateFromTask(DeleteTextHandler);

            _noteNames = new ReadOnlyObservableCollection<string>(
                new ObservableCollection<string>(
                    Workspace.Notebooks.Select(note => note.ToString())));
            _selectedNotebookIndex = IndexOfSelectedNotebook();

            _selectedContentsIndex = this
                .WhenAnyValue(
                    x => x.ContentsViewPageIndex,
                    x => x.MaxViewResultCount,
                    (pi, maxView) => ViewPageIndexToContentsIndex(pi.Item1, pi.Item2, maxView)
                )
                .ToProperty(this, x => x.SelectedContentsIndex);

            _searchRangeString = this
                .WhenAnyValue(
                    x => x.SearchRange,
                    (range) =>
                        SearchOptionDisplay.Format(range)
                )
                .ToProperty(this, x => x.SearchRangeString);

            _searchMethodString = this
                .WhenAnyValue(
                    x => x.SearchMethod,
                    (method) =>
                        SearchOptionDisplay.Format(method)
                )
                .ToProperty(this, x => x.SearchMethodString);
        }

        /// <summary>
        /// Selects the note at the specified presentation index.
        /// </summary>
        /// <param name="index">The zero-based note index.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="index"/> does not identify a note.
        /// </exception>
        public void SelectNotebook(int index)
        {
            if (index < 0 || index >= Workspace.Notebooks.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            Workspace.SelectNotebook(Workspace.Notebooks[index]);
            this.RaiseAndSetIfChanged(
                ref _selectedNotebookIndex,
                index,
                nameof(SelectedNotebookIndex));
        }

        int IndexOfSelectedNotebook()
        {
            for (var index = 0; index < Workspace.Notebooks.Count; index++)
            {
                if (Workspace.Notebooks[index].Equals(Workspace.SelectedNotebook))
                    return index;
            }

            return -1;
        }

        /// <summary>
        /// Handles presentation activation after application startup has completed.
        /// </summary>
        protected virtual void OnActivate()
        {
            // Log information that MemoriaNote service has been activated
            _logger.LogInformation("MemoriaNote service has been activated");
        }

        /// <summary>
        /// Callback method that handles the search result and updates the view accordingly.
        /// </summary>
        /// <param name="result">The search result containing the contents</param>
        /// <param name="newContentsIndex">The index of the new contents</param>
        protected async Task<bool> ApplySearchPageAsync(
            SearchPage result,
            int newContentsIndex,
            TimeSpan elapsed,
            CancellationToken cancellationToken,
            Func<bool> canApply)
        {
            var newViewPageIndex = (
                ContentsIndexToViewPage(newContentsIndex, MaxViewResultCount),
                ContentsIndexToViewIndex(newContentsIndex, MaxViewResultCount));
            var newContentItems = result.Items.Select(FormatPageSummary).ToList();
            var newSearchNotice = FormatSearchNotice(result.TotalCount, elapsed);
            PageSummary newOpenedContent = null;
            string newEditingTitle = string.Empty;
            string newEditingText = string.Empty;
            string newEditingUpdateTime = string.Empty;
            string newEditingNoteTitle = string.Empty;
            var newPlaceHolder = PlaceHolderString(0, 0);

            if (result.Items.Count > 0)
            {
                newOpenedContent = result.Items[newViewPageIndex.Item2];
                var readResult = await ReadTextAsync(
                    newOpenedContent,
                    cancellationToken);
                if (!readResult.IsSuccess)
                {
                    _logger.LogWarning(
                        "Search result {ContentId} was not found in its owner note.",
                        newOpenedContent.PageId.Value);
                    return false;
                }

                var page = readResult.Page;
                newPlaceHolder = PlaceHolderString(newContentsIndex, result.TotalCount);
                newEditingTitle = newOpenedContent.Name;
                newEditingText = page.Text;
                newEditingUpdateTime = newOpenedContent.UpdateTime
                    .ToLocalTime()
                    .ToString("ddd MMM dd hh:mm:ss yyyy zzz");
                var notebook = ResolveNotebook(newOpenedContent.NotebookId);
                newEditingNoteTitle = notebook?.Metadata?.Title ?? string.Empty;
            }

            if (!canApply())
                return false;

            ContentsViewPageIndex = newViewPageIndex;
            ContentsCount = result.TotalCount;
            Contents = result.Items.ToList();
            ContentViewItems.Clear();
            ContentViewItems.AddRange(newContentItems);
            SearchNotice = newSearchNotice;
            OpenedContent = newOpenedContent;
            PlaceHolder = newPlaceHolder;
            EditingTitle = newEditingTitle;
            EditingText = newEditingText;
            EditingUpdateTime = newEditingUpdateTime;
            EditingNoteTitle = newEditingNoteTitle;
            _logger.LogInformation("{SearchNotice}", newSearchNotice);
            return true;
        }

        /// <summary>
        /// Callback method that handles the result of text management operations and updates the view notice accordingly.
        /// This method updates the ManageNotice property with the notification from the result and logs the result.
        /// </summary>
        protected void OnPageOperationResultCallback(
            PageOperationKind operation,
            PageOperationResult result)
        {
            ManageNotice = result.IsSuccess
                ? PageOperationMessageMapper.ToSuccessNotification(operation)
                : PageOperationMessageMapper.ToFailureNotification(operation);
            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "{Operation} succeeded: {Notification}",
                    operation,
                    ManageNotice);
            }
            else
            {
                _logger.LogWarning(
                    "{Operation} validation failed: {Notification} {@Errors}",
                    operation,
                    ManageNotice,
                    ToErrorMessages(operation, result));
            }
        }

        /// <summary>
        /// Method to handle the selection change of the contexts in the view.
        /// This method retrieves the content of the selected context and updates the view accordingly.
        /// </summary>
        protected async Task OnSelectedContextsIndexChangedAsync(
            CancellationToken cancellationToken)
        {
            // Check if there are contents available
            if (0 < this.Contents.Count)
            {
                // Retrieve the selected content based on the view page index
                var content = this.Contents[this.ContentsViewPageIndex.Item2];
                // Read the text content of the selected content
                var readResult = await ReadTextAsync(content, cancellationToken);
                if (!readResult.IsSuccess)
                {
                    _logger.LogWarning(
                        "Text {ContentId} was not found in its owner note.",
                        content.PageId.Value);
                    return;
                }

                var page = readResult.Page;
                // Set the placeholder text based on the selected content index and total contents count
                this.PlaceHolder = PlaceHolderString(this.SelectedContentsIndex, this.ContentsCount);
                // Set the opened content to the selected content
                this.OpenedContent = content;
                // Set the editing title to the name of the opened content
                this.EditingTitle = this.OpenedContent.Name;
                // Set the editing text to the text content of the opened content
                this.EditingText = page.Text;
                // Set the editing update time to the local time representation of the content's update time
                this.EditingUpdateTime = content.UpdateTime.ToLocalTime().ToString("ddd MMM dd hh:mm:ss yyyy zzz");
                // Retrieve the note title associated with the current content, if available
                var notebook = ResolveNotebook(content.NotebookId);
                this.EditingNoteTitle = notebook?.Metadata?.Title ?? string.Empty;
            }
            else
            {
                // Set placeholder text to indicate no contents are available
                this.PlaceHolder = PlaceHolderString(0, 0);
                // Reset editing properties when no content is selected
                this.OpenedContent = null;
                this.EditingTitle = string.Empty;
                this.EditingText = string.Empty;
                this.EditingUpdateTime = string.Empty;
                this.EditingNoteTitle = string.Empty;
            }
        }

        /// <summary>
        /// Method to calculate the placeholder text based on the current index and total count of contents.
        /// If there are contents available, it returns the position of the current content in the total count.
        /// If there are no contents available, it returns "0 of 0".
        /// </summary>
        static string PlaceHolderString(int currentIndex, int totalCount) => totalCount > 0 ? $"{currentIndex + 1} of {totalCount}" : "0 of 0";

        static string FormatPageSummary(PageSummary summary)
        {
            if (summary.Name == null)
                return $"PageId={summary.PageId}";

            return summary.Index == 1
                ? summary.Name
                : summary.Name + summary.Index.ToIndexString();
        }

        static string FormatSearchNotice(int totalCount, TimeSpan elapsed)
        {
            var countText = totalCount <= 0
                ? "No results found"
                : $"{totalCount} results found";
            string elapsedText;
            if (elapsed.TotalHours > 1.0)
                elapsedText = Math.Round(elapsed.TotalHours, 2) + " hours";
            else if (elapsed.TotalMinutes > 1.0)
                elapsedText = Math.Round(elapsed.TotalHours, 2) + " minutes";
            else
                elapsedText = Math.Round(elapsed.TotalSeconds, 2) + " seconds";

            return $"{countText} ( {elapsedText} )";
        }

        /// <summary>
        /// Method to convert the contents index to the view index based on the maximum view result count.
        /// This method calculates the view index within a page using the remainder (%) operator.
        /// </summary>
        static int ContentsIndexToViewIndex(int contentsIndex, int maxViewResultCount) => contentsIndex % maxViewResultCount; // % is remainder

        /// <summary>
        /// Method to convert the contents index to the view page based on the maximum view result count.
        /// This method calculates the view page index using integer division.
        /// </summary>
        static int ContentsIndexToViewPage(int contentsIndex, int maxViewResultCount) => (int)(contentsIndex / maxViewResultCount);

        /// <summary>
        /// Method to convert the view page index and view index to the contents index based on the maximum view result count.
        /// This method calculates the contents index based on the page and index within the page.
        /// </summary>
        static int ViewPageIndexToContentsIndex(int page, int index, int maxViewResultCount) => (page * maxViewResultCount) + index;

        #region SearchContents
        readonly object _searchLockObject = new object();
        CancellationTokenSource _searchCancellation;
        long _searchGeneration;

        /// <summary>
        /// Performs a search without applying its result to the service state.
        /// </summary>
        /// <param name="searchEntry">The search entry captured for this request.</param>
        /// <param name="searchRange">The search range captured for this request.</param>
        /// <param name="searchMethod">The search method captured for this request.</param>
        /// <param name="skipCount">The result offset captured for this request.</param>
        /// <param name="takeCount">The result limit captured for this request.</param>
        /// <param name="token">The cancellation token for this request.</param>
        /// <returns>The matching contents and total count.</returns>
        protected virtual Task<SearchPage> SearchAsync(
            string searchEntry,
            SearchRangeType searchRange,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            return _applicationService.SearchAsync(
                CreateSearchRequest(
                    searchEntry,
                    searchRange,
                    searchMethod,
                    skipCount,
                    takeCount),
                token);
        }

        SearchRequest CreateSearchRequest(
            string searchEntry,
            SearchRangeType searchRange,
            SearchMethodType searchMethod,
            int offset,
            int limit)
        {
            if (!Enum.IsDefined(typeof(SearchRangeType), searchRange))
                throw new ArgumentOutOfRangeException(nameof(searchRange));

            return searchRange == SearchRangeType.Notebook
                ? SearchRequest.ForNotebook(
                    searchEntry,
                    searchMethod,
                    SelectedNotebookId(),
                    offset,
                    limit)
                : SearchRequest.ForWorkspace(
                    searchEntry,
                    searchMethod,
                    Workspace.Notebooks.Select(note => NotebookId.FromDatabasePath(note.DatabasePath)),
                    offset,
                    limit);
        }

        NotebookId SelectedNotebookId()
        {
            return Workspace.SelectedNotebook == null
                ? null
                : NotebookId.FromDatabasePath(Workspace.SelectedNotebook.DatabasePath);
        }

        static PageReference CreateReference(PageSummary summary)
        {
            return summary == null
                ? null
                : new PageReference(summary.NotebookId, summary.PageId);
        }

        Notebook ResolveNotebook(NotebookId notebookId)
        {
            if (notebookId == null)
                return null;

            return Workspace.Notebooks.FirstOrDefault(note =>
                NotebookId.FromDatabasePath(note.DatabasePath) == notebookId);
        }

        /// <summary>
        /// Runs a search and applies its result only when it is the latest request.
        /// </summary>
        /// <param name="searchEntry">The search entry captured for this request.</param>
        /// <param name="searchRange">The search range captured for this request.</param>
        /// <param name="searchMethod">The search method captured for this request.</param>
        /// <param name="selectedContentsIndex">The result offset captured for this request.</param>
        /// <returns>
        /// The applied search result, or null when the request was superseded, canceled,
        /// or stopped by an infrastructure failure.
        /// </returns>
        protected async Task<SearchPage> OnSearchContentsAsync(
            string searchEntry,
            SearchRangeType searchRange,
            SearchMethodType searchMethod,
            int selectedContentsIndex,
            CancellationToken cancellationToken)
        {
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            long generation;

            lock (_searchLockObject)
            {
                _searchCancellation?.Cancel();
                _searchCancellation = cancellation;
                generation = ++_searchGeneration;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await SearchAsync(
                    searchEntry,
                    searchRange,
                    searchMethod,
                    selectedContentsIndex,
                    MaxViewResultCount,
                    cancellation.Token);
                stopwatch.Stop();

                if (!IsCurrentSearch(generation, cancellation))
                    return null;

                var applied = await ApplySearchPageAsync(
                    result,
                    selectedContentsIndex,
                    stopwatch.Elapsed,
                    cancellation.Token,
                    () => IsCurrentSearch(generation, cancellation));
                return applied ? result : null;
            }
            catch (OperationCanceledException) when (
                cancellation.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception exception) when (_errorMapper.IsStorageException(exception))
            {
                LogInfrastructureError(exception, "Search");
                return null;
            }
            finally
            {
                lock (_searchLockObject)
                {
                    if (ReferenceEquals(_searchCancellation, cancellation))
                        _searchCancellation = null;
                }

                cancellation.Dispose();
            }
        }

        bool IsCurrentSearch(
            long generation,
            CancellationTokenSource cancellation)
        {
            lock (_searchLockObject)
            {
                return generation == _searchGeneration &&
                    ReferenceEquals(_searchCancellation, cancellation) &&
                    !cancellation.IsCancellationRequested;
            }
        }
        #endregion

        /// <summary>Creates a page through the UI-independent application service.</summary>
        protected virtual Task<PageOperationResult> CreateTextAsync(
            string newName,
            string newText,
            CancellationToken token)
        {
            var notebookId = SelectedNotebookId();
            return notebookId == null
                ? Task.FromResult(MissingOwner())
                : _applicationService.CreateAsync(
                    new CreatePageCommand(notebookId, newName, newText),
                    token);
        }

        /// <summary>Edits a page through the UI-independent application service.</summary>
        protected virtual Task<PageOperationResult> EditTextAsync(
            PageSummary summary,
            string newText,
            CancellationToken token)
        {
            var target = CreateReference(summary);
            return target == null
                ? Task.FromResult(PageNotSelected())
                : _applicationService.EditAsync(
                    new EditPageCommand(target.NotebookId, target.PageId, newText),
                    token);
        }

        /// <summary>Renames a page through the UI-independent application service.</summary>
        protected virtual Task<PageOperationResult> RenameTextAsync(
            PageSummary summary,
            string newName,
            CancellationToken token)
        {
            var target = CreateReference(summary);
            return target == null
                ? Task.FromResult(PageNotSelected())
                : _applicationService.RenameAsync(
                    new RenamePageCommand(target.NotebookId, target.PageId, newName),
                    token);
        }

        /// <summary>Deletes a page through the UI-independent application service.</summary>
        protected virtual Task<PageOperationResult> DeleteTextAsync(
            PageSummary summary,
            CancellationToken token)
        {
            var target = CreateReference(summary);
            return target == null
                ? Task.FromResult(PageNotSelected())
                : _applicationService.DeleteAsync(
                    new DeletePageCommand(target.NotebookId, target.PageId),
                    token);
        }

        /// <summary>Reads a page through the UI-independent application service.</summary>
        protected virtual Task<PageOperationResult> ReadTextAsync(
            PageSummary summary,
            CancellationToken token)
        {
            var target = CreateReference(summary);
            return target == null
                ? Task.FromResult(PageNotSelected())
                : _applicationService.ReadAsync(target, token);
        }

        /// <summary>
        /// Determines if a new text can be created based on the specified name and content.
        /// Validates the creation of a new text with the given name and content, and sets any validation errors.
        /// </summary>
        /// <param name="newName"></param>
        /// <param name="newText"></param>
        /// <param name="cancellationToken">Cancels validation.</param>
        /// <returns>Returns true if the text can be created, false otherwise.</returns>
        public async Task<bool> CanCreateTextAsync(
            string newName,
            string newText,
            CancellationToken cancellationToken)
        {
            try
            {
                var notebookId = SelectedNotebookId();
                var result = notebookId == null
                    ? MissingOwner()
                    : await _applicationService.ValidateCreateAsync(
                        new CreatePageCommand(notebookId, newName, newText),
                        cancellationToken);
                EditingErrors = ToErrorMessages(PageOperationKind.Create, result);
                return result.IsSuccess;
            }
            catch (Exception exception) when (_errorMapper.IsStorageException(exception))
            {
                LogInfrastructureError(exception, "Validate text creation");
                return false;
            }
        }

        /// <summary>
        /// Determines if a text content can be edited based on the specified content and new text.
        /// Validates the editing of a text content with the given content and new text, and sets any validation errors.
        /// </summary>
        /// <param name="summary">The selected page summary.</param>
        /// <param name="newText">The new text content.</param>
        /// <param name="cancellationToken">Cancels validation.</param>
        /// <returns>Returns true if the text can be edited, false otherwise.</returns>
        public async Task<bool> CanEditTextAsync(
            PageSummary summary,
            string newText,
            CancellationToken cancellationToken)
        {
            try
            {
                var target = CreateReference(summary);
                var result = target == null
                    ? PageNotSelected()
                    : await _applicationService.ValidateEditAsync(
                        new EditPageCommand(target.NotebookId, target.PageId, newText),
                        cancellationToken);
                EditingErrors = ToErrorMessages(PageOperationKind.Edit, result);
                return result.IsSuccess;
            }
            catch (Exception exception) when (_errorMapper.IsStorageException(exception))
            {
                LogInfrastructureError(exception, "Validate text editing");
                return false;
            }
        }

        /// <summary>
        /// Determines if a text content can be renamed based on the specified content and new name.
        /// Validates the renaming of a text content with the given content and new name, and sets any validation errors.
        /// </summary>
        /// <param name="summary">The selected page summary.</param>
        /// <param name="newName">The new name for the text.</param>
        /// <param name="cancellationToken">Cancels validation.</param>
        /// <returns>Returns true if the text can be renamed, false otherwise.</returns>
        public async Task<bool> CanRenameTextAsync(
            PageSummary summary,
            string newName,
            CancellationToken cancellationToken)
        {
            try
            {
                var target = CreateReference(summary);
                var result = target == null
                    ? PageNotSelected()
                    : await _applicationService.ValidateRenameAsync(
                        new RenamePageCommand(target.NotebookId, target.PageId, newName),
                        cancellationToken);
                EditingErrors = ToErrorMessages(PageOperationKind.Rename, result);
                return result.IsSuccess;
            }
            catch (Exception exception) when (_errorMapper.IsStorageException(exception))
            {
                LogInfrastructureError(exception, "Validate text renaming");
                return false;
            }
        }

        /// <summary>
        /// Determines if a text content can be deleted based on the specified content.
        /// Validates the deletion of a text content with the given content, and sets any validation errors.
        /// </summary>
        /// <param name="summary">The selected page summary.</param>
        /// <param name="cancellationToken">Cancels validation.</param>
        /// <returns>Returns true if the text can be deleted, false otherwise.</returns>
        public async Task<bool> CanDeleteTextAsync(
            PageSummary summary,
            CancellationToken cancellationToken)
        {
            try
            {
                var target = CreateReference(summary);
                var result = target == null
                    ? PageNotSelected()
                    : await _applicationService.ValidateDeleteAsync(
                        new DeletePageCommand(target.NotebookId, target.PageId),
                        cancellationToken);
                EditingErrors = ToErrorMessages(PageOperationKind.Delete, result);
                return result.IsSuccess;
            }
            catch (Exception exception) when (_errorMapper.IsStorageException(exception))
            {
                LogInfrastructureError(exception, "Validate text deletion");
                return false;
            }
        }

        private async Task ExecutePageOperationAsync(
            Func<Task<PageOperationResult>> operation,
            PageOperationKind operationType,
            string operationName)
        {
            try
            {
                var result = await operation();
                OnPageOperationResultCallback(operationType, result);
            }
            catch (Exception exception) when (_errorMapper.IsStorageException(exception))
            {
                LogInfrastructureError(exception, operationName);
            }
        }

        static List<string> ToErrorMessages(
            PageOperationKind operation,
            PageOperationResult result)
        {
            return result.Errors
                .Select(error => PageOperationMessageMapper.ToErrorMessage(operation, error))
                .ToList();
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

        private async Task ExecuteInfrastructureOperationAsync(
            Func<Task> operation,
            string operationName)
        {
            try
            {
                await operation();
            }
            catch (Exception exception) when (_errorMapper.IsStorageException(exception))
            {
                LogInfrastructureError(exception, operationName);
            }
        }

        private void LogInfrastructureError(Exception exception, string operation)
        {
            _logger.LogError(
                exception,
                "{Operation} failed due to an infrastructure error.",
                operation);
        }

        /// <summary>
        /// Gets the Workspace associated with the current instance.
        /// </summary>
        public Workspace Workspace { get; }

        /// <summary>Gets the persisted CLI configuration for the current session.</summary>
        public ConfigurationCli Configuration { get; }

        /// <summary>
        /// Handler for activating a specific functionality.
        /// </summary>
        public Func<Task> ActivateHandler { get; }
        /// <summary>
        /// Command to activate a specific functionality.
        /// </summary>
        public ReactiveCommand<Unit, Unit> Activate { get; }
        /// <summary>
        /// Handler for searching content.
        /// </summary>
        public Func<Task<SearchPage>> SearchHandler { get; }
        /// <summary>
        /// Command to initiate the search operation.
        /// </summary>
        public ReactiveCommand<Unit, SearchPage> Search { get; }
        /// <summary>
        /// Command to navigate to the next page of content.
        /// </summary>
        public ReactiveCommand<Unit, SearchPage> PageNext { get; }
        /// <summary>
        /// Command to navigate to the previous page of content.
        /// </summary>
        public ReactiveCommand<Unit, SearchPage> PagePrev { get; }
        /// <summary>
        /// Handler for opening a text content.
        /// </summary>
        public Func<Task> OpenTextHandler { get; }
        /// <summary>
        /// Command to open a specific text content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> OpenText { get; }
        /// <summary>
        /// Handler for creating a new text content.
        /// </summary>
        public Func<Task> CreateTextHandler { get; }
        /// <summary>
        /// Command to create a new text content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> CreateText { get; }
        /// <summary>
        /// Handler for editing an existing text content.
        /// </summary>
        public Func<Task> EditTextHandler { get; }
        /// <summary>
        /// Command to edit an existing text content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> EditText { get; }
        /// <summary>
        /// Handler for renaming a text content.
        /// </summary>
        public Func<Task> RenameTextHandler { get; }
        /// <summary>
        /// Command to rename a text content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> RenameText { get; }
        /// <summary>
        /// Handler for deleting a text content.
        /// </summary>
        public Func<Task> DeleteTextHandler { get; }
        /// <summary>
        /// Command to delete a text content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> DeleteText { get; }

        readonly ReadOnlyObservableCollection<string> _noteNames;
        /// <summary>
        /// Gets the collection of names of notes.
        /// </summary>
        public ReadOnlyObservableCollection<string> NoteNames => _noteNames;

        /// <summary>
        /// Collection of content items.
        /// </summary>
        [Reactive] public List<PageSummary> Contents { get; set; }

        int _selectedNotebookIndex;

        /// <summary>
        /// Gets the selected note index.
        /// </summary>
        public int SelectedNotebookIndex => _selectedNotebookIndex;

        /// <summary>
        /// Gets or sets the collection of view items for content.
        /// </summary>
        [Reactive] public List<string> ContentViewItems { get; set; } = new List<string>();

        readonly ObservableAsPropertyHelper<int> _selectedContentsIndex;
        /// <summary>
        /// Gets the selected contents index.
        /// </summary>
        public int SelectedContentsIndex => _selectedContentsIndex.Value;

        /// <summary>
        /// Gets or sets the page index for the contents view.
        /// </summary>
        [Reactive] public (int, int) ContentsViewPageIndex { get; set; }

        /// <summary>
        /// Gets or sets the count of contents.
        /// </summary>
        [Reactive] public int ContentsCount { get; set; }

        /// <summary>
        /// Gets or sets the currently opened content.
        /// </summary>
        [Reactive] public PageSummary OpenedContent { get; set; }

        /// <summary>
        /// Gets or sets the title being edited.
        /// </summary>
        [Reactive] public string EditingTitle { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the text being edited.
        /// </summary>
        [Reactive] public string EditingText { get; set; } = string.Empty;

        /// <summary>
        /// Collection of errors that occurred during editing.
        /// </summary>
        [Reactive] public List<string> EditingErrors { get; set; } = new List<string>();

        /// <summary>
        /// The last time the content was updated during editing.
        /// </summary>
        [Reactive] public string EditingUpdateTime { get; set; } = string.Empty;

        /// <summary>
        /// The title of the note being edited.
        /// </summary>
        [Reactive] public string EditingNoteTitle { get; set; } = string.Empty;

        /// <summary>
        /// The current state of the text content editing.
        /// </summary>
        [Reactive] public EditorMode EditingState { get; set; }

        /// <summary>
        /// Gets or sets the search range type for searching.
        /// </summary>
        [Reactive] public SearchRangeType SearchRange { get; set; }

        /// <summary>
        /// Helper for obtaining the string representation of the search range.
        /// </summary>
        readonly ObservableAsPropertyHelper<string> _searchRangeString;

        /// <summary>
        /// Gets the search range string representation.
        /// </summary>
        public string SearchRangeString => _searchRangeString.Value;

        /// <summary>
        /// Gets or sets the search method type for searching.
        /// </summary>
        [Reactive] public SearchMethodType SearchMethod { get; set; }

        /// <summary>
        /// Helper for obtaining the string representation of the search method.
        /// </summary>
        readonly ObservableAsPropertyHelper<string> _searchMethodString;

        /// <summary>
        /// Gets the search method string representation.
        /// </summary>
        public string SearchMethodString => _searchMethodString.Value;

        /// <summary>
        /// Gets or sets the search entry used for searching.
        /// </summary>
        [Reactive] public string SearchEntry { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the maximum number of results to display in the view.
        /// </summary>
        [Reactive] public int MaxViewResultCount { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the placeholder text for search input.
        /// </summary>
        [Reactive] public string PlaceHolder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the search notice message displayed to the user.
        /// </summary>
        [Reactive] public string SearchNotice { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the manage notice message displayed to the user.
        /// </summary>
        [Reactive] public string ManageNotice { get; set; } = string.Empty;
    }
}
