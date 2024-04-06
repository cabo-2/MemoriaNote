using System;
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

namespace MemoriaNote
{
    /// <summary>
    /// Class definition for MemoriaNoteService inheriting from ReactiveObject
    /// </summary>
    public class MemoriaNoteService : ReactiveObject
    {
        public MemoriaNoteService()
        {
            if (!File.Exists(Configuration.Instance.DefaultDataSourcePath))
            {
                Note.Create(Configuration.Instance.DefaultNoteName, Configuration.Instance.DefaultNoteTitle, Configuration.Instance.DefaultDataSourcePath);
                Log.Logger.Information("Default note created");
            }
            Workgroup = Configuration.Instance.Workgroup.Build();

            ActivateHandler = () => Task.Run(() =>
                {
                    OnActivate();
                    OnSearchContents(SearchEntry, SearchRange, SearchMethod, 0, OnSearchResultCallback);
                });
            Activate = ReactiveCommand.CreateFromTask(ActivateHandler);

            SearchHandler = () => OnSearchContents(SearchEntry, SearchRange, SearchMethod, 0, OnSearchResultCallback);
            Search = ReactiveCommand.Create(
                () => OnSearchContentsAsync(SearchEntry, SearchRange, SearchMethod, 0, OnSearchResultCallback)
            );

            var canPageNext = this.WhenAnyValue(
                x => x.ContentsViewPageIndex,
                x => x.MaxViewResultCount,
                x => x.ContentsCount,
                (pi, maxView, count) =>
                    ViewPageIndexToContentsIndex(pi.Item1, pi.Item2, maxView) + maxView < count);

            PageNext = ReactiveCommand.Create(
                () => OnSearchContentsAsync(SearchEntry, SearchRange, SearchMethod,
                        SelectedContentsIndex + MaxViewResultCount, OnSearchResultCallback),
                canPageNext
            );

            var canPagePrev = this.WhenAnyValue(
                x => x.ContentsViewPageIndex,
                x => x.MaxViewResultCount,
                x => x.ContentsCount,
                (pi, maxView, count) =>
                    0 <= ViewPageIndexToContentsIndex(pi.Item1, pi.Item2, maxView) - maxView);

            PagePrev = ReactiveCommand.Create(
                () => OnSearchContentsAsync(SearchEntry, SearchRange, SearchMethod,
                        SelectedContentsIndex - MaxViewResultCount, OnSearchResultCallback),
                canPagePrev
            );

            OpenTextHandler = () => OnSelectedContextsIndexChanged();
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
                Note.Migrate(dataSource);
            }
        }

        /// <summary>
        /// Callback method that handles the search result and updates the view accordingly.
        /// </summary>
        /// <param name="result">The search result containing the contents</param>
        /// <param name="newContentsIndex">The index of the new contents</param>
        protected void OnSearchResultCallback(SearchResult result, int newContentsIndex)
        {
            // Update the contents view page index based on the new contents index
            this.ContentsViewPageIndex = (ContentsIndexToViewPage(newContentsIndex, MaxViewResultCount), ContentsIndexToViewIndex(newContentsIndex, MaxViewResultCount));
            // Update the total number of contents
            this.ContentsCount = result.Count;
            // Update the contents list with the search result
            this.Contents = result.Contents;
            // Convert the contents to string representation
            var newContentItems = result.Contents.ConvertAll(c => c.ToString());
            // Clear the current content view items and add the new items
            this.ContentViewItems.Clear();
            this.ContentViewItems.Add(newContentItems);
            // Update the search notice with the search result
            this.SearchNotice = result.ToString();
            // Update the selected contexts in the view
            OnSelectedContextsIndexChanged();
            // Log the search result information
            Log.Logger.Information(result.ToString());
        }

        /// <summary>
        /// Callback method that handles the result of text management operations and updates the view notice accordingly.
        /// This method updates the ManageNotice property with the notification from the result and logs the result.
        /// </summary>
        protected void OnTextManageResultCallback(TextManageResult result)
        {
            this.ManageNotice = result.Notification;
            Log.Logger.Information(result.ToString());
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
                // Set the placeholder text based on the selected content index and total contents count
                this.PlaceHolder = PlaceHolderString(this.SelectedContentsIndex, this.ContentsCount);
                // Read the text content of the selected content
                var page = Workgroup.ReadAll(content);
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
        object _searchLockObject = new object();
        List<CancellationTokenSource> _searchJobs = new List<CancellationTokenSource>();
        
        /// <summary>
        /// Method to handle the search operation based on the search entry, search range, search method, and selected contents index.
        /// This method cancels any ongoing search jobs, performs the search based on the specified criteria, and provides the search result to the callback.
        /// </summary>
        protected void OnSearchContents(string searchEntry, SearchRangeType searchRange, SearchMethodType searchMethod, int selectedContentsIndex, Action<SearchResult, int> result)
        {
            // Lock the search operation to ensure thread safety
            lock (_searchLockObject)
            {
                // Cancel any ongoing search jobs
                foreach (var job in _searchJobs)
                    job.Cancel();

                // Clear the list of search jobs to start fresh
                _searchJobs.Clear();

                // Set the skip count and take count based on the selected contents index and maximum view result count
                int skipCount = selectedContentsIndex;
                int takeCount = MaxViewResultCount;

                // Perform the search based on the search method
                if (searchMethod == SearchMethodType.Heading)
                {
                    // Search for heading matches and handle any exceptions
                    SearchResult sr;
                    try
                    {
                        sr = Workgroup.SearchContents(searchEntry, searchRange, skipCount, takeCount);
                        if (sr != null)
                            result(sr, selectedContentsIndex);
                    }
                    catch (Exception ex)
                    {
                        // Log debug information in case of an exception
                        Log.Logger.Debug(ex.Message);
                    }
                }
                else if (searchMethod == SearchMethodType.FullText)
                {
                    // Search for full text matches and handle any exceptions
                    SearchResult sr;
                    try
                    {
                        sr = Workgroup.SearchFullText(searchEntry, searchRange, skipCount, takeCount);
                        if (sr != null)
                            result(sr, selectedContentsIndex);
                    }
                    catch (Exception ex)
                    {
                        // Log debug information in case of an exception
                        Log.Logger.Debug(ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Asynchronously handles the search operation based on the search entry, search range, search method, and selected contents index.
        /// This method cancels any ongoing search jobs, performs the search based on the specified criteria, and provides the search result to the callback.
        /// </summary>
        protected async void OnSearchContentsAsync(string searchEntry, SearchRangeType searchRange, SearchMethodType searchMethod, int selectedContentsIndex, Action<SearchResult, int> result)
        {
            // Create a new cancellation token source
            var cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;

            // Lock the search operation to ensure thread safety
            lock (_searchLockObject)
            {
                // Cancel any ongoing search jobs
                foreach (var job in _searchJobs)
                    job.Cancel();

                // Clear the list of search jobs and add the current one
                _searchJobs.Clear();
                _searchJobs.Add(cts);
            }

            // Set the skip count and take count based on the selected contents index and maximum view result count
            int skipCount = selectedContentsIndex;
            int takeCount = MaxViewResultCount;

            // Perform the search based on the search method
            if (searchMethod == SearchMethodType.Heading)
            {
                // Search for heading matches and handle any exceptions
                SearchResult sr;
                try
                {
                    // Perform asynchronous search contents operation
                    sr = await Workgroup.SearchContentsAsync(searchEntry, searchRange, skipCount, takeCount, token);
                    
                    // If search result is not null, provide it to the callback function
                    if (sr != null)
                        result(sr, selectedContentsIndex);
                }
                catch (InvalidOperationException) { }
                catch (Exception ex)
                {
                    // Log debug information in case of an exception
                    Log.Logger.Debug(ex.Message);
                }
            }
            else if (searchMethod == SearchMethodType.FullText)
            {
                // Search for full text matches and handle any exceptions
                SearchResult sr;
                try
                {
                    // Perform asynchronous search full text operation
                    sr = await Workgroup.SearchFullTextAsync(searchEntry, searchRange, skipCount, takeCount, token);
                    
                    // If search result is not null, provide it to the callback function
                    if (sr != null)
                        result(sr, selectedContentsIndex);
                }
                catch (InvalidOperationException) { }
                catch (Exception ex)
                {
                    // Log debug information in case of an exception
                    Log.Logger.Debug(ex.Message);
                }
            }

            // Release the cancellation token source from the list of search jobs
            lock (_searchLockObject)
            {
                if (_searchJobs.Contains(cts))
                    _searchJobs.Remove(cts);
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
            var mr = Workgroup.CreateText(newName, newText);
            if (mr != null)
                result(mr);
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
            List<string> errors;
            var result = Workgroup.ValidateCreateText(newName, newText, out errors);
            EditingErrors = errors;
            return result;
        }

        /// <summary>
        /// Handles the editing of a text content with the specified new text, providing the result to the callback function.
        /// </summary>
        /// <param name="content">The content to be edited.</param>
        /// <param name="newText">The new text content.</param>
        /// <param name="result">The callback function to receive the result of the edit text operation.</param>
        protected void OnEditText(Content content, string newText, Action<TextManageResult> result)
        {
            var mr = Workgroup.EditText(content, newText);
            if (mr != null)
                result(mr);
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
            List<string> errors;
            var result = Workgroup.ValidateEditText(content, newText, out errors);
            EditingErrors = errors;
            return result;
        }

        /// <summary>
        /// Handles the renaming of a text with the specified new name, providing the result to the callback function.
        /// </summary>
        /// <param name="content">The content of the text to be renamed.</param>
        /// <param name="newName">The new name for the text.</param>
        /// <param name="result">The callback function to receive the result of the rename text operation.</param>
        protected void OnRenameText(Content content, string newName, Action<TextManageResult> result)
        {
            var mr = Workgroup.RenameText(content, newName);
            if (mr != null)
                result(mr);
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
            List<string> errors;
            var result = Workgroup.ValidateRenameText(content, newName, out errors);
            EditingErrors = errors;
            return result;
        }

        /// <summary>
        /// Handles the deletion of a text content with the specified content, providing the result to the callback function.
        /// </summary>
        /// <param name="content">The content to be deleted.</param>
        /// <param name="result">The callback function to receive the result of the delete text operation.</param>
        protected void OnDeleteText(Content content, Action<TextManageResult> result)
        {
            var mr = Workgroup.DeleteText(content);
            if (mr != null)
                result(mr);
        }

        /// <summary>
        /// Determines if a text content can be deleted based on the specified content.
        /// Validates the deletion of a text content with the given content, and sets any validation errors.
        /// </summary>
        /// <param name="content">The content to be deleted.</param>
        /// <returns>Returns true if the text can be deleted, false otherwise.</returns>
        public bool CanDeleteText(Content content)
        {
            List<string> errors;
            var result = Workgroup.ValidateDeleteText(content, out errors);
            EditingErrors = errors;
            return result;
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
        public Action SearchHandler { get; }
        /// <summary>
        /// Command to initiate the search operation.
        /// </summary>
        public ReactiveCommand<Unit, Unit> Search { get; }
        /// <summary>
        /// Command to navigate to the next page of content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> PageNext { get; }
        /// <summary>
        /// Command to navigate to the previous page of content.
        /// </summary>
        public ReactiveCommand<Unit, Unit> PagePrev { get; }
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
