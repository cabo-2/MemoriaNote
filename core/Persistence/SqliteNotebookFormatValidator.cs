using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MemoriaNote.Models;

namespace MemoriaNote.Persistence
{
    /// <summary>Validates the schema and metadata of SQLite live notebooks.</summary>
    public sealed class SqliteNotebookFormatValidator : INotebookFormatValidator
    {
        readonly INotebookDbContextFactory _databaseFactory;
        readonly INotebookMetadataRepository _metadataRepository;

        /// <summary>Initializes a current-format notebook validator.</summary>
        /// <param name="databaseFactory">The database context factory.</param>
        /// <param name="metadataRepository">The notebook metadata repository.</param>
        public SqliteNotebookFormatValidator(
            INotebookDbContextFactory databaseFactory,
            INotebookMetadataRepository metadataRepository)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
        }

        /// <inheritdoc/>
        public async Task<NotebookMetadataResult> ValidateCurrentAsync(
            string databasePath,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            var normalizedPath = context.DatabasePath;
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException(
                    $"The notebook file does not exist: {normalizedPath}",
                    normalizedPath);
            }

            try
            {
                var metadata = await _metadataRepository
                    .LoadAsync(normalizedPath, token)
                    .ConfigureAwait(false);
                var expectedMigrations = context.Database.GetMigrations().ToArray();
                var appliedMigrations = (await context.Database
                        .GetAppliedMigrationsAsync(token)
                        .ConfigureAwait(false))
                    .ToArray();
                if (metadata.HasIssues)
                    throw InvalidFormat(normalizedPath);
                if (IsNewerFormatVersion(metadata.Metadata.Version))
                {
                    throw new UnsupportedNotebookFormatVersionException(
                        metadata.Metadata.Version);
                }
                if (metadata.Metadata.Version != NotebookDbContext.CurrentVersion ||
                    !appliedMigrations.SequenceEqual(expectedMigrations))
                {
                    throw InvalidFormat(normalizedPath);
                }

                return metadata;
            }
            catch (SqliteException exception) when (
                exception.SqliteErrorCode == 1 ||
                exception.SqliteErrorCode == 11 ||
                exception.SqliteErrorCode == 26)
            {
                throw InvalidFormat(normalizedPath, exception);
            }
        }

        static bool IsNewerFormatVersion(string formatVersion)
        {
            return long.TryParse(
                    formatVersion,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedVersion) &&
                long.TryParse(
                    NotebookDbContext.CurrentVersion,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var currentVersion) &&
                parsedVersion > currentVersion;
        }

        static InvalidDataException InvalidFormat(
            string databasePath,
            Exception innerException = null)
        {
            return new InvalidDataException(
                $"The file is not a current Memoria Note notebook: {databasePath}",
                innerException);
        }
    }
}
