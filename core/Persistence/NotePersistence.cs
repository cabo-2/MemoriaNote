namespace MemoriaNote
{
    /// <summary>
    /// Creates the default SQLite persistence services used by the application.
    /// </summary>
    public static class NotePersistence
    {
        /// <summary>
        /// Creates a note migrator configured for the application's SQLite databases.
        /// </summary>
        /// <returns>A configured note migrator.</returns>
        public static INotebookMigrator CreateMigrator()
        {
            var databaseFactory =
                new SqliteNotebookDbContextFactory(NotebookDbContext.MyLoggerFactory);
            var metadataRepository =
                new SqliteNotebookMetadataRepository(databaseFactory);
            return new SqliteNotebookMigrator(databaseFactory, metadataRepository);
        }

        /// <summary>
        /// Creates read model maintenance configured for the application's SQLite databases.
        /// </summary>
        /// <returns>Configured read model maintenance.</returns>
        public static INotebookReadModelMaintenance CreateReadModelMaintenance()
        {
            return new SqliteNotebookReadModelMaintenance(
                new SqliteNotebookDbContextFactory(NotebookDbContext.MyLoggerFactory));
        }
    }
}
