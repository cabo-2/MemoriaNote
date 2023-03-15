using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    public class PageClient
    {
        public PageClient () {}
        public PageClient(NoteDbContext context)
        {
            DbContext = context;
        }        

        public Page Read(int rowid)
        {
            return DbContext.Pages.Find(rowid);
        }

        public Page Read(string title, int index)
        {
            return DbContext.Pages.FirstOrDefault(m => m.Title == title && m.Index == index);
        }

        public Page Read(Guid guid)
        {
            var guidString = guid.ToString("B");
            return DbContext.Pages.FirstOrDefault(m => m.GuidAsString == guidString);
        }

        public IEnumerable<Page> Read(string title)
        {
            return DbContext.Pages.Where(m => m.Title == title);
        }

        public Page Add (string title, string text, string tag) {
            var page = Page.Create (title, text, tag);
            Add (page);
            return page;
        }

        public void Add (Page value)
        {
            var entity = DbContext.Pages.Find(value.Rowid);
            if (entity == null)
                DbContext.Pages.Add(value);
            else
                throw new ArgumentException("Duplicate entries, use update method");
        }

        public void Update (int rowid, string title, string text) {
            var entity = Read (rowid);
            entity.Title = title;
            entity.Text = text;
            Update (entity);
        }

        public void Update(Page value)
        {
            var entity = DbContext.Pages.Find(value.Rowid);
            if (entity != null)
                DbContext.Entry(entity).CurrentValues.SetValues(value);
            else
                throw new ArgumentException("Not found update entry, use create method");
        }

        public void Remove(int rowid)
        {
            var entity = Read(rowid);
            if (entity != null)
                DbContext.Pages.Remove(entity);
        }

        public int GetLastIndex(string title)
        {
            var last = DbContext.Pages.LastOrDefault(m => m.Title == title);
            return last != null ? last.Index : 0;
        }

        public NoteDbContext DbContext { get; set; }
    }
}
