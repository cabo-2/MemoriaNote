﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote
{
    public class Note
    {
        string _dataSource = null;

        public Note() {}
        public Note(string dataSource)
        {
            DataSource = dataSource;
        }

        public static Note Create (string name, string title, string dataSource) {
            if (File.Exists (dataSource))
                throw new ArgumentException ("File exists");

            using (NoteDbContext context = new NoteDbContext (dataSource)) {
                context.Database.Migrate();

                var tp = new TitlePage (context.DataSource);
                tp.Name = name;
                tp.Title = title;
                tp.Version = NoteDbContext.CurrentVersion;
                tp.Noteid = title.CalculateHash();
            }

            return new Note (dataSource);
        }

        public static Note Migrate(string dataSource)
        {
            if (!File.Exists (dataSource))
                throw new ArgumentException ("File does not exists");

            using (NoteDbContext context = new NoteDbContext(dataSource))
            {
                context.Database.Migrate();
                // application migration code here                
                var tp = new TitlePage(context.DataSource);
                if (string.IsNullOrEmpty(tp.Noteid))
                    tp.Noteid = NoteDbContext.GenerateID(tp.Title);                

                tp.Version = NoteDbContext.CurrentVersion;
            }

            return new Note(dataSource);
        }


        public Page Read (string title, int index) {
            using (NoteDbContext db = new NoteDbContext(DataSource))
                return db.PageClient.Read(title, index);
        }

        public Page Read (Guid guid)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
                return db.PageClient.Read(guid);
        }
    
        public Page Read (IContent content) => Read(content.Guid);


        public IEnumerable<Page> Read(string title)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
                return db.PageClient.Read(title);
        }

        public IEnumerable<Page> ReadAll ()
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
                return db.Pages.AsEnumerable();
        }

        public Page Create(string title, string text, params string[] tags) {
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                var page = Page.Create(TitlePage.Noteid, title, text, tags);
                page.Index = db.PageClient.GetLastIndex(title) + 1;
                db.PageClient.Add(page);
                RelocatePage(page.Title, db);
                db.SaveChanges();
                return page;
            }
        }

        public void Update(Page newPage) {
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                var oldPage = db.Pages.Find(newPage.Rowid);
                var history = PageHistory.Create(oldPage, newPage);
                if (string.IsNullOrEmpty(history.TitlePatch) &&
                    string.IsNullOrEmpty(history.TextPatch) &&
                    string.IsNullOrEmpty(history.TagsPatch))
                    return;
                db.HistoryClient.Add(history);
                newPage.UpdateLastModified();

                var beforeTitle = oldPage.Title;
                db.PageClient.Update(newPage);

                RelocatePage(newPage.Title, db);
                if (newPage.Title != beforeTitle)
                {
                    RelocatePage(beforeTitle, db);
                }
                db.SaveChanges();
            }
        }

        protected void RelocatePage(string title, NoteDbContext db) {
            var pages = db.PageClient.Read(title).ToList()
                        .OrderByDescending(p => p.UpdateTime);
            int index = 1;
            foreach (var page in pages) {
                if (page.Index != index) {
                    page.Index = index;
                    db.PageClient.Update(page);
                }
                index++;
            }
        }

        public void Delete (Content content) {
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                db.PageClient.Remove(content.Rowid);
                db.SaveChanges();
            }
        }

        public void Delete (int rowid) {
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                db.PageClient.Remove(rowid);
                db.SaveChanges();
            }
        }

        public SearchResult SearchContents(string keyword, int skipCount, int takeCount)
        {
            DateTime startTime = DateTime.UtcNow;
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                List<Content> contents = null;
                int count;
                TextMatching textMatch = TextMatching.Create(keyword);
                if (textMatch.MatchingType == MatchingType.Exact)
                {
                    var countSql =
                        "SELECT p.* FROM Pages p JOIN " +
                       $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Title : \"{textMatch.Pattern}\"') f " +
                        "ON p.Rowid = f.rowid " +
                       $"{textMatch.GetWhereClause("p.Title")}";
                    var querySql =
                        "SELECT p.* FROM Pages p JOIN " +
                       $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Title : \"{textMatch.Pattern}\"') f " +
                        "ON p.Rowid = f.rowid " +
                       $"{textMatch.GetWhereClause("p.Title")} " +
                        "ORDER BY p.Title COLLATE NOCASE ASC, p.'Index' ASC ";

                    count = db.Pages.FromSqlRaw(countSql).Count();
                    contents = db.Pages.FromSqlRaw(querySql)
                              .Skip(skipCount)
                              .Take(takeCount)
                              .Select(p => p.GetContent())
                              .ToList();
                }
                else if (textMatch.MatchingType == MatchingType.None)
                {
                    var sql =
                         "SELECT * FROM Contents " +
                         "ORDER BY Title COLLATE NOCASE ASC, 'Index' ASC ";

                    count = db.Contents.Count();
                    contents = db.Contents.FromSqlRaw(sql)
                              .Skip(skipCount)
                              .Take(takeCount)
                              .ToList();
                }
                else
                {
                    var countSql =
                        "SELECT * FROM Contents " +
                       $"{textMatch.GetWhereClause("Title")}";
                    var querySql =
                        "SELECT * FROM Contents " +
                       $"{textMatch.GetWhereClause("Title")} " +
                        "ORDER BY Title COLLATE NOCASE ASC, 'Index' ASC ";

                    count = db.Contents.FromSqlRaw(countSql).Count();
                    contents = db.Contents.FromSqlRaw(querySql)
                              .Skip(skipCount)
                              .Take(takeCount)
                              .ToList();
                }
                contents.ForEach(c => c.Parent = this);
                return new SearchResult()
                {
                    Contents = contents,
                    Count = count,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow
                };
            }
        }

        public SearchResult SearchFullText(string keyword, int skipCount, int takeCount)
        {
            DateTime startTime = DateTime.UtcNow;
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                List<Content> contents = null;
                int count;
                TextMatching textMatch = TextMatching.Create(keyword);
                if (textMatch.MatchingType != MatchingType.None)
                {
                    var countSql =
                        "SELECT p.* FROM Pages p JOIN " +
                       $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Text : \"{textMatch.Pattern}\"') f " +
                        "ON p.Rowid = f.rowid";
                    var querySql =
                        "SELECT p.* FROM Pages p JOIN " +
                       $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Text : \"{textMatch.Pattern}\"') f " +
                        "ON p.Rowid = f.rowid " +
                        "ORDER BY p.Title COLLATE NOCASE ASC, p.'Index' ASC ";

                    count = db.Pages.FromSqlRaw(countSql).Count();
                    contents = db.Pages.FromSqlRaw(querySql)
                              .Skip(skipCount)
                              .Take(takeCount)
                              .Select(p => p.GetContent())
                              .ToList();
                }
                else
                {
                    var sql =
                         "SELECT * FROM Contents " +
                         "ORDER BY Title COLLATE NOCASE ASC, 'Index' ASC ";

                    count = db.Contents.Count();
                    contents = db.Contents.FromSqlRaw(sql)
                              .Skip(skipCount)
                              .Take(takeCount)
                              .ToList();
                }
                contents.ForEach(c => c.Parent = this);
                return new SearchResult()
                {
                    Contents = contents,
                    Count = count,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow
                };
            }
        }

        static bool CancelIfRequested(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {               
                token.ThrowIfCancellationRequested();
            }
            return true;
        }

        static bool IsEmptyKeyword (string keyword) {
            return string.IsNullOrWhiteSpace (keyword) || keyword == "*";
        }

        public Task<SearchResult> SearchContentsAsync(string searchEntry, CancellationToken token)
        {
            return SearchContentsAsync(searchEntry, 0, int.MaxValue, token);
        }
        public Task<SearchResult> SearchContentsAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var task = Task.Run(async () =>
           {
               DateTime startTime = DateTime.UtcNow;
               await Task.Yield(); //dummy
               using (NoteDbContext db = new NoteDbContext(DataSource))
               {
                   List<Content> contents = null;
                   int count;
                   TextMatching textMatch = TextMatching.Create(searchEntry);
                   if (textMatch.MatchingType == MatchingType.Exact)
                   {
                       var sql =
                           "SELECT p.* FROM Pages p JOIN " +
                          $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Title : \"{textMatch.Pattern}\"') f " +
                           "ON p.Rowid = f.rowid " +
                          $"{textMatch.GetWhereClause("p.Title")} " +
                           "ORDER BY p.Title COLLATE NOCASE ASC, p.'Index' ASC ";

                       count = db.Contents.FromSqlRaw(sql).Count();
                       contents = db.Contents.FromSqlRaw(sql)
                                 .Where(m => CancelIfRequested(token))
                                 .Skip(skipCount)
                                 .Take(takeCount)
                                 .Select(p => p.GetContent())
                                 .ToList();
                   }
                   else if (textMatch.MatchingType == MatchingType.None)
                   {
                       var sql =
                            "SELECT * FROM Contents " +
                            "ORDER BY Title COLLATE NOCASE ASC, 'Index' ASC ";

                       count = db.Contents.Count();
                       contents = db.Contents.FromSqlRaw(sql)
                                 .Where(m => CancelIfRequested(token))
                                 .Skip(skipCount)
                                 .Take(takeCount)
                                 .ToList();
                   }
                   else
                   {
                       var countSql =
                           $"SELECT * FROM Contents " +
                           $"{textMatch.GetWhereClause("Title")} ";
                       var querySql =
                           $"SELECT * FROM Contents " +
                           $"{textMatch.GetWhereClause("Title")} " +
                           "ORDER BY Title COLLATE NOCASE ASC, 'Index' ASC ";

                       count = db.Contents.FromSqlRaw(countSql).Count();
                       contents = db.Contents.FromSqlRaw(querySql)
                                 .Where(m => CancelIfRequested(token))
                                 .Skip(skipCount)
                                 .Take(takeCount)
                                 .Select(p => p.GetContent())
                                 .ToList();
                   }
                   contents.ForEach(c => c.Parent = this);
                   return new SearchResult()
                   {
                       Contents = contents,
                       Count = count,
                       StartTime = startTime,
                       EndTime = DateTime.UtcNow
                   };
               }
           }, token);
            return task;
        }

        public Task<SearchResult> SearchFullTextAsync(string searchEntry, CancellationToken token)
        {
            return SearchFullTextAsync(searchEntry, 0, int.MaxValue, token);
        }
        public Task<SearchResult> SearchFullTextAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var task = Task.Run( async() =>
            {
                DateTime startTime = DateTime.UtcNow;
                await Task.Yield(); // dummy
                using (NoteDbContext db = new NoteDbContext(DataSource))
                {
                    List<Content> contents = null;
                    int count;
                    TextMatching textMatch = TextMatching.Create(searchEntry);
                    if (textMatch.MatchingType != MatchingType.None)
                    {
                        var countSql =
                            "SELECT p.* FROM Pages p JOIN " +
                           $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Text : \"{textMatch.Pattern}\"') f " +
                            "ON p.Rowid = f.rowid";
                        var querySql =
                            "SELECT p.* FROM Pages p JOIN " +
                           $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Text : \"{textMatch.Pattern}\"') f " +
                            "ON p.Rowid = f.rowid " +
                            "ORDER BY p.Title COLLATE NOCASE ASC, p.'Index' ASC ";

                        count = db.Pages.FromSqlRaw(countSql).Count();
                        contents = db.Pages.FromSqlRaw(querySql)
                                  .Where(m => CancelIfRequested(token))
                                  .Skip(skipCount)
                                  .Take(takeCount)
                                  .Select(p => p.GetContent())
                                  .ToList();
                    }
                    else
                    {
                        var sql =
                             "SELECT * FROM Contents " +
                             "ORDER BY Title COLLATE NOCASE ASC, 'Index' ASC ";

                        count = db.Contents.Count();
                        contents = db.Contents.FromSqlRaw(sql)
                                  .Where(m => CancelIfRequested(token))
                                  .Skip(skipCount)
                                  .Take(takeCount)
                                  .ToList();
                    }
                    contents.ForEach(c => c.Parent = this);
                    return new SearchResult()
                    {
                        Contents = contents,
                        Count = count,
                        StartTime = startTime,
                        EndTime = DateTime.UtcNow
                    };
                }
            }, token);
            return task;
        }

        public IEnumerable<Page> GetPageHistory(Page page)
        {
            var current = page;
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                yield return page;
                foreach (var history in db.HistoryClient.Read(page.Rowid))
                {
                    current = history.Restore(current);
                    yield return current;
                }
            }
        }

        public string DataSource
        {
            get => _dataSource;
            set
            {
                if (value != null)
                {
                    _dataSource = value;
                    TitlePage = new TitlePage(value);
                }
                else
                {
                    _dataSource = null;
                    TitlePage = null;
                }
            }
        }

        public TitlePage TitlePage { get; private set; }

        public override string ToString () {
            if (TitlePage != null) {
                return TitlePage.Title;
            }
            return base.ToString ();
        }
   
    }
}
