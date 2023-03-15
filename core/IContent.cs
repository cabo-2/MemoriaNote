using System;
using System.Collections.Generic;

namespace MemoriaNote
{
    public interface IContent
    {
        int Rowid { get; set; }
        Guid Guid { get; set; }
        string GuidAsString { get; set; }
        string Title { get; set; }
        int Index { get; set; }
        List<string> Tags { get; set; }
        string TagsAsString { get; set; }
        string Noteid { get; set; }
        string ContentType { get; set; }
        string ViewTitle { get; }
        DateTime CreateTime { get; set; }
        DateTime UpdateTime { get; set; }
        bool IsErased { get; set; }
        object Parent { get; set; }

        bool EntityEquals(IContent other);
        Content GetContent();
    }
}
