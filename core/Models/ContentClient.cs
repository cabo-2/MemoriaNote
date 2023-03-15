using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    public class ContentClient
    {
        public ContentClient() { }
        public ContentClient(NoteDbContext dbContext)
        {
            DbContext = dbContext;
        }

        public Content Read(Guid guid)
        {
            return DbContext.Contents.Find(guid.ToString("B"));
        }

        public Content Read(string title, int index)
        {
            return DbContext.Contents.FirstOrDefault(m => m.Title == title && m.Index == index);
        }

        public IEnumerable<Content> Read(string title)
        {
            return DbContext.Contents.Where(m => m.Title == title);
        }
        
        public NoteDbContext DbContext { get; set; }
    }
}
