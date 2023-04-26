using System;
using System.Collections.Generic;

namespace MemoriaNote
{
    public interface IDataSource : IEquatable<IDataSource>
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }       
        public string Description { get; set; }
        public string Author { get; set; }        
        public bool ReadOnly { get; set; }        
        public string Tag { get; set; }
        public DateTime CreateTime { get; set; }
        public string DataSource { get; set; }

        public void CopyTo(IDataSource dest);                
        public string ToString();
        public IDataSource Clone();
    }
}