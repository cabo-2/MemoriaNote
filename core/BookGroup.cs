using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;

namespace MemoriaNote
{
    public class BookGroup : ReactiveObject, IBookGroup
    {
        public BookGroup()
        {
            _items = new List<BookGroupItem>();
        }

        protected Note _current;
        protected IList<BookGroupItem> _items;
        protected string _name;
        protected SearchRangeType _searchRange;
        protected DateTime _lastSearchTime;
        protected bool _isAutoEnabled;

        public void Add(BookGroupItem item) {
            _items.Add(item);
            if (_current == null)
                CurrentNote = item.Note;
        }

        #region SearchContents
        public SearchResult SearchContents(string searchEntry)
        {
            return SearchContents(searchEntry, 0, int.MaxValue);
        }
        public SearchResult SearchContents(string searchEntry, int skipCount, int takeCount)
        {
            if (SearchRange == SearchRangeType.Single)
                return SearchSingleContents(searchEntry, skipCount, takeCount);
            else
                return SearchAllContents(searchEntry, skipCount, takeCount);
        }

        public SearchResult SearchSingleContents(string searchEntry, int skipCount, int takeCount)
        {
            var currentNote = this.CurrentNote;
            if (currentNote == null)
                return SearchResult.Empty;
            else
                return currentNote.SearchContents(searchEntry, skipCount, takeCount);
        }

        public SearchResult SearchAllContents(string searchEntry, int skipCount, int takeCount)
        {
            DateTime startTime = DateTime.UtcNow;
            var tables = CreateContentsCountTable(searchEntry);
            IEnumerable<Content> contents = new List<Content>();
            foreach (var note in EnabledNotes)
            {
                int count = tables[note.DataSource];
                if (skipCount < count)
                {
                    var s = note.SearchContents(searchEntry, skipCount, takeCount);
                    contents = contents.Concat(s.Contents);

                    int newSkipCount, newTakeCount;
                    if (IsNeedReadNext(count, skipCount, takeCount, out newSkipCount, out newTakeCount))
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
            foreach (var n in EnabledNotes)
                tables.Add(n.DataSource, 0);

            TextMatching textMatch = TextMatching.Create(searchEntry);
            foreach (var dataSource in tables.Select(t => t.Key).ToList().AsParallel())
            {
                using (NoteDbContext db = new NoteDbContext(dataSource))
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine("SELECT * FROM Contents ");
                    builder.AppendLine(textMatch.GetWhereClause("Title"));
                    int count = db.Contents.FromSqlRaw(builder.ToString()).Count();
                    tables[dataSource] = count;
                }
            }
            return tables;
        }

        [Obsolete]
        public SearchResult SearchAllContentsObsolete(string searchEntry, int skipCount, int takeCount)
        {
            var dataSources = EnabledNotes.Select(n => n.DataSource).ToList();
            if (dataSources.Count == 0)
            {
                return SearchResult.Empty;
            }
            var notes = new Dictionary<string, Note>();
            foreach (var note in EnabledNotes)
            {
                var id = note.TitlePage.Noteid;
                notes.Add(id, note);
            }

            using (NoteDbContext db = new NoteDbContext(dataSources[0]))
            {
                using (var conn = db.Database.GetDbConnection())
                {
                    DateTime startTime = DateTime.UtcNow;
                    List<Content> contents;
                    int count;
                    try
                    {
                        conn.Open();
                        for (int i = 1; i < dataSources.Count; i++)
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = string.Format("ATTACH [{0}] AS db{1};", dataSources[i], i);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        StringBuilder builder = new StringBuilder();
                        TextMatching textMatch;
                        try
                        {
                            textMatch = TextMatching.Create(searchEntry);
                            builder.AppendLine("SELECT * FROM main.Contents ");
                            builder.AppendLine(textMatch.GetWhereClause("Title"));
                            for (int i = 1; i < dataSources.Count; i++)
                            {
                                builder.AppendLine("UNION ALL ");
                                builder.AppendLine(string.Format("SELECT * FROM db{0}.Contents ", i));
                                builder.AppendLine(textMatch.GetWhereClause("Title"));
                            }

                            count = db.Contents.FromSqlRaw(builder.ToString()).Count();
                            contents = db.Contents.FromSqlRaw(builder.ToString())
                                           .OrderBy(c => c.Title)
                                           .ThenBy(c => c.Index)
                                           .Skip(skipCount)
                                           .Take(takeCount)
                                           .ToList();
                            contents.ForEach(c => c.Parent = notes[c.Noteid]);
                        }
                        catch (ArgumentException)
                        {
                            count = 0;
                            contents = new List<Content>();
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        Console.WriteLine(e.StackTrace);
                        return null;
                    }
                    return new SearchResult()
                    {
                        Contents = contents,
                        StartTime = startTime,
                        EndTime = DateTime.UtcNow,
                        Count = count
                    };
                }
            }
        }
        #endregion

        #region SearchContentsAsync
        public Task<SearchResult> SearchContentsAsync(string searchEntry, CancellationToken token)
        {
            return SearchContentsAsync(searchEntry, 0, int.MaxValue, token);
        }

        public Task<SearchResult> SearchContentsAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            if (SearchRange == SearchRangeType.Single)
                return SearchSingleContentsAsync(searchEntry, skipCount, takeCount, token);
            else
                return SearchAllContentsAsync(searchEntry, skipCount, takeCount, token);
        }

        public Task<SearchResult> SearchSingleContentsAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var currentNote = this.CurrentNote;
            if (currentNote == null)
                return Task.Run(() => { return SearchResult.Empty; });
            else
                return currentNote.SearchContentsAsync(searchEntry, skipCount, takeCount, token);
        }

        public Task<SearchResult> SearchAllContentsAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var task = Task.Run(async () =>
            {
                DateTime startTime = DateTime.UtcNow;
                var tables = CreateContentsCountTable(searchEntry, token);
                IEnumerable<Content> contents = new List<Content>();
                foreach (var note in EnabledNotes)
                {
                    int count = tables[note.DataSource];
                    if (skipCount < count)
                    {
                        var s = await note.SearchContentsAsync(searchEntry, skipCount, takeCount, token);
                        contents = contents.Concat(s.Contents);

                        int newSkipCount, newTakeCount;
                        if (IsNeedReadNext(count, skipCount, takeCount, out newSkipCount, out newTakeCount))
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
            foreach (var n in EnabledNotes)
                tables.Add(n.DataSource, 0);

            TextMatching textMatch = TextMatching.Create(searchEntry);
            foreach (var dataSource in tables.Select(t => t.Key).ToList().AsParallel())
            {
                using (NoteDbContext db = new NoteDbContext(dataSource))
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine("SELECT * FROM Contents ");
                    builder.AppendLine(textMatch.GetWhereClause("Title"));
                    int count = db.Contents.FromSqlRaw(builder.ToString()).Count();
                    tables[dataSource] = count;
                }
                CancelIfRequested(token);
            }
            return tables;
        }
        [Obsolete]
        public Task<SearchResult> SearchAllContentsObsoleteAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            if (EnabledNotes.Count > 10)
                throw new InvalidOperationException("limit 10");

            var task = Task.Run(async() =>
            {
                DateTime startTime = DateTime.UtcNow;
                await Task.Yield(); //dummy

                var dataSources = EnabledNotes.Select(n => n.DataSource).ToList();
                if (dataSources.Count == 0)
                {
                    return SearchResult.Empty;
                }
                var notes = new Dictionary<string, Note>();
                foreach (var note in EnabledNotes)
                {
                    var id = note.TitlePage.Noteid;
                    notes.Add(id, note);
                }

                using (NoteDbContext db = new NoteDbContext(dataSources[0]))
                {
                    using (var conn = db.Database.GetDbConnection())
                    {                        
                        List<Content> contents;
                        int count;

                        conn.Open();
                        for (int i = 1; i < dataSources.Count; i++)
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = $"ATTACH [{dataSources[i]}] AS db{i};";
                                cmd.ExecuteNonQuery();
                            }
                        }

                        StringBuilder builder = new StringBuilder();
                        TextMatching textMatch = TextMatching.Create(searchEntry);
                        builder.AppendLine("SELECT * FROM main.Contents ");
                        builder.AppendLine(textMatch.GetWhereClause("Title"));
                        for (int i = 1; i < dataSources.Count; i++)
                        {
                            builder.AppendLine("UNION ALL ");
                            builder.AppendLine(string.Format("SELECT * FROM db{0}.Contents ", i));
                            builder.AppendLine(textMatch.GetWhereClause("Title"));
                        }

                        count = db.Contents.FromSqlRaw(builder.ToString()).Count();
                        contents = db.Contents.FromSqlRaw(builder.ToString())
                                       .Where(c => CancelIfRequested(token))
                                       .OrderBy(c => c.Title)
                                       .ThenBy(c => c.Index)
                                       .Skip(skipCount)
                                       .Take(takeCount)
                                       .ToList();
                        contents.ForEach(c => c.Parent = notes[c.Noteid]);

                        return new SearchResult()
                        {
                            Contents = contents,
                            StartTime = startTime,
                            EndTime = DateTime.UtcNow,
                            Count = count
                        };
                    }
                }
            }, token);
            return task;
        }
        #endregion

        #region SearchFullText
        public SearchResult SearchFullText(string searchEntry)
        {
            return SearchFullText(searchEntry, 0, int.MaxValue);
        }
        public SearchResult SearchFullText(string searchEntry, int skipCount, int takeCount)
        {
            if (SearchRange == SearchRangeType.Single)
                return SearchSingleFullText(searchEntry, skipCount, takeCount);
            else
                return SearchAllFullText(searchEntry, skipCount, takeCount);
        }
        public SearchResult SearchSingleFullText(string searchEntry, int skipCount, int takeCount)
        {
            var currentNote = this.CurrentNote;
            if (currentNote == null)
                return SearchResult.Empty;
            else
                return currentNote.SearchFullText(searchEntry, skipCount, takeCount);
        }
        public SearchResult SearchAllFullText(string searchEntry, int skipCount, int takeCount)
        {
            return SearchResult.Empty;
        }
        #endregion

        #region SearchFullTextAsync
        public Task<SearchResult> SearchFullTextAsync(string searchEntry, CancellationToken token)
        {
            return SearchFullTextAsync(searchEntry, 0, int.MaxValue, token);
        }

        public Task<SearchResult> SearchFullTextAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            if (SearchRange == SearchRangeType.Single)
                return SearchSingleFullTextAsync(searchEntry, skipCount, takeCount, token);
            else
                return SearchAllFullTextAsync(searchEntry, skipCount, takeCount, token);
        }

        public Task<SearchResult> SearchSingleFullTextAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var currentNote = this.CurrentNote;
            if (currentNote == null)
                return Task.Run(() => { return SearchResult.Empty; });
            else
                return currentNote.SearchFullTextAsync(searchEntry, skipCount, takeCount, token);
        }

        public Task<SearchResult> SearchAllFullTextAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var task = Task.Run( async() =>
            {
                DateTime startTime = DateTime.UtcNow;
                var tables = CreateFullTextCountTable(searchEntry, token);
                IEnumerable<Content> contents = new List<Content>();
                foreach (var note in EnabledNotes)
                {
                    int count = tables[note.DataSource];
                    if (skipCount < count)
                    {
                        var s = await note.SearchFullTextAsync(searchEntry, skipCount, takeCount, token);
                        contents = contents.Concat(s.Contents);

                        int newSkipCount, newTakeCount;
                        if (IsNeedReadNext(count, skipCount, takeCount, out newSkipCount, out newTakeCount))
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
            foreach(var n in EnabledNotes)
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

        bool IsNeedReadNext(int resultCount, int skipCount, int takeCount, out int newSkipCount, out int newTakeCount)
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

        public Page FindPage(Guid guid)
        {
            foreach(var note in EnabledNotes)
            {
                var page = note.Read(guid);
                if (page != null)
                    return page;                
            }
            return null;
        }
     
        public IList<BookGroupItem> Collection {
            get => _items;
            set
            {
                if (!_items.Equals(value)) {
                    this.RaiseAndSetIfChanged(ref _items, value);
                    this.RaisePropertyChanged(nameof(EnabledNotes));
                }            
            }
        }

        public IList<Note> EnabledNotes
        {
            get => _items.Where(i => i.IsEnabled)
                    .OrderBy(i => i.Priority)
                    .Select(i => i.Note).ToList();
        }

        public List<string> UseDataSources
        {
            get => _items.Where(i => i.IsEnabled)
                   .Select(i => i.Note.DataSource).ToList();
        }

        public Note CurrentNote
        {
            get => _current;
            set => this.RaiseAndSetIfChanged(ref _current, value);
        }

        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        public SearchRangeType SearchRange
        {
            get => _searchRange;
            set => this.RaiseAndSetIfChanged(ref _searchRange, value);
        }

        public bool IsAutoEnabled
        {
            get => _isAutoEnabled;
            set => this.RaiseAndSetIfChanged(ref _isAutoEnabled, value);
        }

        public DateTime LastSearchTime
        {
            get => _lastSearchTime;
            set => this.RaiseAndSetIfChanged(ref _lastSearchTime, value);
        }

        public override string ToString()
        {
            return Name ?? base.ToString();
        }
    }  
}
