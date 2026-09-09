using System;
using System.Data.Common;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using DynamicData;
using DynamicData.Binding;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote
{
    /// <summary>
    /// Class definition for MemoriaNoteService inheriting from ReactiveObject
    /// </summary>
    public class MemoriaNoteService : ReactiveObject
    {
        readonly INoteMigrator _noteMigrator;

        /// <summary>
        /// Initializes the service from the configured workgroup.
        /// </summary>
        public MemoriaNoteService() : this(NotePersistence.CreateMigrator())
        {
        }

        MemoriaNoteService(INoteMigrator noteMigrator)
            : this(CreateConfiguredWorkgroup(noteMigrator), noteMigrator)
        {
        }

        /// <summary>
        /// Initializes the service with the specified workgroup.
        /// </summary>
        /// <param name="workgroup">The workgroup used by the service.</param>
        protected MemoriaNoteService(Workgroup workgroup)
            : this(workgroup, NotePersistence.CreateMigrator())
        {
        }

        /// <summary>
        /// Initializes the service with an explicit workgroup and note migrator.
        /// </summary>
        /// <param name="workgroup">The workgroup used by the service.</param>
        /// <param name="noteMigrator">The service used for note database lifecycle operations.</param>
        protected MemoriaNoteService(Workgroup workgroup, INoteMigrator noteMigrator)
        {
            Workgroup = workgroup ?? throw new ArgumentNullException(nameof(workgroup));
            _noteMigrator = noteMigrator ??
                throw new ArgumentNullException(nameof(noteMigrator));

            ActivateHandler = async () =>
            {
                try
                {
                    await Task.Run(OnActivate);
                }
                catch (Exception exception) when (IsInfrastructureException(exception))
                {
                    LogInfrastructureError(exception, "Activation");
                    return;
                }

                await OnSearchContentsAsync(SearchEntry, SearchRange, SearchMethod, 0);
            };
            Activate = ReactiveCommand.CreateFromTask(ActivateHandler);

            SearchHandler = () => OnSearchContentsAsync(
                SearchEntry,
                SearchRange,
                SearchMethod,
                0);
            Search = ReactiveCommand.CreateFromTask(SearchHandler);

            var canPageNext = this.WhenAnyValue(
                x => x.ContentsViewPageIndex,
                x => x.MaxViewResultCount,
                x => x.ContentsCount,
                (pi, maxView, count) =>
                    ViewPageIndexToContentsIndex(pi.Item1, pi.Item2, maxView) + maxView < count);

            PageNext = ReactiveCommand.CreateFromTask(
                () => OnSearchContentsAsync(SearchEntry, SearchRange, SearchMethod,
                        SelectedContentsIndex + MaxViewResultCount),
                canPageNext
            );

            var canPagePrev = this.WhenAnyValue(
                x => x.ContentsViewPageIndex,
                x => x.MaxViewResultCount,
                x => x.ContentsCount,
                (pi, maxView, count) =>
                    0 <= ViewPageIndexToContentsIndex(pi.Item1, pi.Item2, maxView) - maxView);

            PagePrev = ReactiveCommand.CreateFromTask(
                () => OnSearchContentsAsync(SearchEntry, SearchRange, SearchMethod,
                        SelectedContentsIndex - MaxViewResultCount),
                canPagePrev
            );

            OpenTextHandler = () => ExecuteInfrastructureOperation(
                OnSelectedContextsIndexChanged,
                "Open text");
            OpenText = ReactiveCommand.Create(OpenTextHandler);

            CreateTextHandler = () => OnCreateText(EditingTitle.ToString(), EditingText.ToString(), OnTextManageResultCallback);
            CreateText = ReactiveCommand.Create(CreateTextHandler);

            EditTextHandler = () => OnEditText(OpenedContent?.GetContent(), EditingText.ToString(), OnTextManageResultCallback);
            EditText = ReactiveCommand.Create(EditTextHandler);

            RenameTextHandler = () => OnRenameText(OpenedContent?.GetContent(), EditingTitle.ToString(), OnTextManageResultCallback);
            RenameText = ReactiveCommand.Create(RenameTextHandler);

            DeleteTextHandler = () => OnDeleteText(OpenedContent?.GetContent(), OnTextManageResultCallback);
            DeleteText = ReactiveCommand.Create(DeleteTextHandler);

            Workgroup.Notes.ToObservableChangeSet()
                .Transform(note => note.ToString())
                .Bind(out _noteNames)
                .Subscribe();

            _selectedNoteIndex = this
                .WhenAnyValue(x => x.Workgroup.SelectedNoteIndex)
                .ToProperty(this, x => x.SelectedNoteIndex);

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
                        range.ToDisplayString()
                )
                .ToProperty(this, x => x.SearchMethodString);

            _searchMethodString = this
                .WhenAnyValue(
                    x => x.SearchMethod,
                    (method) =>
                        method.ToDisplayString()
                )
                .ToProperty(this, x => x.SearchMethodString);
        }

        private static Workgroup CreateConfiguredWorkgroup(INoteMigrator noteMigrator)
        {
            if (!File.Exists(Configuration.Instance.DefaultDataSourcePath))
            {
                noteMigrator.CreateAsync(
                        Configuration.Instance.DefaultNoteName,
                        Configuration.Instance.DefaultNoteTitle,
                        Configuration.Instance.DefaultDataSourcePath,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Log.Logger.Information("Default note created");
            }

            return Configuration.Instance.Workgroup.Build();
        }

        /// <summary>
        /// Method to be called when the MemoriaNote service is activated.
        /// This method triggers database migration and logs the activation information.
        /// </summary>
        protected virtual void OnActivate()
        {
            // Perform database migration for each data source in the configuration
            DatabaseMigrate();
            // Log information that MemoriaNote service has been activated
            Log.Logger.Information("MemoriaNote service has been activated");
        }

        /// <summary>
        /// Method to migrate the database for each data source in the configuration.
        /// This method iterates through each data source and performs the migration.
        /// </summary>
        protected void DatabaseMigrate()
        {
            foreach (var dataSource in Configuration.Instance.DataSources)
            {
                _noteMigrator.MigrateAsync(dataSource, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
        }

        /// <summary>
        /// Callback method that handles the search result and updates the view accordingly.
        /// </summary>
        /// <param name="result">The search result containing the contents</param>
        /// <param name="newContentsIndex">The index of the new contents</param>
        protected void OnSearchResultCallback(SearchResult result, int newContentsIndex)
        {
            var newViewPageIndex = (
                ContentsIndexToViewPage(newContentsIndex, MaxViewResultCount),
                ContentsIndexToViewIndex(newContentsIndex, MaxViewResultCount));
            var newContentItems = result.Contents.ConvertAll(c => c.ToString());
            var newSearchNotice = result.ToString();
            Content newOpenedContent = null;
            string newEditingTitle = string.Empty;
            string newEditingText = string.Empty;
            string newEditingUpdateTime = string.Empty;
            string newEditingNoteTitle = string.Empty;
            var newPlaceHolder = PlaceHolderString(0, 0);

            if (result.Contents.Count > 0)
            {
                newOpenedContent = result.Contents[newViewPageIndex.Item2];
                var page = Workgroup.ReadAll(newOpenedContent);
                if (page == null)
                {
                    Log.Logger.Warning(
                        "Search result {ContentId} was not found in its owner note.",
                        newOpenedContent.Guid);
                    return;
                }

                newPlaceHolder = PlaceHolderString(newContentsIndex, result.Count);
                newEditingTitle = newOpenedContent.Name;
                newEditingText = page.Text;
                newEditingUpdateTime = newOpenedContent.UpdateTime
                    .ToLocalTime()
                    .ToString("ddd MMM dd hh:mm:ss yyyy zzz");
                var note = newOpenedContent.Parent as Note;
                newEditingNoteTitle = note?.Metadata?.Title ?? string.Empty;
            }

            ContentsViewPageIndex = newViewPageIndex;
            ContentsCount = result.Count;
            Contents = result.Contents;
            ContentViewItems.Clear();
            ContentViewItems.Add(newContentItems);
            SearchNotice = newSearchNotice;
            OpenedContent = newOpenedContent;
            PlaceHolder = newPlaceHolder;
            EditingTitle = newEditingTitle;
            EditingText = newEditingText;
            EditingUpdateTime = newEditingUpdateTime;
            EditingNoteTitle = newEditingNoteTitle;
            Log.Logger.Information(newSearchNotice);
        }

        /// <summary>
        /// Callback method that handles the result of text management operations and updates the view notice accordingly.
        /// This method updates the ManageNotice property with the notification from the result and logs the result.
        /// </summary>
        protected void OnTextManageResultCallback(TextManageResult result)
        {
            this.ManageNotice = result.Notification;
            if (result.Result)
                Log.Logger.Information(result.ToString());
            else
                Log.Logger.Warning(
                    "{Operation} validation failed: {Notification} {@Errors}",
                    result.Operation,
                    result.Notification,
                    result.Errors);
        }

        /// <summary>
        /// Method to handle the selection change of the contexts in the view.
        /// This method retrieves the content of the selected context and updates the view accordingly.
        /// </summary>
        protected void OnSelectedContextsIndexChanged()
        {
            // Check if there are contents available
            if (0 < this.Contents.Count)
            {
                // Retrieve the selected content based on the view page index
                var content = this.Contents[this.ContentsViewPageIndex.Item2];
                // Read the text content of the selected content
                var page = Workgroup.ReadAll(content);
                if (page == null)
                {
                    Log.Logger.Warning(
                        "Text {ContentId} was not found in its owner note.",
                        content.Guid);
                    return;
                }

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
                var note = content.Parent as Note;
                this.EditingNoteTitle = note?.Metadata?.Title ?? string.Empty;
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
        protected virtual Task<SearchResult> SearchAsync(
            string searchEntry,
            SearchRangeType searchRange,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            return Workgroup.SearchAsync(
                searchEntry,
                searchRange,
                searchMethod,
                skipCount,
                takeCount,
                token);
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
        protected async Task<SearchResult> OnSearchContentsAsync(
            string searchEntry,
            SearchRangeType searchRange,
            SearchMethodType searchMethod,
            int selectedContentsIndex)
        {
            var cancellation = new CancellationTokenSource();
            long generation;

            lock (_searchLockObject)
            {
                _searchCancellation?.Cancel();
                _searchCancellation = cancellation;
                generation = ++_searchGeneration;
            }

            try
            {
                var result = await SearchAsync(
                    searchEntry,
                    searchRange,
                    searchMethod,
                    selectedContentsIndex,
                    MaxViewResultCount,
                    cancellation.Token);

                lock (_searchLockObject)
                {
                    if (generation != _searchGeneration ||
                        !ReferenceEquals(_searchCancellation, cancellation) ||
                        cancellation.IsCancellationRequested)
                        return null;

                    OnSearchResultCallback(result, selectedContentsIndex);
                }

                return result;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception exception) when (IsInfrastructureException(exception))
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
        #endregion

        /// <summary>
        /// Handles the creation of a new text with the specified name and content, providing the result to the callback function.
        /// </summary>
        /// <param name="newName">The name of the new text.</param>
        /// <param name="newText">The content of the new text.</param>
        /// <param name="result">The callback function to receive the result of the create text operation.</param>
        protected void OnCreateText(string newName, string newText, Action<TextManageResult> result)
        {
            ExecuteTextManagement(
                () => Workgroup.CreateText(newName, newText),
                result,
                "Create text");
        }

        /// <summary>
        /// Determines if a new text can be created based on the specified name and content.
        /// Validates the creation of a new text with the given name and content, and sets any validation errors.
        /// </summary>
        /// <param name="newName"></param>
        /// <param name="newText"></param>
        /// <returns>Returns true if the text can be created, false otherwise.</returns>
        public bool CanCreateText(string newName, string newText)
        {
            try
            {
                var validation = Workgroup.ValidateCreateText(newName, newText, out var errors);
                EditingErrors = errors;
                return validation;
            }
            catch (Exception exception) when (IsInfrastructureException(exception))
            {
                LogInfrastructureError(exception, "Validate text creation");
                return false;
            }
        }

        /// <summary>
        /// Handles the editing of a text content with the specified new text, providing the result to the callback function.
        /// </summary>
        /// <param name="content">The content to be edited.</param>
        /// <param name="newText">The new text content.</param>
        /// <param name="result">The callback function to receive the result of the edit text operation.</param>
        protected void OnEditText(Content content, string newText, Action<TextManageResult> result)
        {
            ExecuteTextManagement(
                () => Workgroup.EditText(content, newText),
                result,
                "Edit text");
        }

        /// <summary>
        /// Determines if a text content can be edited based on the specified content and new text.
        /// Validates the editing of a text content with the given content and new text, and sets any validation errors.
        /// </summary>
        /// <param name="content">The content to be edited.</param>
        /// <param name="newText">The new text content.</param>
        /// <returns>Returns true if the text can be edited, false otherwise.</returns>
        public bool CanEditText(Content content, string newText)
        {
            try
            {
                var validation = Workgroup.ValidateEditText(content, newText, out var errors);
                EditingErrors = errors;
                return validation;
            }
            catch (Exception exception) when (IsInfrastructureException(exception))
            {
                LogInfrastructureError(exception, "Validate text editing");
                return false;
            }
        }

        /// <summary>
        /// Handles the renaming of a text with the specified new name, providing the result to the callback function.
        /// </summary>
        /// <param name="content">The content of the text to be renamed.</param>
        /// <param name="newName">The new name for the text.</param>
        /// <param name="result">The callback function to receive the result of the rename text operation.</param>
        protected void OnRenameText(Content content, string newName, Action<TextManageResult> result)
        {
            ExecuteTextManagement(
                () => Workgroup.RenameText(content, newName),
                result,
                "Rename text");
        }

        /// <summary>
        /// Determines if a text content can be renamed based on the specified content and new name.
        /// Validates the renaming of a text content with the given content and new name, and sets any validation errors.
        /// </summary>
        /// <param name="content">The content to be renamed.</param>
        /// <param name="newName">The new name for the text.</param>
        /// <returns>Returns true if the text can be renamed, false otherwise.</returns>
        public bool CanRenameText(Content content, string newName)
        {
            try
            {
                var validation = Workgroup.ValidateRenameText(content, newName, out var errors);
                EditingErrors = errors;
                return validation;
            }
            catch (Exception exception) when (IsInfrastructureException(exception))
            {
                LogInfrastructureError(exception, "Validate text renaming");
                return false;
            }
        }

        /// <summary>
        /// Handles the deletion of a text content with the specified content, providing the result to the callback function.
        /// </summary>
        /// <param name="content">The content to be deleted.</param>
        /// <param name="result">The callback function to receive the result of the delete text operation.</param>
        protected void OnDeleteText(Content content, Action<TextManageResult> result)
        {
            ExecuteTextManagement(
                () => Workgroup.DeleteText(content),
                result,
                "Delete text");
        }

        /// <summary>
        /// Determines if a text content can be deleted based on the specified content.
        /// Validates the deletion of a text content with the given content, and sets any validation errors.
        /// </summary>
        /// <param name="content">The content to be deleted.</param>
        /// <returns>Returns true if the text can be deleted, false otherwise.</returns>
        public bool CanDeleteText(Content content)
        {
            try
            {
                var validation = Workgroup.ValidateDeleteText(content, out var errors);
                EditingErrors = errors;
                return validation;
            }
            catch (Exception exception) when (IsInfrastructureException(exception))
            {
                LogInfrastructureError(exception, "Validate text deletion");
                return false;
            }
        }

        private void ExecuteTextManagement(
            Func<TextManageResult> operation,
            Action<TextManageResult> result,
            string operationName)
        {
            try
            {
                var manageResult = operation();
                if (manageResult != null)
                    result(manageResult);
            }
            catch (Exception exception) when (IsInfrastructureException(exception))
            {
                LogInfrastructureError(exception, operationName);
            }
        }

        private static void ExecuteInfrastructureOperation(
            Action operation,
            string operationName)
        {
            try
            {
                operation();
            }
            catch (Exception exception) when (IsInfrastructureException(exception))
            {
                LogInfrastructureError(exception, operationName);
            }
        }

        private static bool IsInfrastructureException(Exception exception)
        {
            if (exception is DbException ||
                exception is IOException ||
                exception is UnauthorizedAccessException)
                return true;

            if (exception is DbUpdateException dbUpdateException)
                return dbUpdateException.InnerException != null &&
                    IsInfrastructureException(dbUpdateException.InnerException);

            if (exception is AggregateException aggregateException)
                return aggregateException.InnerExceptions.Count > 0 &&
                    aggregateException.InnerExceptions.All(IsInfrastructureException);

            return false;
        }

        private static void LogInfrastructureError(Exception exception, string operation)
        {
            Log.Logger.Error(
                exception,
                "{Operation} failed due to an infrastructure error.",
                operation);
        }

        /// <summary>
        /// Gets or sets the Workgroup associated with the current instance.
        /// </summary>
        [Reactive] public Workgroup Workgroup { get; set; }

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
        public Func<Task<SearchResult>> SearchHandler { get; }
        /// <summary>
        /// Command to initiate the search operation.
        /// </summary>
        public ReactiveCommand<Unit, SearchResult> Search { get; }
        /// <summary>
        /// Command to navigate to the next page of content.
        /// </summary>
        public ReactiveCommand<Unit, SearchResult> PageNext { get; }
        /// <summary>
        /// Command to navigate to the previous page of content.
        /// </summary>
        public ReactiveCommand<Unit, SearchResult> PagePrev { get; }
        /// <summary>
        /// Handler for opening a text content.
        /// </summary>
        public Action OpenTextHandler { get; }
        /// <summary>
        /// Command to open a specific text content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> OpenText { get; }
        /// <summary>
        /// Handler for creating a new text content.
        /// </summary>
        public Action CreateTextHandler { get; }
        /// <summary>
        /// Command to create a new text content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> CreateText { get; }
        /// <summary>
        /// Handler for editing an existing text content.
        /// </summary>
        public Action EditTextHandler { get; }
        /// <summary>
        /// Command to edit an existing text content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> EditText { get; }
        /// <summary>
        /// Handler for renaming a text content.
        /// </summary>
        public Action RenameTextHandler { get; }
        /// <summary>
        /// Command to rename a text content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> RenameText { get; }
        /// <summary>
        /// Handler for deleting a text content.
        /// </summary>
        public Action DeleteTextHandler { get; }
        /// <summary>
        /// Command to delete a text content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> DeleteText { get; }

        readonly ReadOnlyObservableCollection<string> _noteNames;
        /// <summary>
        /// Gets the collection of names of notes.
        /// </summary>
        [IgnoreDataMember] public ReadOnlyObservableCollection<string> NoteNames => _noteNames;

        /// <summary>
        /// Collection of content items.
        /// </summary>
        [Reactive, DataMember] public List<Content> Contents { get; set; }

        readonly ObservableAsPropertyHelper<int> _selectedNoteIndex;

        /// <summary>
        /// Gets the selected note index.
        /// </summary>
        [IgnoreDataMember] public int SelectedNoteIndex => _selectedNoteIndex.Value;

        /// <summary>
        /// Gets or sets the collection of view items for content.
        /// </summary>
        [Reactive, DataMember] public List<string> ContentViewItems { get; set; } = new List<string>();

        readonly ObservableAsPropertyHelper<int> _selectedContentsIndex;
        /// <summary>
        /// Gets the selected contents index.
        /// </summary>
        [IgnoreDataMember] public int SelectedContentsIndex => _selectedContentsIndex.Value;

        /// <summary>
        /// Gets or sets the page index for the contents view.
        /// </summary>
        [Reactive, DataMember] public (int, int) ContentsViewPageIndex { get; set; }

        /// <summary>
        /// Gets or sets the count of contents.
        /// </summary>
        [Reactive, DataMember] public int ContentsCount { get; set; }

        /// <summary>
        /// Gets or sets the currently opened content.
        /// </summary>
        [Reactive] public Content OpenedContent { get; set; }

        /// <summary>
        /// Gets or sets the title being edited.
        /// </summary>
        [Reactive, DataMember] public string EditingTitle { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the text being edited.
        /// </summary>
        [Reactive, DataMember] public string EditingText { get; set; } = string.Empty;

        /// <summary>
        /// Collection of errors that occurred during editing.
        /// </summary>
        [Reactive, DataMember] public List<string> EditingErrors { get; set; } = new List<string>();

        /// <summary>
        /// The last time the content was updated during editing.
        /// </summary>
        [Reactive, DataMember] public string EditingUpdateTime { get; set; } = string.Empty;

        /// <summary>
        /// The title of the note being edited.
        /// </summary>
        [Reactive, DataMember] public string EditingNoteTitle { get; set; } = string.Empty;

        /// <summary>
        /// The current state of the text content editing.
        /// </summary>
        [Reactive, DataMember] public TextManageType EditingState { get; set; }

        /// <summary>
        /// Gets or sets the search range type for searching.
        /// </summary>
        [Reactive, DataMember] public SearchRangeType SearchRange { get; set; }
        
        /// <summary>
        /// Helper for obtaining the string representation of the search range.
        /// </summary>
        readonly ObservableAsPropertyHelper<string> _searchRangeString;

        /// <summary>
        /// Gets the search range string representation.
        /// </summary>
        [IgnoreDataMember] public string SearchRangeString => _searchRangeString.Value;

        /// <summary>
        /// Gets or sets the search method type for searching.
        /// </summary>
        [Reactive, DataMember] public SearchMethodType SearchMethod { get; set; }

        /// <summary>
        /// Helper for obtaining the string representation of the search method.
        /// </summary>
        readonly ObservableAsPropertyHelper<string> _searchMethodString;

        /// <summary>
        /// Gets the search method string representation.
        /// </summary>
        [IgnoreDataMember] public string SearchMethodString => _searchMethodString.Value;

        /// <summary>
        /// Gets or sets the search entry used for searching.
        /// </summary>
        [Reactive, DataMember] public string SearchEntry { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the maximum number of results to display in the view.
        /// </summary>
        [Reactive, DataMember] public int MaxViewResultCount { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the placeholder text for search input.
        /// </summary>
        [Reactive, DataMember] public string PlaceHolder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the search notice message displayed to the user.
        /// </summary>
        [Reactive, DataMember] public string SearchNotice { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the manage notice message displayed to the user.
        /// </summary>
        [Reactive, DataMember] public string ManageNotice { get; set; } = string.Empty;
    }
}
