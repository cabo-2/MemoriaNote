namespace MemoriaNote.Cli
{
    /// <summary>
    /// Identifies the current presentation editing workflow.
    /// </summary>
    public enum EditorMode
    {
        /// <summary>No editing workflow is active.</summary>
        None = 0,

        /// <summary>The editor is creating a page.</summary>
        Create = 1,

        /// <summary>The editor is changing a page body.</summary>
        Edit = 2,

        /// <summary>The editor is renaming a page.</summary>
        Rename = 3,

        /// <summary>The editor is deleting a page.</summary>
        Delete = 4
    }
}
