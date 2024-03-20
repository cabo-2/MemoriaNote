using System.Collections.Generic;

namespace MemoriaNote
{
    /// <summary>
    /// Interface representing a workgroup
    /// </summary>
    public interface IWorkgroup
    {
        /// <summary>
        /// Property to get or set the name of the workgroup
        /// </summary>
        string Name { get; set; }
        
        /// <summary>
        /// Property to get the selected note name of the workgroup
        /// </summary>
        string SelectedNoteName { get; }
        
        /// <summary>
        /// Property to get a list of data sources used by the workgroup
        /// </summary>
        List<string> UseDataSources { get; }
    }
}
