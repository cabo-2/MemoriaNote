using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a class that provides functionality to interact with Page entities in the DbContext.
    /// </summary>
    public class PageClient
    {
        public PageClient() { }
        public PageClient(NoteDbContext context)
        {
            DbContext = context;
        }

        /// <summary>
        /// Reads a Page with a specific rowid from the DbContext.
        /// </summary>
        /// <param name="rowid">The rowid of the Page to read.</param>
        /// <returns>The Page with the specified rowid, if found; otherwise null.</returns>
        public Page Read(int rowid)
        {
            return DbContext.Pages.Find(rowid);
        }

        /// <summary>
        /// Reads a Page with a specific name and index from the DbContext.
        /// </summary>
        /// <param name="name">The name of the Page to read.</param>
        /// <param name="index">The index of the Page to read.</param>
        /// <returns>The Page with the specified name and index, if found; otherwise null.</returns>
        public Page Read(string name, int index)
        {
            return DbContext.Pages.FirstOrDefault(m => m.Name == name && m.Index == index);
        }

        /// <summary>
        /// Reads a Page with a specific GUID from the DbContext.
        /// </summary>
        /// <param name="guid">The GUID of the Page to read.</param>
        /// <returns>The Page with the specified GUID, if found; otherwise null.</returns>
        public Page Read(Guid guid)
        {
            var uuid = guid.ToUuid();
            return DbContext.Pages.FirstOrDefault(m => m.Uuid == uuid);
        }

        /// <summary>
        /// Retrieves a collection of Pages with a specific name from the DbContext.
        /// </summary>
        /// <param name="name">The name of the Pages to retrieve.</param>
        /// <returns>A collection of Pages with the specified name.</returns>
        public IEnumerable<Page> Read(string name)
        {
            return DbContext.Pages.Where(m => m.Name == name);
        }

        /// <summary>
        /// Retrieves all Pages from the DbContext.
        /// </summary>
        /// <returns>A collection of all Pages stored in the DbContext.</returns>
        public IEnumerable<Page> ReadAll()
        {
            return DbContext.Pages;
        }

        /// <summary>
        /// Adds a new Page to the DbContext if the specified Page does not already exist.
        /// </summary>
        /// <param name="value">The Page to add to the DbContext.</param>
        /// <exception cref="ArgumentException">Thrown when a duplicate entry is found. Use the update method instead.</exception>
        public void Add(Page value)
        {
            var entity = DbContext.Pages.Find(value.Rowid);
            if (entity == null)
                DbContext.Pages.Add(value);
            else
                throw new ArgumentException("Duplicate entries, use update method");
        }

        /// <summary>
        /// Updates an existing Page in the DbContext with the values of the specified Page.
        /// </summary>
        /// <param name="value">The Page with updated values to apply to the existing Page.</param>
        /// <exception cref="ArgumentException">Thrown when the update entry is not found. Use the create method instead.</exception>
        public void Update(Page value)
        {
            var entity = DbContext.Pages.Find(value.Rowid);
            if (entity != null)
                DbContext.Entry(entity).CurrentValues.SetValues(value);
            else
                throw new ArgumentException("Not found update entry, use create method");
        }

        /// <summary>
        /// Removes a Page with a specific rowid from the DbContext if it exists.
        /// </summary>
        /// <param name="rowid">The rowid of the Page to remove.</param>
        public void Remove(int rowid)
        {
            var entity = Read(rowid);
            if (entity != null)
                DbContext.Pages.Remove(entity);
        }

        /// <summary>
        /// Gets the last index of a Page with a specific name from the DbContext.
        /// If no Page with the specified name is found, returns 0.
        /// </summary>
        /// <param name="name">The name of the Page to get the last index for.</param>
        /// <returns>The last index of the Page with the specified name, or 0 if no Page is found.</returns>
        public int GetLastIndex(string name)
        {
            var last = DbContext.Pages.OrderBy(p => p.Name).LastOrDefault(m => m.Name == name);
            return last != null ? last.Index : 0;
        }

        /// <summary>
        /// Gets or sets the NoteDbContext used by the PageClient.
        /// </summary>
        public NoteDbContext DbContext { get; set; }
    }
}
