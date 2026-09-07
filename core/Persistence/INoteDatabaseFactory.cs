namespace MemoriaNote
{
    /// <summary>
    /// Creates independently configured database contexts for note data sources.
    /// </summary>
    public interface INoteDatabaseFactory
    {
        /// <summary>
        /// Creates a database context for the specified note data source.
        /// </summary>
        /// <param name="dataSource">The path of the SQLite note database.</param>
        /// <returns>A new database context owned by the caller.</returns>
        NoteDbContext Create(string dataSource);
    }
}
