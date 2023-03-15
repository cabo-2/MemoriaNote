using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    public class PageHistoryClient
    {
        public PageHistoryClient() { }
        public PageHistoryClient(NoteDbContext context)
        {
            this.DbContext = context;
        }

        public IEnumerable<PageHistory> Read(int rowid)
        {
            return DbContext.History.Where(h => h.Rowid == rowid)
                                    .OrderByDescending(h => h.Generation)
                                    .AsEnumerable();
        }

        public PageHistory ReadLast(int rowid)
        {
            return DbContext.History.Where(h => h.Rowid == rowid)
                                    .OrderByDescending(h => h.Generation)
                                    .FirstOrDefault();
        }

        public void Add(Page oldPage, Page newPage)
        {
            var history = PageHistory.Create(oldPage, newPage);
            Add(history);
        }

        public void Add(PageHistory value)
        {
            var last = ReadLast(value.Rowid);
            value.Generation = last != null ? last.Generation + 1 : 0;
            DbContext.History.Add(value);
        }

        public void Remove(int rowid, long generation)
        {
            var entity = DbContext.History.Find(rowid, generation);
            if (entity != null)
                DbContext.History.Remove(entity);
        }

        public NoteDbContext DbContext { get; set; }
    }
}
