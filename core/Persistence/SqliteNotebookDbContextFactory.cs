using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MemoriaNote
{
    /// <summary>
    /// Creates notebook database contexts configured for SQLite files.
    /// </summary>
    public sealed class SqliteNotebookDbContextFactory : INotebookDbContextFactory
    {
        readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteNotebookDbContextFactory"/> class.
        /// </summary>
        /// <param name="loggerFactory">The shared logger factory used by created contexts.</param>
        public SqliteNotebookDbContextFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        /// <inheritdoc/>
        public NotebookDbContext CreateDbContext(string databasePath)
        {
            if (databasePath == null)
                throw new ArgumentNullException(nameof(databasePath));
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("The data source path cannot be empty.", nameof(databasePath));
            if (string.Equals(databasePath, ":memory:", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The data source must be a SQLite file path.", nameof(databasePath));

            var normalizedDatabasePath = Path.GetFullPath(databasePath);
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = normalizedDatabasePath
            }.ToString();
            var options = new DbContextOptionsBuilder<NotebookDbContext>()
                .UseLoggerFactory(_loggerFactory)
                .UseSqlite(connectionString)
                .Options;

            return new NotebookDbContext(options)
            {
                DatabasePath = normalizedDatabasePath
            };
        }
    }
}
