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

        public Page Read(string name, int index)
        {
            return DbContext.Pages.FirstOrDefault(m => m.Name == name && m.Index == index);
        }

        public Page Read(Guid guid)
        {
            var uuid = guid.ToUuid();
            return DbContext.Pages.FirstOrDefault(m => m.Uuid == uuid);
        }

        public IEnumerable<Page> Read(string name)
        {
            return DbContext.Pages.Where(m => m.Name == name);
        }

        public IEnumerable<Page> ReadAll()
        {
            return DbContext.Pages;
        }

        public Page Add (string name, string text, string tag) {
            var page = Page.Create (name, text, tag);
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

        public void Update (int rowid, string name, string text) {
            var entity = Read (rowid);
            entity.Name = name;
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

        public int GetLastIndex(string name)
        {
            var last = DbContext.Pages.OrderBy(p => p.Name).LastOrDefault(m => m.Name == name);
            return last != null ? last.Index : 0;
        }

        public NoteDbContext DbContext { get; set; }
    }
}
