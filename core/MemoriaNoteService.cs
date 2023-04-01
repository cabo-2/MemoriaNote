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

            Activate = ReactiveCommand.CreateFromTask(
                () => Task.Run(() =>
                {
                    OnActivate();
                    OnSearchContents(SearchEntry, SearchRange, SearchMethod, 0, OnSearchResultCallback);
                }
                )
            );

            Search = ReactiveCommand.Create(
                () => OnSearchContents(SearchEntry, SearchRange, SearchMethod, 0, OnSearchResultCallback)
            );

            var canPageNext = this.WhenAnyValue(
                x => x.ContentsViewPageIndex,
                x => x.ContentsCount,
                (pi, count) =>
                    ViewPageIndexToContentsIndex(pi.Item1, pi.Item2) + Configuration.Instance.Search.MaxViewResultCount < count);

            PageNext = ReactiveCommand.Create(
                () => OnSearchContents(SearchEntry, SearchRange, SearchMethod,
                        SelectedContentsIndex + Configuration.Instance.Search.MaxViewResultCount, OnSearchResultCallback),
                canPageNext
            );

            var canPagePrev = this.WhenAnyValue(
                x => x.ContentsViewPageIndex,
                x => x.ContentsCount,
                (pi, count) =>
                    0 <= ViewPageIndexToContentsIndex(pi.Item1, pi.Item2) - Configuration.Instance.Search.MaxViewResultCount);

            PagePrev = ReactiveCommand.Create(
                () => OnSearchContents(SearchEntry, SearchRange, SearchMethod,
                        SelectedContentsIndex - Configuration.Instance.Search.MaxViewResultCount, OnSearchResultCallback),
                canPagePrev
            );

            OpenText = ReactiveCommand.Create(
                () => OnSelectedContextsIndexChanged()
            );

            CreateText = ReactiveCommand.Create(
                () => OnCreateText(EditingTitle.ToString(), EditingText.ToString(), OnTextManageResultCallback)
            );

            EditText = ReactiveCommand.Create(
                () => OnEditText(OpenedPage?.GetContent(), EditingText.ToString(), OnTextManageResultCallback)
            );

            RenameText = ReactiveCommand.Create(
                () => OnRenameText(OpenedPage?.GetContent(), EditingTitle.ToString(), OnTextManageResultCallback)
            );

            DeleteText = ReactiveCommand.Create(
                () => OnDeleteText(OpenedPage?.GetContent(), OnTextManageResultCallback)
            );

            Workgroup.Notes.ToObservableChangeSet()
                .Transform(note => note.ToString())
                .Bind(out _noteNames)
                .Subscribe();

            _selectedNoteIndex = this
                .WhenAnyValue(x => x.Workgroup.SelectedNoteIndex)
                .ToProperty(this, x => x.SelectedNoteIndex);

            _selectedContentsIndex = this
                .WhenAnyValue(x => x.ContentsViewPageIndex)
                .Select(x => ViewPageIndexToContentsIndex(x.Item1, x.Item2))
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
            this.ContentsViewPageIndex = (ContentsIndexToViewPage(newContentsIndex), ContentsIndexToViewIndex(newContentsIndex));
            this.ContentsCount = result.Count;
            this.Contents = result.Contents;
            var newContentItems = result.Contents.ConvertAll(c => c.ToString());
            this.ContentViewItems.Clear();
            this.ContentViewItems.Add(newContentItems);
            this.Notification = result.ToString();
            OnSelectedContextsIndexChanged();
            Log.Logger.Information(result.ToString());
        }

        protected void OnTextManageResultCallback(TextManageResult result)
        {
            this.Notification = result.Notification;
            Log.Logger.Information(result.ToString());
        }

        protected void OnSelectedContextsIndexChanged()
        {
            if (0 < this.Contents.Count)
            {
                this.PlaceHolder = PlaceHolderString(this.SelectedContentsIndex, this.ContentsCount);
                this.OpenedPage = Workgroup.SelectedNote.Read(this.Contents[this.ContentsViewPageIndex.Item2]);
                this.EditingTitle = this.OpenedPage.Title;
                this.TextEditor = this.OpenedPage.Text;
            }
            else
            {
                this.PlaceHolder = PlaceHolderString(0, 0);
                this.OpenedPage = null;
                this.EditingTitle = string.Empty;
                this.TextEditor = string.Empty;
            }
        }

        static string PlaceHolderString(int currentIndex, int totalCount) => totalCount > 0 ? $"{currentIndex + 1} of {totalCount}" : "0 of 0";

        static int ContentsIndexToViewIndex(int contentsIndex) => contentsIndex % Configuration.Instance.Search.MaxViewResultCount; // % is remainder
        static int ContentsIndexToViewPage(int contentsIndex) => (int)(contentsIndex / Configuration.Instance.Search.MaxViewResultCount);
        static int ViewPageIndexToContentsIndex(int page, int index) => (page * Configuration.Instance.Search.MaxViewResultCount) + index;

        #region SearchContents
        object _searchLockObject = new object();
        List<CancellationTokenSource> _searchJobs = new List<CancellationTokenSource>();

        protected async void OnSearchContents(string searchEntry, SearchRangeType searchRange, SearchMethodType searchMethod, int selectedContentsIndex, Action<SearchResult, int> result)
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
            int takeCount = Configuration.Instance.Search.MaxViewResultCount;

            if (searchMethod == SearchMethodType.Headline)
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

        public ReactiveCommand<Unit, Unit> Activate { get; }
        public ReactiveCommand<Unit, Unit> Search { get; }
        public ReactiveCommand<Unit, Unit> PageNext { get; }
        public ReactiveCommand<Unit, Unit> PagePrev { get; }
        public ReactiveCommand<Unit, Unit> OpenText { get; }
        public ReactiveCommand<Unit, Unit> CreateText { get; }
        public ReactiveCommand<Unit, Unit> EditText { get; }
        public ReactiveCommand<Unit, Unit> RenameText { get; }
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

        [Reactive] public Page OpenedPage { get; set; }

        [Reactive, DataMember] public string EditingTitle { get; set; } = string.Empty;

        [Reactive, DataMember] public string TextEditor { get; set; } = string.Empty;

        [Reactive, DataMember] public string EditingText { get; set; } = string.Empty;

        [Reactive, DataMember] public List<string> EditingErrors { get; set; } = new List<string>();

        [Reactive, DataMember] public TextManageType EditingState { get; set; }

        [Reactive, DataMember] public SearchRangeType SearchRange { get; set; }
        readonly ObservableAsPropertyHelper<string> _searchRangeString;
        [IgnoreDataMember] public string SearchRangeString => _searchRangeString.Value;

        [Reactive, DataMember] public SearchMethodType SearchMethod { get; set; }
        readonly ObservableAsPropertyHelper<string> _searchMethodString;
        [IgnoreDataMember] public string SearchMethodString => _searchMethodString.Value;

        [Reactive, DataMember] public string SearchEntry { get; set; } = string.Empty;

        //[Reactive] public List<string> SearchHistory { get; set; } = new List<string>();

        // public bool IsWritable
        // {
        //     get
        //     {
        //         var noteReadOnly = Workgroup?.SelectedNote?.TitlePage?.ReadOnly;
        //         if (noteReadOnly != null && !noteReadOnly.Value)
        //             return true;
        //         else
        //             return false;
        //     }
        // }

        [Reactive, DataMember] public string PlaceHolder { get; set; } = string.Empty;

        [Reactive, DataMember] public string Notification { get; set; } = string.Empty;
    }
}
