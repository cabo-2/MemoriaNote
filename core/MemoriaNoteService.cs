using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Reactive;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace MemoriaNote
{
    public class MemoriaNoteService : ReactiveObject
    {
        public MemoriaNoteService()
        {
            if (!File.Exists(Configuration.Instance.DefaultDataSourcePath)) {
                Note.Create(Configuration.Instance.DefaultNoteName, Configuration.Instance.DefaultNoteTitle, Configuration.Instance.DefaultDataSourcePath);
                Log.Logger.Information("Default note created");
            }
            Workgroup = Configuration.Instance.Workgroup.Build();
        }

        protected virtual void OnActivate()
        {
            DatabaseMigrate();
            Log.Logger.Information("MemoriaNote service activated");
        }

        protected void DatabaseMigrate()
        {
            foreach (var dataSource in Configuration.Instance.DataSources) {
                Note.Migrate(dataSource);                
            }
        }

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
                catch (InvalidOperationException) {}
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
                catch (InvalidOperationException) {}
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

        protected void OnEditText(Content content, string newText, Action<TextManageResult> result)
        {
            var mr = Workgroup.EditText(content, newText);
            if (mr != null)
                result(mr);
        }

        protected void OnRenameText(Content content, string newName, Action<TextManageResult> result)
        {
            var mr = Workgroup.RenameText(content, newName);
            if (mr != null)
                result(mr);
        }

        protected void OnDeleteText(Content content, Action<TextManageResult> result)
        {
            var mr = Workgroup.DeleteText(content);
            if (mr != null)
                result(mr);
        }

        [Reactive] public Workgroup Workgroup { get; set; }
    }
}
