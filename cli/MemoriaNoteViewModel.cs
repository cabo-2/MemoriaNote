using System;
using System.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using NStack;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using DynamicData;
using DynamicData.Binding;

namespace MemoriaNote.Cli
{

    [DataContract]
    public class MemoriaNoteViewModel : MemoriaNoteService
    {
        public MemoriaNoteViewModel() : base()
        {
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

            Workgroup.Notes.ToObservableChangeSet()
                .Transform(note => ustring.Make(note.ToString()))
                .Bind(out _notes)
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

        protected void OnSearchResultCallback(SearchResult result, int newContentsIndex)
        {
            this.ContentsViewPageIndex = (ContentsIndexToViewPage(newContentsIndex), ContentsIndexToViewIndex(newContentsIndex));
            this.ContentsCount = result.Count;
            this.Contents = result.Contents;
            var newContentItems = result.Contents.ConvertAll(c => ustring.Make(c.ToString()));
            this.ContentViewItems.Clear();
            this.ContentViewItems.Add(newContentItems);
            this.Notification = ustring.Make(result.ToString());
            OnSelectedContextsIndexChanged();
            Log.Logger.Information(result.ToString());
        }

        protected void OnSelectedContextsIndexChanged()
        {
            if (0 < this.Contents.Count)
            {
                this.PlaceHolder = ustring.Make(PlaceHolderString(this.SelectedContentsIndex, this.ContentsCount));
                this.OpenedPage = Workgroup.SelectedNote.Read(this.Contents[this.ContentsViewPageIndex.Item2]);
                this.EditingTitle = ustring.Make(this.OpenedPage.Title);
                this.TextEditor = ustring.Make(this.OpenedPage.Text);
            }
            else
            {
                this.PlaceHolder = ustring.Make(PlaceHolderString(0, 0));
                this.OpenedPage = null;
                this.EditingTitle = ustring.Empty;
                this.TextEditor = ustring.Empty;
            }
        }

        static string PlaceHolderString(int currentIndex, int totalCount) => totalCount > 0 ? $"{currentIndex+1} of {totalCount}" : "0 of 0";
        
        static int ContentsIndexToViewIndex(int contentsIndex) => contentsIndex % Configuration.Instance.Search.MaxViewResultCount; // % is remainder
        static int ContentsIndexToViewPage(int contentsIndex) => (int)(contentsIndex / Configuration.Instance.Search.MaxViewResultCount);
        static int ViewPageIndexToContentsIndex(int page, int index) => (page * Configuration.Instance.Search.MaxViewResultCount) + index;
     
        public ReactiveCommand<Unit, Unit> Activate { get; }

        public ReactiveCommand<Unit, Unit> Search { get; }

        public ReactiveCommand<Unit, Unit> PageNext { get; }

        public ReactiveCommand<Unit, Unit> PagePrev { get; }

        public ReactiveCommand<Unit, Unit> OpenText { get; }

        public ReactiveCommand<Unit, Unit> Edit { get; }

        readonly ReadOnlyObservableCollection<ustring> _notes;
        [IgnoreDataMember] public ReadOnlyObservableCollection<ustring> Notes => _notes;

        [Reactive, DataMember] public List<Content> Contents { get; set; }
        readonly ObservableAsPropertyHelper<int> _selectedNoteIndex;
        [IgnoreDataMember] public int SelectedNoteIndex => _selectedNoteIndex.Value;

        [Reactive, DataMember] public List<ustring> ContentViewItems { get; set; } = new List<ustring>();

        readonly ObservableAsPropertyHelper<int> _selectedContentsIndex;
        [IgnoreDataMember] public int SelectedContentsIndex => _selectedContentsIndex.Value;

        [Reactive, DataMember] public (int,int) ContentsViewPageIndex { get; set; }

        [Reactive, DataMember] public int ContentsCount { get; set; }

        [Reactive] public Page OpenedPage { get; set; }

        [Reactive, DataMember] public ustring EditingTitle { get; set; } = "";

        [Reactive, DataMember] public ustring TextEditor { get; set; } = "";

        [Reactive, DataMember] public ustring EditingText { get; set; }

        [Reactive, DataMember] public EditingState EditingState { get; set; }

        [Reactive, DataMember] public SearchRangeType SearchRange { get; set; }
        readonly ObservableAsPropertyHelper<string> _searchRangeString;
        [IgnoreDataMember] public string SearchRangeString => _searchRangeString.Value;

        [Reactive, DataMember] public SearchMethodType SearchMethod { get; set; }
        readonly ObservableAsPropertyHelper<string> _searchMethodString;
        [IgnoreDataMember] public string SearchMethodString => _searchMethodString.Value;

        [Reactive, DataMember] public string SearchEntry { get; set; } = "";

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

        [Reactive, DataMember] public ustring PlaceHolder { get; set; }

        [Reactive, DataMember] public ustring Notification { get; set; }
    }
}