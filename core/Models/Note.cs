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
                return SetOwner(db.PageClient.Read(name, index));
        }

        /// <summary>
        /// Reads a specific Page from the database based on the provided unique identifier (GUID).
        /// </summary>
        /// <param name="guid">The unique identifier (GUID) of the Page to read.</param>
        /// <returns>The Page object if found, or null if not found.</returns>
        public Page ReadPage(Guid guid)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
                return SetOwner(db.PageClient.Read(guid));
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
                return db.PageClient.Read(name).ToList().Select(SetOwner).ToList();
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
            using (var transaction = db.Database.BeginTransaction())
            {
                var page = Page.Create(name, text, dir);
                page.Index = db.PageClient.GetLastIndex(name) + 1;
                db.PageClient.Add(page);

                var pages = db.PageClient.Read(name).ToList();
                pages.Add(page);
                NormalizePageIndexes(pages);

                db.SaveChanges();
                transaction.Commit();
                return SetOwner(page);
            }
        }

        /// <summary>
        /// Updates an existing Page in the database with the provided new Page object.
        /// The method retrieves the old Page from the database, updates its last modified timestamp,
        /// and updates the new Page without changing its dictionary sense index. If the name changes,
        /// the page is appended to the destination name and the source indexes are compacted.
        /// </summary>
        /// <param name="newPage">The new Page object containing the updated information.</param>
        public void UpdatePage(Page newPage)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
            using (var transaction = db.Database.BeginTransaction())
            {
                var oldPage = db.Pages.Find(newPage.Rowid);
                var beforeName = oldPage.Name;
                var beforeIndex = oldPage.Index;
                var sourcePages = db.PageClient.Read(beforeName).ToList();
                var nameChanged = !string.Equals(
                    newPage.Name,
                    beforeName,
                    StringComparison.Ordinal);
                var destinationPages = nameChanged
                    ? db.PageClient.Read(newPage.Name).ToList()
                    : sourcePages;

                newPage.UpdateLastModified();
                db.PageClient.Update(newPage);

                if (nameChanged)
                {
                    NormalizePageIndexes(
                        sourcePages.Where(page => page.Rowid != oldPage.Rowid));
                    NormalizePageIndexes(destinationPages);
                    oldPage.Index = destinationPages.Count + 1;
                }
                else
                {
                    oldPage.Index = beforeIndex;
                    NormalizePageIndexes(sourcePages);
                }

                db.SaveChanges();
                transaction.Commit();
                newPage.Index = oldPage.Index;
            }
        }

        /// <summary>
        /// Normalizes page indexes while preserving their current display order.
        /// </summary>
        /// <param name="pages">The pages in one exact-name group.</param>
        protected void NormalizePageIndexes(IEnumerable<Page> pages)
        {
            int index = 1;
            foreach (var page in pages
                .OrderBy(page => page.Index)
                .ThenBy(page => page.Rowid))
            {
                page.Index = index;
                index++;
            }
        }

        /// <summary>
        /// Deletes a specific page from the database based on the provided content object.
        /// The page with the corresponding row identifier is removed and the remaining indexes
        /// for its exact-name group are compacted in the same transaction.
        /// </summary>
        /// <param name="content">The content object representing the page to delete.</param>
        public void DeletePage(IContent content)
        {
            DeletePage(content.Rowid);
        }

        /// <summary>
        /// Deletes a specific page from the database based on the provided row identifier.
        /// The page with the corresponding row identifier is removed and the remaining indexes
        /// for its exact-name group are compacted in the same transaction.
        /// </summary>
        /// <param name="rowid">The row identifier of the page to delete.</param>
        public void DeletePage(int rowid)
        {
            using (NoteDbContext db = new NoteDbContext(DataSource))
            using (var transaction = db.Database.BeginTransaction())
            {
                var page = db.PageClient.Read(rowid);
                if (page == null)
                {
                    transaction.Commit();
                    return;
                }

                var pages = db.PageClient.Read(page.Name).ToList();
                db.PageClient.Remove(page.Rowid);
                NormalizePageIndexes(
                    pages.Where(candidate => candidate.Rowid != page.Rowid));

                db.SaveChanges();
                transaction.Commit();
            }
        }

        /// <summary>
        /// Asynchronously searches this note using the specified search method.
        /// </summary>
        /// <param name="searchEntry">The search entry to match.</param>
        /// <param name="searchMethod">The search method to use.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The matching contents and total count.</returns>
        public Task<SearchResult> SearchAsync(
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            return SearchAsync(searchEntry, searchMethod, 0, int.MaxValue, token);
        }

        /// <summary>
        /// Asynchronously searches this note using the specified search method and paging values.
        /// </summary>
        /// <param name="searchEntry">The search entry to match.</param>
        /// <param name="searchMethod">The search method to use.</param>
        /// <param name="skipCount">The number of matching contents to skip.</param>
        /// <param name="takeCount">The maximum number of matching contents to return.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The matching contents and total count.</returns>
        public Task<SearchResult> SearchAsync(
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            if (searchMethod == SearchMethodType.Heading)
                return SearchHeadingsAsync(searchEntry, skipCount, takeCount, token);
            else
                return SearchFullTextAsync(searchEntry, skipCount, takeCount, token);
        }

        private async Task<SearchResult> SearchHeadingsAsync(
            string searchEntry,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            DateTime startTime = DateTime.UtcNow;
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                List<Content> contents;
                int count;
                TextMatching textMatch = TextMatching.Create(searchEntry);
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

                    count = await db.Pages.FromSqlRaw(countSql).CountAsync(token);
                    var pages = await db.Pages.FromSqlRaw(querySql)
                        .Skip(skipCount)
                        .Take(takeCount)
                        .ToListAsync(token);
                    contents = pages.ConvertAll(page => page.GetContent());
                }
                else if (textMatch.MatchingType == MatchingType.None)
                {
                    var sql =
                         "SELECT * FROM Contents " +
                         "ORDER BY Name COLLATE NOCASE ASC, 'Index' ASC ";

                    count = await db.Contents.CountAsync(token);
                    contents = await db.Contents.FromSqlRaw(sql)
                        .Skip(skipCount)
                        .Take(takeCount)
                        .ToListAsync(token);
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

                    count = await db.Contents.FromSqlRaw(countSql).CountAsync(token);
                    contents = await db.Contents.FromSqlRaw(querySql)
                        .Skip(skipCount)
                        .Take(takeCount)
                        .ToListAsync(token);
                }

                contents.ForEach(content => SetOwner(content));
                return new SearchResult()
                {
                    Contents = contents,
                    Count = count,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow
                };
            }
        }

        private async Task<SearchResult> SearchFullTextAsync(
            string searchEntry,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            DateTime startTime = DateTime.UtcNow;
            using (NoteDbContext db = new NoteDbContext(DataSource))
            {
                List<Content> contents;
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

                    count = await db.Pages.FromSqlRaw(countSql).CountAsync(token);
                    var pages = await db.Pages.FromSqlRaw(querySql)
                        .Skip(skipCount)
                        .Take(takeCount)
                        .ToListAsync(token);
                    contents = pages.ConvertAll(page => page.GetContent());
                }
                else
                {
                    var sql =
                         "SELECT * FROM Contents " +
                         "ORDER BY Name COLLATE NOCASE ASC, 'Index' ASC ";

                    count = await db.Contents.CountAsync(token);
                    contents = await db.Contents.FromSqlRaw(sql)
                        .Skip(skipCount)
                        .Take(takeCount)
                        .ToListAsync(token);
                }

                contents.ForEach(content => SetOwner(content));
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
                            .ToList()
                            .Select(SetOwner)
                            .ToList();
        }

        private T SetOwner<T>(T content) where T : class, IContent
        {
            if (content != null)
            {
                content.OwnerDataSource = Path.GetFullPath(DataSource);
                content.Parent = this;
            }

            return content;
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
