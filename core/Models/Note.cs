using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a Note in the MemoriaNote application.
    /// Provides methods for creating, migrating, and managing notes and pages in the database.
    /// </summary>
    public class Note
    {
        string _dataSource = null;

        public Note() { }
        public Note(string dataSource)
        {
            DataSource = dataSource;
        }

        /// <summary>
        /// Creates a new Note with the specified name, title, and data source.
        /// If the file already exists at the data source path, an exception is thrown.
        /// </summary>
        /// <param name="name">The name of the note.</param>
        /// <param name="title">The title of the note.</param>
        /// <param name="dataSource">The path to the data source.</param>
        /// <returns>A new Note object.</returns>
        public static Note Create(string name, string title, string dataSource)
        {
            if (File.Exists(dataSource))
                throw new ArgumentException("File exists");

            using (NoteDbContext context = new NoteDbContext(dataSource))
            {
                context.Database.Migrate();

                var md = new Metadata(context.DataSource);
                md.Name = name;
                md.Title = title;
                md.Version = NoteDbContext.CurrentVersion;
            }

            return new Note(dataSource);
        }

        /// <summary>
        /// Migrates an existing Note data source to the current version of the database schema.
        /// If the file does not exist at the specified data source path, an exception is thrown.
        /// </summary>
        /// <param name="dataSource">The path to the data source.</param>
        /// <returns>A new Note object with the migrated data source.</returns>
        public static Note Migrate(string dataSource)
        {
            if (!File.Exists(dataSource))
                throw new ArgumentException("File does not exists");

            using (NoteDbContext context = new NoteDbContext(dataSource))
            {
                context.Database.Migrate();

                var md = new Metadata(context.DataSource);

                md.Version = NoteDbContext.CurrentVersion;
            }

            return new Note(dataSource);
        }


        /// <summary>
        /// Reads a specific Page from the database based on the provided name and index.
        /// </summary>
        /// <param name="name">The name of the Page to read.</param>
        /// <param name="index">The index of the Page to read.</param>
        /// <returns>The Page object if found, or null if not found.</returns>
        public Page ReadPage(string name, int index)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
                return db.PageClient.Read(name, index);
        }

        /// <summary>
        /// Reads a specific Page from the database based on the provided unique identifier (GUID).
        /// </summary>
        /// <param name="guid">The unique identifier (GUID) of the Page to read.</param>
        /// <returns>The Page object if found, or null if not found.</returns>
        public Page ReadPage(Guid guid)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
                return db.PageClient.Read(guid);
        }

        /// <summary>
        /// Reads a specific Page from the database based on the provided Content object.
        /// </summary>
        /// <param name="content">The Content object representing the Page to read.</param>
        /// <returns>The Page object if found, or null if not found.</returns>
        public Page ReadPage(IContent content) => ReadPage(content.Guid);

        /// <summary>
        /// Retrieves a collection of pages with the specified name from the database.
        /// </summary>
        /// <param name="name">The name of the pages to retrieve.</param>
        /// <returns>An IEnumerable collection of Page objects.</returns>
        public IEnumerable<Page> ReadPage(string name)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
                return db.PageClient.Read(name).ToList();
        }

        /// <summary>
        /// Creates a new Page with the specified name, text content, and optional directory.
        /// The page is added to the database, its index is set, and the database is saved.
        /// </summary>
        /// <param name="name">The name of the page.</param>
        /// <param name="text">The text content of the page.</param>
        /// <param name="dir">Optional directory for the page. Default is null.</param>
        /// <returns>The newly created Page object.</returns>
        public Page CreatePage(string name, string text, string dir = null)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                var page = Page.Create(name, text, dir);
                page.Index = db.PageClient.GetLastIndex(name) + 1;
                db.PageClient.Add(page);
                RelocatePage(page.Name, db);
                db.SaveChanges();
                return page;
            }
        }

        /// <summary>
        /// Updates an existing Page in the database with the provided new Page object.
        /// The method retrieves the old Page from the database, updates its last modified timestamp,
        /// updates the new Page in the database, and ensures the correct index ordering by relocating 
        /// the affected pages if the name of the Page has changed.
        /// </summary>
        /// <param name="newPage">The new Page object containing the updated information.</param>
        public void UpdatePage(Page newPage)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                var oldPage = db.Pages.Find(newPage.Rowid);
                newPage.UpdateLastModified();

                var beforeName = oldPage.Name;
                db.PageClient.Update(newPage);

                RelocatePage(newPage.Name, db);
                if (newPage.Name != beforeName)
                {
                    RelocatePage(beforeName, db);
                }
                db.SaveChanges();
            }
        }

        /// <summary>
        /// Updates the index of pages with the specified name in the database to ensure correct ordering based on the last update time.
        /// The method retrieves all pages with the specified name, orders them based on the last update time in descending order,
        /// and then updates their index values sequentially to maintain the correct ordering.
        /// </summary>
        /// <param name="name">The name of the pages to relocate.</param>
        /// <param name="db">The NoteDbContext instance for interacting with the database.</param>
        protected void RelocatePage(string name, NoteDbContext db)
        {
            var pages = db.PageClient.Read(name).ToList()
                        .OrderByDescending(p => p.UpdateTime);
            int index = 1;
            foreach (var page in pages)
            {
                if (page.Index != index)
                {
                    page.Index = index;
                    db.PageClient.Update(page);
                }
                index++;
            }
        }

        /// <summary>
        /// Deletes a specific page from the database based on the provided content object.
        /// The page with the corresponding row identifier is removed from the database,
        /// and the changes are saved to the database.
        /// </summary>
        /// <param name="content">The content object representing the page to delete.</param>
        public void DeletePage(IContent content)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                db.PageClient.Remove(content.Rowid);
                db.SaveChanges();
            }
        }

        /// <summary>
        /// Deletes a specific page from the database based on the provided row identifier.
        /// The page with the corresponding row identifier is removed from the database,
        /// and the changes are saved to the database.
        /// </summary>
        /// <param name="rowid">The row identifier of the page to delete.</param>
        public void DeletePage(int rowid)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                db.PageClient.Remove(rowid);
                db.SaveChanges();
            }
        }

        /// <summary>
        /// Searches the database for contents matching the provided keyword, with optional pagination.
        /// The search is performed based on the matching type of the keyword, filtering results as needed.
        /// </summary>
        /// <param name="keyword">The keyword to search for matching contents.</param>
        /// <param name="skipCount">The number of results to skip before retrieving data.</param>
        /// <param name="takeCount">The maximum number of results to retrieve for the search query.</param>
        /// <returns>A SearchResult object containing the matching contents, total count of results, and timing information.</returns>
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
                       $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Name : \"{textMatch.Pattern}\"') f " +
                        "ON p.Rowid = f.rowid " +
                       $"{textMatch.Where("p.Name")}";
                    var querySql =
                        "SELECT p.* FROM Pages p JOIN " +
                       $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Name : \"{textMatch.Pattern}\"') f " +
                        "ON p.Rowid = f.rowid " +
                       $"{textMatch.Where("p.Name")} " +
                        "ORDER BY p.Name COLLATE NOCASE ASC, p.'Index' ASC ";

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
                         "ORDER BY Name COLLATE NOCASE ASC, 'Index' ASC ";

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
                       $"{textMatch.Where("Name")}";
                    var querySql =
                        "SELECT * FROM Contents " +
                       $"{textMatch.Where("Name")} " +
                        "ORDER BY Name COLLATE NOCASE ASC, 'Index' ASC ";

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

        /// <summary>
        /// Searches the database for contents matching the provided keyword using full-text search, with optional pagination.
        /// The search is performed based on the matching type of the keyword, filtering results as needed.
        /// </summary>
        /// <param name="keyword">The keyword to search for matching contents using full-text search.</param>
        /// <param name="skipCount">The number of results to skip before retrieving data.</param>
        /// <param name="takeCount">The maximum number of results to retrieve for the search query.</param>
        /// <returns>A SearchResult object containing the matching contents, total count of results, and timing information.</returns>
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
                        "ORDER BY p.Name COLLATE NOCASE ASC, p.'Index' ASC ";

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
                         "ORDER BY Name COLLATE NOCASE ASC, 'Index' ASC ";

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

        /// <summary>
        /// Checks if the CancellationToken requests cancellation and throws an exception if cancellation is requested.
        /// </summary>
        /// <param name="token">The CancellationToken to check for cancellation request.</param>
        /// <returns>True if the CancellationToken requested cancellation; otherwise, false.</returns>
        static bool CancelIfRequested(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }
            return true;
        }

        static bool IsEmptyKeyword(string keyword)
        {
            return string.IsNullOrWhiteSpace(keyword) || keyword == "*";
        }

        public Task<SearchResult> SearchContentsAsync(string searchEntry, CancellationToken token)
        {
            return SearchContentsAsync(searchEntry, 0, int.MaxValue, token);
        }

        /// <summary>
        /// Asynchronously searches the database for contents matching the provided search entry using heading search, 
        /// with optional pagination and cancellation token. The search is performed based on the matching type of the search entry, 
        /// filtering results as needed while allowing for cancellation.
        /// </summary>
        /// <param name="searchEntry">The search entry to match contents using heading search.</param>
        /// <param name="skipCount">The number of matching results to skip before retrieving data.</param>
        /// <param name="takeCount">The maximum number of matching results to retrieve for the search query.</param>
        /// <param name="token">The CancellationToken used for cancellation request during the search operation.</param>
        /// <returns>A task that represents the asynchronous search operation, returning a SearchResult object with matching contents, 
        /// total count of results, and timing information.</returns>
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
                          $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Name : \"{textMatch.Pattern}\"') f " +
                           "ON p.Rowid = f.rowid " +
                          $"{textMatch.Where("p.Name")} " +
                           "ORDER BY p.Name COLLATE NOCASE ASC, p.'Index' ASC ";

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
                            "ORDER BY Name COLLATE NOCASE ASC, 'Index' ASC ";

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
                           $"{textMatch.Where("Name")} ";
                       var querySql =
                           $"SELECT * FROM Contents " +
                           $"{textMatch.Where("Name")} " +
                           "ORDER BY Name COLLATE NOCASE ASC, 'Index' ASC ";

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
        /// <summary>
        /// Asynchronously searches the database for contents matching the provided search entry using full-text search, 
        /// with optional pagination and cancellation token. The search is performed based on the matching type of the search entry, 
        /// filtering results as needed while allowing for cancellation.
        /// </summary>
        /// <param name="searchEntry">The search entry to match contents using full-text search.</param>
        /// <param name="skipCount">The number of matching results to skip before retrieving data.</param>
        /// <param name="takeCount">The maximum number of matching results to retrieve for the search query.</param>
        /// <param name="token">The CancellationToken used for cancellation request during the search operation.</param>
        /// <returns>A task that represents the asynchronous search operation, returning a SearchResult object with matching contents, 
        /// total count of results, and timing information.</returns>
        public Task<SearchResult> SearchFullTextAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
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
                            "ORDER BY p.Name COLLATE NOCASE ASC, p.'Index' ASC ";

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
                             "ORDER BY Name COLLATE NOCASE ASC, 'Index' ASC ";

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

        /// <summary>
        /// Gets the count of contents in the database using the NoteDbContext specified by the DataSource property. 
        /// </summary>
        /// <returns>An integer representing the total count of contents in the database.</returns>
        public int Count
        {
            get
            {
                using (NoteDbContext db = new NoteDbContext(DataSource))
                    return db.Contents.Count();
            }
        }

        /// <summary>
        /// Retrieves a list of content items from the database based on the provided skip count and take count, using the specified NoteDbContext as the data source.
        /// </summary>
        /// <param name="skipCount">The number of content items to skip before retrieving data.</param>
        /// <param name="takeCount">The maximum number of content items to retrieve from the database.</param>
        /// <returns>A list of Content objects representing the retrieved content items.</returns>
        public List<Content> GetContents(int skipCount, int takeCount)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
                return db.ContentClient
                            .ReadAll()
                            .Skip(skipCount)
                            .Take(takeCount)
                            .ToList();
        }

        /// <summary>
        /// Gets or sets the data source for the search operation, initializing the Metadata property with the value provided.
        /// If the provided value is not null, sets the data source and initializes the Metadata property with a new Metadata object using the value.
        /// If the provided value is null, resets the data source to null and sets the Metadata property to null.
        /// </summary>
        public string DataSource
        {
            get => _dataSource;
            set
            {
                if (value != null)
                {
                    _dataSource = value;
                    Metadata = new Metadata(value);
                }
                else
                {
                    _dataSource = null;
                    Metadata = null;
                }
            }
        }

        /// <summary>
        /// Gets or sets the metadata for the content item. The metadata includes information such as the name and title of the content.
        /// </summary>
        public Metadata Metadata { get; private set; }
        
        public override string ToString()
        {
            if (Metadata != null)
            {
                StringBuilder buffer = new StringBuilder();
                buffer.Append(Metadata.Name);
                buffer.Append(" (");
                buffer.Append(Metadata.Title);
                buffer.Append(")");
                return buffer.ToString();
            }
            return base.ToString();
        }
    }
}
