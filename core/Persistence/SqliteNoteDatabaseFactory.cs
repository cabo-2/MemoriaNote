using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MemoriaNote
{
    /// <summary>
    /// Creates note database contexts configured for SQLite files.
    /// </summary>
    public sealed class SqliteNoteDatabaseFactory : INoteDatabaseFactory
    {
        readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteNoteDatabaseFactory"/> class.
        /// </summary>
        /// <param name="loggerFactory">The shared logger factory used by created contexts.</param>
        public SqliteNoteDatabaseFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        /// <inheritdoc/>
        public NoteDbContext Create(string dataSource)
        {
            if (dataSource == null)
                throw new ArgumentNullException(nameof(dataSource));
            if (string.IsNullOrWhiteSpace(dataSource))
                throw new ArgumentException("The data source path cannot be empty.", nameof(dataSource));
            if (string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The data source must be a SQLite file path.", nameof(dataSource));

            var normalizedDataSource = Path.GetFullPath(dataSource);
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = normalizedDataSource
            }.ToString();
            var options = new DbContextOptionsBuilder<NoteDbContext>()
                .UseLoggerFactory(_loggerFactory)
                .UseSqlite(connectionString)
                .Options;

            return new NoteDbContext(options)
            {
                DataSource = normalizedDataSource
            };
        }
    }
}
