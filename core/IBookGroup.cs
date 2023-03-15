using System.Collections.Generic;

namespace MemoriaNote
{
    public interface IBookGroup
    {
        string Name { get; set; }
        SearchRangeType SearchRange { get; set; }
        bool IsAutoEnabled { get; set; }
        List<string> UseDataSources { get; }
    }
}
