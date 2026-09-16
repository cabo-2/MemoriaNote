namespace MemoriaNote.Application
{
    /// <summary>
    /// Identifies the page mutation whose typed result is being presented.
    /// </summary>
    public enum PageOperationKind
    {
        /// <summary>A page creation.</summary>
        Create = 1,

        /// <summary>A page body edit.</summary>
        Edit = 2,

        /// <summary>A page rename.</summary>
        Rename = 3,

        /// <summary>A page deletion.</summary>
        Delete = 4
    }
}
