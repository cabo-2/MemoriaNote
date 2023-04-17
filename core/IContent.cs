using System;
using System.Collections.Generic;

namespace MemoriaNote
{
    public interface IContent
    {
        int Rowid { get; set; }
        Guid Guid { get; set; }
        string Uuid { get; set; }
        string Name { get; set; }
        int Index { get; set; }
        Dictionary<string, string> TagDict { get; set; }
        string Tags { get; set; }
        string ContentType { get; set; }
        DateTime CreateTime { get; set; }
        DateTime UpdateTime { get; set; }
        bool IsErased { get; set; }
        object Parent { get; set; }

        bool EntityEquals(IContent other);
        Content GetContent();
    }
}
