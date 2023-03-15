using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using ReactiveUI;

namespace MemoriaNote
{
    public class MemoriaNoteViewModel : ReactiveObject
    {
        public MemoriaNoteViewModel () {}

        #region SearchContents
        object _searchLockObject = new object();
        List<CancellationTokenSource> _searchJobs = new List<CancellationTokenSource>();

        public async void SearchContents()
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

            int skipCount = 0;
            int takeCount = Configuration.Instance.Search.MaxViewResultCount;
            var books = Archive.CurrentBooks;

            if (SearchMethodIndex == (int)SearchMethodType.Default)
            {
                SearchResult sr = null;
                try
                {
                    sr = await books.SearchContentsAsync(SearchEntry, skipCount, takeCount, token);
                    if (sr != null)
                    {
                        SearchResult = sr;
                        SelectedContentIndex = 0;
                        Contents = sr.Contents.ToList();
                        SelectedContent = Contents.FirstOrDefault();
                        Notification = sr.ToString();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            else if (SearchMethodIndex == (int)SearchMethodType.FTS)
            {
                SearchResult sr = null;
                try
                {
                    sr = await Archive.CurrentBooks.SearchFullTextAsync(SearchEntry, skipCount, takeCount, token);
                    if (sr != null)
                    {
                        SearchResult = sr;
                        SelectedContentIndex = 0;
                        Contents = sr.Contents.ToList();
                        SelectedContent = Contents.FirstOrDefault();
                        Notification = sr.ToString();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            lock (_searchLockObject)
            {
                if (_searchJobs.Contains(cts))
                    _searchJobs.Remove(cts);
            }
        }
        
        public async void UpdateViewContents()
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

            int skipCount = SelectedContentIndex;
            int takeCount = Configuration.Instance.Search.MaxViewResultCount;
            var books = Archive.CurrentBooks;

            if (SearchMethodIndex == (int)SearchMethodType.Default)
            {
                SearchResult sr = null;
                try
                {
                    sr = await books.SearchContentsAsync(SearchEntry, skipCount, takeCount, token);
                    if (sr != null)
                    {
                        SearchResult = sr;
                        Contents = sr.Contents.ToList();
                        SelectedContent = Contents.FirstOrDefault();
                        Notification = sr.ToString();
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            else if (SearchMethodIndex == (int)SearchMethodType.FTS)
            {
                SearchResult sr = null;
                try
                {
                    sr = await Archive.CurrentBooks.SearchFullTextAsync(SearchEntry, skipCount, takeCount, token);
                    if (sr != null)
                    {
                        SearchResult = sr;
                        Contents = sr.Contents.ToList();
                        SelectedContent = Contents.FirstOrDefault();
                        Notification = sr.ToString();
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            lock (_searchLockObject)
            {
                if (_searchJobs.Contains(cts))
                    _searchJobs.Remove(cts);
            }
        }
        #endregion

        public ITextEditor TextEditor { get; set; }

        ArchiveManager _archive = new ArchiveManager();
        public ArchiveManager Archive
        {
            get => _archive;
            set => this.RaiseAndSetIfChanged(ref _archive, value);
        }

        List<Content> _contents = null;
        public List<Content> Contents
        {
            get => _contents;
            set => this.RaiseAndSetIfChanged(ref _contents, value);
        }

        Content _selectedContent = null;
        public Content SelectedContent
        {
            get => _selectedContent;
            set => this.RaiseAndSetIfChanged(ref _selectedContent, value);
        }

        Page _openedPage = null;
        public Page OpenedPage
        {
            get => _openedPage;
            set => this.RaiseAndSetIfChanged(ref _openedPage, value);
        }

        string _editingTitle = null;
        public string EditingTitle
        {
            get => _editingTitle;
            set => this.RaiseAndSetIfChanged(ref _editingTitle, value);
        }

        SearchRangeType _searchRangeType;
        public SearchRangeType SearchRange
        {
            get => _searchRangeType;
            set => this.RaiseAndSetIfChanged(ref _searchRangeType, value);
        }

        string _searchEntry = "";
        public string SearchEntry
        {
            get => _searchEntry;
            set => this.RaiseAndSetIfChanged(ref _searchEntry, value);
        }

        int _searchMethodIndex;
        public int SearchMethodIndex
        {
            get => _searchMethodIndex;
            set => this.RaiseAndSetIfChanged(ref _searchMethodIndex, value);
        }

        SearchResult _searchResult;
        public SearchResult SearchResult
        {
            get => _searchResult;
            set => this.RaiseAndSetIfChanged(ref _searchResult, value);
        }

        int _selectedContentIndex;
        public int SelectedContentIndex
        {
            get => _selectedContentIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedContentIndex, value);
        }

        ObservableCollection<string> _searchHistory = new ObservableCollection<string> ();
        public ObservableCollection<string> SearchHistory
        {
            get => _searchHistory;
            set => this.RaiseAndSetIfChanged(ref _searchHistory, value);
        }

        public bool IsWritable
        {
            get
            {
                var noteReadOnly = Archive?.CurrentBooks?.CurrentNote?.TitlePage?.ReadOnly;
                if (noteReadOnly != null && !noteReadOnly.Value)
                    return true;
                else
                    return false;
            }
        }

        string _notification;
        public string Notification
        {
            get => _notification;
            set => this.RaiseAndSetIfChanged(ref _notification, value);
        }
    }
}
