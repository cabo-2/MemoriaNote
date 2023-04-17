using System;
using System.Collections.Generic;
using System.Linq;
namespace MemoriaNote
{
    public enum SearchRangeType : int
    {
        Note = 0,
        Workgroup = 1
    }

    public enum SearchMethodType : int
    {
        Heading = 0,
        FullText = 1
    }
}
