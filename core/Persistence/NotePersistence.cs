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
        public static INoteMigrator CreateMigrator()
        {
            var databaseFactory =
                new SqliteNoteDatabaseFactory(NoteDbContext.MyLoggerFactory);
            var metadataRepository =
                new SqliteNoteMetadataRepository(databaseFactory);
            return new SqliteNoteMigrator(databaseFactory, metadataRepository);
        }

        /// <summary>
        /// Creates read model maintenance configured for the application's SQLite databases.
        /// </summary>
        /// <returns>Configured read model maintenance.</returns>
        public static INoteReadModelMaintenance CreateReadModelMaintenance()
        {
            return new SqliteNoteReadModelMaintenance(
                new SqliteNoteDatabaseFactory(NoteDbContext.MyLoggerFactory));
        }
    }
}
