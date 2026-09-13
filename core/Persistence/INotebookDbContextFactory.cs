namespace MemoriaNote
{
    /// <summary>
    /// Creates independently configured database contexts for notebook data sources.
    /// </summary>
    public interface INotebookDbContextFactory
    {
        /// <summary>
        /// Creates a database context for the specified notebook data source.
        /// </summary>
        /// <param name="databasePath">The path of the SQLite notebook database.</param>
        /// <returns>A new database context owned by the caller.</returns>
        NotebookDbContext CreateDbContext(string databasePath);
    }
}
