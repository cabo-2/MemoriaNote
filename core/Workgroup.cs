using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;

using System.Collections.ObjectModel;
using System.IO;
using ReactiveUI.Fody.Helpers;
using DynamicData;
using DynamicData.Binding;

namespace MemoriaNote
{
    public class Workgroup : ReactiveObject, IWorkgroup
    {
        public Workgroup()
        {
            _notes = new ObservableCollectionExtended<Note>();   
            _notes.CollectionChanged += (sender,e) => { this.RaisePropertyChanged(nameof(SelectedNoteIndex)); };                      
        }

        protected Note _selectedNote;
        protected ObservableCollectionExtended<Note> _notes;

        #region SearchContents
        public SearchResult SearchContents(string searchEntry, SearchRangeType searchRange = SearchRangeType.Note)
        {
            return SearchContents(searchEntry, 0, int.MaxValue, searchRange);
        }
        public SearchResult SearchContents(string searchEntry, int skipCount, int takeCount, SearchRangeType searchRange = SearchRangeType.Note)
        {
            if (searchRange == SearchRangeType.Note)
                return SearchNoteContents(searchEntry, skipCount, takeCount);
            else
                return SearchWorkgroupContents(searchEntry, skipCount, takeCount);
        }

        public SearchResult SearchNoteContents(string searchEntry, int skipCount, int takeCount)
        {
            var currentNote = this.SelectedNote;
            if (currentNote == null)
                return SearchResult.Empty;
            else
                return currentNote.SearchContents(searchEntry, skipCount, takeCount);
        }

        public SearchResult SearchWorkgroupContents(string searchEntry, int skipCount, int takeCount)
        {
            DateTime startTime = DateTime.UtcNow;
            var tables = CreateContentsCountTable(searchEntry);
            IEnumerable<Content> contents = new List<Content>();
            foreach (var note in Notes)
            {
                int count = tables[note.DataSource];
                if (skipCount < count)
                {
                    var s = note.SearchContents(searchEntry, skipCount, takeCount);
                    contents = contents.Concat(s.Contents);

                    int newSkipCount, newTakeCount;
                    if (NeedMoreQuery(count, skipCount, takeCount, out newSkipCount, out newTakeCount))
                    {
                        skipCount = newSkipCount;
                        takeCount = newTakeCount;
                    }
                    else
                        break;
                }
                else
                {
                    skipCount -= count; //read skip
                }
            }
            var sr = new SearchResult();
            sr.Contents = contents.ToList();
            sr.Count = tables.Select(kv => kv.Value).Sum();
            sr.StartTime = startTime;
            sr.EndTime = DateTime.UtcNow;
            return sr;
        }
        Dictionary<string, int> CreateContentsCountTable(string searchEntry)
        {
            Dictionary<string, int> tables = new Dictionary<string, int>();
            foreach (var n in Notes)
                tables.Add(n.DataSource, 0);

            TextMatching textMatch = TextMatching.Create(searchEntry);
            foreach (var dataSource in tables.Select(t => t.Key).ToList().AsParallel())
            {
                using (NoteDbContext db = new NoteDbContext(dataSource))
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine("SELECT * FROM Contents ");
                    builder.AppendLine(textMatch.GetWhereClause("Name"));
                    int count = db.Contents.FromSqlRaw(builder.ToString()).Count();
                    tables[dataSource] = count;
                }
            }
            return tables;
        }
        #endregion

        #region SearchContentsAsync
        public Task<SearchResult> SearchContentsAsync(string searchEntry, SearchRangeType searchRange, CancellationToken token)
        {
            return SearchContentsAsync(searchEntry, searchRange, 0, int.MaxValue, token);
        }

        public Task<SearchResult> SearchContentsAsync(string searchEntry, SearchRangeType searchRange, int skipCount, int takeCount, CancellationToken token)
        {
            if (searchRange == SearchRangeType.Note)
                return SearchNoteContentsAsync(searchEntry, skipCount, takeCount, token);
            else
                return SearchWorkgroupContentsAsync(searchEntry, skipCount, takeCount, token);
        }

        public Task<SearchResult> SearchNoteContentsAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var currentNote = this.SelectedNote;
            if (currentNote == null)
                return Task.Run(() => { return SearchResult.Empty; });
            else
                return currentNote.SearchContentsAsync(searchEntry, skipCount, takeCount, token);
        }

        public Task<SearchResult> SearchWorkgroupContentsAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var task = Task.Run(async () =>
            {
                DateTime startTime = DateTime.UtcNow;
                var tables = CreateContentsCountTable(searchEntry, token);
                IEnumerable<Content> contents = new List<Content>();
                foreach (var note in Notes)
                {
                    int count = tables[note.DataSource];
                    if (skipCount < count)
                    {
                        var s = await note.SearchContentsAsync(searchEntry, skipCount, takeCount, token);
                        contents = contents.Concat(s.Contents);

                        int newSkipCount, newTakeCount;
                        if (NeedMoreQuery(count, skipCount, takeCount, out newSkipCount, out newTakeCount))
                        {
                            skipCount = newSkipCount;
                            takeCount = newTakeCount;
                        }
                        else
                            break;
                    }
                    else
                    {
                        skipCount -= count; //read skip
                    }
                    CancelIfRequested(token);
                }
                var sr = new SearchResult();
                sr.Contents = contents.ToList();
                sr.Count = tables.Select(kv => kv.Value).Sum();
                sr.StartTime = startTime;
                sr.EndTime = DateTime.UtcNow;
                return sr;
            }, token);
            return task;
        }
        Dictionary<string, int> CreateContentsCountTable(string searchEntry, CancellationToken token)
        {
            Dictionary<string, int> tables = new Dictionary<string, int>();
            foreach (var n in Notes)
                tables.Add(n.DataSource, 0);

            TextMatching textMatch = TextMatching.Create(searchEntry);
            foreach (var dataSource in tables.Select(t => t.Key).ToList().AsParallel())
            {
                using (NoteDbContext db = new NoteDbContext(dataSource))
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine("SELECT * FROM Contents ");
                    builder.AppendLine(textMatch.GetWhereClause("Name"));
                    int count = db.Contents.FromSqlRaw(builder.ToString()).Count();
                    tables[dataSource] = count;
                }
                CancelIfRequested(token);
            }
            return tables;
        }
        #endregion

        #region SearchFullText
        public SearchResult SearchFullText(string searchEntry, SearchRangeType searchRange)
        {
            return SearchFullText(searchEntry, searchRange, 0, int.MaxValue);
        }
        public SearchResult SearchFullText(string searchEntry, SearchRangeType searchRange, int skipCount, int takeCount)
        {
            if (searchRange == SearchRangeType.Note)
                return SearchNoteFullText(searchEntry, skipCount, takeCount);
            else
                return SearchWorkgroupFullText(searchEntry, skipCount, takeCount);
        }
        public SearchResult SearchNoteFullText(string searchEntry, int skipCount, int takeCount)
        {
            var currentNote = this.SelectedNote;
            if (currentNote == null)
                return SearchResult.Empty;
            else
                return currentNote.SearchFullText(searchEntry, skipCount, takeCount);
        }
        public SearchResult SearchWorkgroupFullText(string searchEntry, int skipCount, int takeCount)
        {
            return SearchResult.Empty;
        }
        #endregion

        #region SearchFullTextAsync
        public Task<SearchResult> SearchFullTextAsync(string searchEntry, SearchRangeType searchRange, CancellationToken token)
        {
            return SearchFullTextAsync(searchEntry, searchRange, 0, int.MaxValue, token);
        }

        public Task<SearchResult> SearchFullTextAsync(string searchEntry, SearchRangeType searchRange, int skipCount, int takeCount, CancellationToken token)
        {
            if (searchRange == SearchRangeType.Note)
                return SearchNoteFullTextAsync(searchEntry, skipCount, takeCount, token);
            else
                return SearchWorkgroupFullTextAsync(searchEntry, skipCount, takeCount, token);
        }

        public Task<SearchResult> SearchNoteFullTextAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var currentNote = this.SelectedNote;
            if (currentNote == null)
                return Task.Run(() => { return SearchResult.Empty; });
            else
                return currentNote.SearchFullTextAsync(searchEntry, skipCount, takeCount, token);
        }

        public Task<SearchResult> SearchWorkgroupFullTextAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var task = Task.Run( async() =>
            {
                DateTime startTime = DateTime.UtcNow;
                var tables = CreateFullTextCountTable(searchEntry, token);
                IEnumerable<Content> contents = new List<Content>();
                foreach (var note in Notes)
                {
                    int count = tables[note.DataSource];
                    if (skipCount < count)
                    {
                        var s = await note.SearchFullTextAsync(searchEntry, skipCount, takeCount, token);
                        contents = contents.Concat(s.Contents);

                        int newSkipCount, newTakeCount;
                        if (NeedMoreQuery(count, skipCount, takeCount, out newSkipCount, out newTakeCount))
                        {
                            skipCount = newSkipCount;
                            takeCount = newTakeCount;
                        }
                        else
                            break;
                    }
                    else
                    {                        
                        skipCount -= count; //read skip
                    }
                    CancelIfRequested(token);
                }
                var sr = new SearchResult();
                sr.Contents = contents.ToList();
                sr.Count = tables.Select(kv => kv.Value).Sum();
                sr.StartTime = startTime;
                sr.EndTime = DateTime.UtcNow;
                return sr;
            }, token);
            return task;
        }

        Dictionary<string, int> CreateFullTextCountTable(string searchEntry, CancellationToken token)
        {
            Dictionary<string, int> tables = new Dictionary<string, int>();
            foreach(var n in Notes)
                tables.Add(n.DataSource, 0);

            TextMatching textMatch = TextMatching.Create(searchEntry);
            foreach (var dataSource in tables.Select(t => t.Key).ToList().AsParallel())
            {
                using (NoteDbContext db = new NoteDbContext(dataSource))
                {
                    if (!string.IsNullOrWhiteSpace(textMatch.Pattern))
                    {
                        StringBuilder builder = new StringBuilder();
                        builder.AppendLine(
                            "SELECT p.* FROM Pages p JOIN " +
                           $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Text : \"{textMatch.Pattern}\"') f " +
                            "ON p.Rowid = f.rowid "
                        );
                        int count = db.Pages.FromSqlRaw(builder.ToString()).Count();
                        tables[dataSource] = count;
                    }
                    else
                    {
                        int count = db.Contents.Count();
                        tables[dataSource] = count;
                    }
                }
                CancelIfRequested(token);
            }
            return tables;
        }
        #endregion

        static bool CancelIfRequested(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }
            return true;
        }

        bool NeedMoreQuery(int resultCount, int skipCount, int takeCount, out int newSkipCount, out int newTakeCount)
        {
            if (resultCount < skipCount + takeCount)
            {
                newSkipCount = skipCount - resultCount;
                if (newSkipCount < 0)
                {
                    newTakeCount = newSkipCount + takeCount;
                    newSkipCount = 0;
                }
                else
                    newTakeCount = takeCount;

                return true;
            }
            else
            {
                newSkipCount = newTakeCount = 0;
                return false;
            }
        }

        public Page ReadAll(IContent content)
        {
            Note current = this.SelectedNote;
            Page page = current.Read(content.Guid);
            if (page != null)
                return page;
            
            foreach(var note in this.Notes.Where(n => !n.Equals(current)))
            {
                page = note.Read(content.Guid);
                if (page != null)
                    return page;
            }
            return null;
        }

        public bool ValidateCreateText(string testName, string testText, out List<string> errors)
        {
            errors = new List<string>();
            TextUtil.ValidateNameString(testName, errors);            
            if (SelectedNote.Read(testName).FirstOrDefault() != null)
                errors.Add("The text name is already in use.");

            return errors.Count == 0;
        }

        public bool ValidateEditText(IContent content, string testText, out List<string> errors)
        {
            errors = new List<string>();
            if (content == null)
            {
                errors.Add("The text not yet opened.");
                return false;
            }    

            return TextUtil.ValidateTextString(testText, errors);
        }

        public bool ValidateRenameText(IContent content, string testName, out List<string> errors)
        {
            errors = new List<string>();
            if (content == null)
            {
                errors.Add("The text not yet opened.");
                return false;
            } 

            TextUtil.ValidateNameString(testName, errors);            
            if (SelectedNote.Read(testName).FirstOrDefault() != null)
                errors.Add("The text name is already in use.");

            return errors.Count == 0;
        }

        public bool ValidateDeleteText(IContent content, out List<string> errors)
        {
            errors = new List<string>();
            if (content == null)
            {
                errors.Add("The text not yet opened.");
                return false;
            }

            return true;
        }

        public TextManageResult CreateText(string newName, string newText)
        {            
            TextManageResult mr = new TextManageResult() { Operation = TextManageType.Create };
            List<string> errors;
            var validate = ValidateCreateText(newName, newText, out errors);
            mr.Errors = errors;
            if (validate) {
                var page = SelectedNote.Create(newName, newText);
                mr.Content = page.GetContent();             
                mr.Notification = "The text created successfully.";
                mr.Result = true;
            }
            else
            {                
                mr.Notification = "Failed to create the text.";
            }
            return mr;
        }
        
        public TextManageResult EditText(IContent content, string newText)
        {
            TextManageResult mr = new TextManageResult() { Operation = TextManageType.Edit };
            List<string> errors;
            var validate = ValidateEditText(content, newText, out errors);
            mr.Errors = errors;
            if (validate) {
                var page = SelectedNote.Read(content);
                page.Text = newText;
                SelectedNote.Update(page);
                mr.Content = page.GetContent();
                mr.Notification = "The text updated successfully.";
                mr.Result = true;
            }
            else
            {                
                mr.Notification = "Failed to update the text.";
            }
            return mr;
        }

        public TextManageResult RenameText(IContent content, string newName)
        {
            TextManageResult mr = new TextManageResult() { Operation = TextManageType.Rename };
            List<string> errors;
            var validate = ValidateRenameText(content, newName, out errors);
            mr.Errors = errors;
            if (validate) {
                var page = SelectedNote.Read(content);
                page.Name = newName;
                SelectedNote.Update(page);
                mr.Content = page.GetContent();
                mr.Notification = "The text renamed successfully.";
                mr.Result = true;
            }
            else
            {                
                mr.Notification = "Failed to rename the text.";
            }
            return mr;
        }

        public TextManageResult DeleteText(IContent content)
        {
            TextManageResult mr = new TextManageResult() { Operation = TextManageType.Delete };
            List<string> errors;
            var validate = ValidateDeleteText(content, out errors);
            mr.Errors = errors;
            if (validate) {
                SelectedNote.Delete(content);
                mr.Content = null;
                mr.Notification = "The text deleted successfully.";
                mr.Result = true;
            }
            else
            {               
                mr.Content = content.GetContent(); 
                mr.Notification = "Failed to delete the text.";
            }
            return mr;
        }
     
        public ObservableCollectionExtended<Note> Notes => _notes;
        public List<string> UseDataSources => _notes.Select(note => note.DataSource).ToList();

        public Note SelectedNote
        { 
            get => _selectedNote;
            set
            {                
                if (!object.Equals(_selectedNote, value))
                {
                    this.RaiseAndSetIfChanged(ref _selectedNote, value);
                    this.RaisePropertyChanged(nameof(SelectedNoteIndex));
                }         
            }
        }
        public int SelectedNoteIndex
        { 
            get => _notes.IndexOf(_selectedNote);
            set => SelectedNote = _notes[value];
        }
        public string SelectedNoteName => SelectedNote.ToString();

        [Reactive] public string Name { get; set; }
        
        public override string ToString()
        {
            return Name ?? base.ToString();
        }
    }  
}
