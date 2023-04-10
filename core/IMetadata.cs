using System;
using System.Collections.Generic;

namespace MemoriaNote
{
    public interface IMetadata
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }       
        public string Description { get; set; }
        public string Author { get; set; }        
        public bool ReadOnly { get; set; }        
        public List<string> TagList { get; set; }

        public void CopyTo(IMetadata data);
    }
}