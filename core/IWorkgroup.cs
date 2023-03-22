using System.Collections.Generic;

namespace MemoriaNote
{
    public interface IWorkgroup
    {
        string Name { get; set; }
        string SelectedNoteName { get; }
        List<string> UseDataSources { get; }
    }
}
