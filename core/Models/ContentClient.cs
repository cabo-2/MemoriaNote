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
            return DbContext.Contents.Find(guid.ToUuid());
        }

        public Content Read(string name, int index)
        {
            return DbContext.Contents.FirstOrDefault(m => m.Name == name && m.Index == index);
        }

        public IEnumerable<Content> Read(string name)
        {
            return DbContext.Contents.Where(m => m.Name == name);
        }

        public IEnumerable<Content> ReadAll()
        {
            return DbContext.Contents;
        }
        
        public NoteDbContext DbContext { get; set; }
    }
}
