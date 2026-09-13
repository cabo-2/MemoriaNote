using System;
using System.Collections.Generic;
using System.Linq;
namespace MemoriaNote
{
    /// <summary>
    /// Define an enumeration for different search range types such as Note and Workspace
    /// </summary>
    public enum SearchRangeType : int
    {
        Note = 0,
        Workspace = 1
    }

    /// <summary>
    /// Define an enumeration for different search method types such as Heading and FullText
    /// </summary>
    public enum SearchMethodType : int
    {
        Heading = 0,
        FullText = 1
    }
}
