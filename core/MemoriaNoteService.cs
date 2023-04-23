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

        protected virtual void OnActivate()
        {
            DatabaseMigrate();
            Log.Logger.Information("MemoriaNote service activated");
        }

        protected void DatabaseMigrate()
        {
            foreach (var dataSource in Configuration.Instance.DataSources)
            {
                Note.Migrate(dataSource);
            }
        }

        protected void OnSearchResultCallback(SearchResult result, int newContentsIndex)
        {
            this.ContentsViewPageIndex = (ContentsIndexToViewPage(newContentsIndex, MaxViewResultCount), ContentsIndexToViewIndex(newContentsIndex, MaxViewResultCount));
            this.ContentsCount = result.Count;
            this.Contents = result.Contents;
            var newContentItems = result.Contents.ConvertAll(c => c.ToString());
            this.ContentViewItems.Clear();
            this.ContentViewItems.Add(newContentItems);
            this.SearchNotice = result.ToString();
            OnSelectedContextsIndexChanged();
            Log.Logger.Information(result.ToString());
        }

        protected void OnTextManageResultCallback(TextManageResult result)
        {
            this.ManageNotice = result.Notification;
            Log.Logger.Information(result.ToString());
        }

        protected void OnSelectedContextsIndexChanged()
        {
            if (0 < this.Contents.Count)
            {
                var content = this.Contents[this.ContentsViewPageIndex.Item2];
                this.PlaceHolder = PlaceHolderString(this.SelectedContentsIndex, this.ContentsCount);
                var page = Workgroup.ReadAll(content);
                this.OpenedContent = content;
                this.EditingTitle = this.OpenedContent.Name;
                this.EditingText = page.Text;
                this.EditingUpdateTime = content.UpdateTime.ToLocalTime().ToString("ddd MMM dd hh:mm:ss yyyy zzz");
                var note = content.Parent as Note;
                this.EditingNoteTitle = note?.Metadata?.Title ?? string.Empty;
            }
            else
            {
                this.PlaceHolder = PlaceHolderString(0, 0);
                this.OpenedContent = null;
                this.EditingTitle = string.Empty;
                this.EditingText = string.Empty;
                this.EditingUpdateTime = string.Empty;
                this.EditingNoteTitle = string.Empty;
            }
        }

        static string PlaceHolderString(int currentIndex, int totalCount) => totalCount > 0 ? $"{currentIndex + 1} of {totalCount}" : "0 of 0";

        static int ContentsIndexToViewIndex(int contentsIndex, int maxViewResultCount) => contentsIndex % maxViewResultCount; // % is remainder
        static int ContentsIndexToViewPage(int contentsIndex, int maxViewResultCount) => (int)(contentsIndex / maxViewResultCount);
        static int ViewPageIndexToContentsIndex(int page, int index, int maxViewResultCount) => (page * maxViewResultCount) + index;

        #region SearchContents
        object _searchLockObject = new object();
        List<CancellationTokenSource> _searchJobs = new List<CancellationTokenSource>();
        
        protected void OnSearchContents(string searchEntry, SearchRangeType searchRange, SearchMethodType searchMethod, int selectedContentsIndex, Action<SearchResult, int> result)
        {
            lock (_searchLockObject)
            {
                foreach (var job in _searchJobs)
                    job.Cancel();

                _searchJobs.Clear();

                int skipCount = selectedContentsIndex;
                int takeCount = MaxViewResultCount;

                if (searchMethod == SearchMethodType.Heading)
                {
                    SearchResult sr;
                    try
                    {
                        sr = Workgroup.SearchContents(searchEntry, searchRange, skipCount, takeCount);
                        if (sr != null)
                            result(sr, selectedContentsIndex);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Debug(ex.Message);
                    }
                }
                else if (searchMethod == SearchMethodType.FullText)
                {
                    SearchResult sr;
                    try
                    {
                        sr = Workgroup.SearchFullText(searchEntry, searchRange, skipCount, takeCount);
                        if (sr != null)
                            result(sr, selectedContentsIndex);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Debug(ex.Message);
                    }
                }
            }
        }

        protected async void OnSearchContentsAsync(string searchEntry, SearchRangeType searchRange, SearchMethodType searchMethod, int selectedContentsIndex, Action<SearchResult, int> result)
        {
            var cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;
            lock (_searchLockObject)
            {
                foreach (var job in _searchJobs)
                    job.Cancel();

                _searchJobs.Clear();
                _searchJobs.Add(cts);
            }

            int skipCount = selectedContentsIndex;
            int takeCount = MaxViewResultCount;

            if (searchMethod == SearchMethodType.Heading)
            {
                SearchResult sr;
                try
                {
                    sr = await Workgroup.SearchContentsAsync(searchEntry, searchRange, skipCount, takeCount, token);
                    if (sr != null)
                        result(sr, selectedContentsIndex);
                }
                catch (InvalidOperationException) { }
                catch (Exception ex)
                {
                    Log.Logger.Debug(ex.Message);
                }
            }
            else if (searchMethod == SearchMethodType.FullText)
            {
                SearchResult sr;
                try
                {
                    sr = await Workgroup.SearchFullTextAsync(searchEntry, searchRange, skipCount, takeCount, token);
                    if (sr != null)
                        result(sr, selectedContentsIndex);
                }
                catch (InvalidOperationException) { }
                catch (Exception ex)
                {
                    Log.Logger.Debug(ex.Message);
                }
            }

            lock (_searchLockObject)
            {
                if (_searchJobs.Contains(cts))
                    _searchJobs.Remove(cts);
            }
        }
        #endregion

        protected void OnCreateText(string newName, string newText, Action<TextManageResult> result)
        {
            var mr = Workgroup.CreateText(newName, newText);
            if (mr != null)
                result(mr);
        }

        public bool CanCreateText(string newName, string newText)
        {
            List<string> errors;
            var result = Workgroup.ValidateCreateText(newName, newText, out errors);
            EditingErrors = errors;
            return result;
        }

        protected void OnEditText(Content content, string newText, Action<TextManageResult> result)
        {
            var mr = Workgroup.EditText(content, newText);
            if (mr != null)
                result(mr);
        }

        public bool CanEditText(Content content, string newText)
        {
            List<string> errors;
            var result = Workgroup.ValidateEditText(content, newText, out errors);
            EditingErrors = errors;
            return result;
        }

        protected void OnRenameText(Content content, string newName, Action<TextManageResult> result)
        {
            var mr = Workgroup.RenameText(content, newName);
            if (mr != null)
                result(mr);
        }

        public bool CanRenameText(Content content, string newName)
        {
            List<string> errors;
            var result = Workgroup.ValidateRenameText(content, newName, out errors);
            EditingErrors = errors;
            return result;
        }

        protected void OnDeleteText(Content content, Action<TextManageResult> result)
        {
            var mr = Workgroup.DeleteText(content);
            if (mr != null)
                result(mr);
        }

        public bool CanDeleteText(Content content)
        {
            List<string> errors;
            var result = Workgroup.ValidateDeleteText(content, out errors);
            EditingErrors = errors;
            return result;
        }

        [Reactive] public Workgroup Workgroup { get; set; }

        public Func<Task> ActivateHandler { get; }
        public ReactiveCommand<Unit, Unit> Activate { get; }
        public Action SearchHandler { get; }
        public ReactiveCommand<Unit, Unit> Search { get; }
        public ReactiveCommand<Unit, Unit> PageNext { get; }
        public ReactiveCommand<Unit, Unit> PagePrev { get; }
        public Action OpenTextHandler { get; }
        public ReactiveCommand<Unit, Unit> OpenText { get; }
        public Action CreateTextHandler { get; }
        public ReactiveCommand<Unit, Unit> CreateText { get; }
        public Action EditTextHandler { get; }
        public ReactiveCommand<Unit, Unit> EditText { get; }
        public Action RenameTextHandler { get; }
        public ReactiveCommand<Unit, Unit> RenameText { get; }
        public Action DeleteTextHandler { get; }
        public ReactiveCommand<Unit, Unit> DeleteText { get; }

        readonly ReadOnlyObservableCollection<string> _noteNames;
        [IgnoreDataMember] public ReadOnlyObservableCollection<string> NoteNames => _noteNames;

        [Reactive, DataMember] public List<Content> Contents { get; set; }

        readonly ObservableAsPropertyHelper<int> _selectedNoteIndex;
        [IgnoreDataMember] public int SelectedNoteIndex => _selectedNoteIndex.Value;

        [Reactive, DataMember] public List<string> ContentViewItems { get; set; } = new List<string>();

        readonly ObservableAsPropertyHelper<int> _selectedContentsIndex;
        [IgnoreDataMember] public int SelectedContentsIndex => _selectedContentsIndex.Value;

        [Reactive, DataMember] public (int, int) ContentsViewPageIndex { get; set; }

        [Reactive, DataMember] public int ContentsCount { get; set; }

        [Reactive] public Content OpenedContent { get; set; }

        [Reactive, DataMember] public string EditingTitle { get; set; } = string.Empty;

        [Reactive, DataMember] public string EditingText { get; set; } = string.Empty;

        [Reactive, DataMember] public List<string> EditingErrors { get; set; } = new List<string>();

        [Reactive, DataMember] public string EditingUpdateTime { get; set; } = string.Empty;

        [Reactive, DataMember] public string EditingNoteTitle { get; set; } = string.Empty;

        [Reactive, DataMember] public TextManageType EditingState { get; set; }

        [Reactive, DataMember] public SearchRangeType SearchRange { get; set; }
        readonly ObservableAsPropertyHelper<string> _searchRangeString;
        [IgnoreDataMember] public string SearchRangeString => _searchRangeString.Value;

        [Reactive, DataMember] public SearchMethodType SearchMethod { get; set; }
        readonly ObservableAsPropertyHelper<string> _searchMethodString;
        [IgnoreDataMember] public string SearchMethodString => _searchMethodString.Value;

        [Reactive, DataMember] public string SearchEntry { get; set; } = string.Empty;

        [Reactive, DataMember] public int MaxViewResultCount { get; set; } = 1000;

        [Reactive, DataMember] public string PlaceHolder { get; set; } = string.Empty;

        [Reactive, DataMember] public string SearchNotice { get; set; } = string.Empty;

        [Reactive, DataMember] public string ManageNotice { get; set; } = string.Empty;
    }
}
