using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies SQLite note database lifecycle operations through the migration boundary.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class SqliteNoteMigratorTests
{
    private const string InitialMigration = "20230404004629_InitialCreate";
    private const string FtsMigration = "20230404004813_FtsIndexTable";

    /// <summary>
    /// Verifies that creation applies the existing migrations and initializes metadata.
    /// </summary>
    [Test]
    public async Task CreateAsync_CreatesMigratedDatabaseAndRequiredMetadata()
    {
        using var database = new TemporaryNoteDatabase();
        var (migrator, factory, _) = CreatePersistence();

        var note = await migrator.CreateAsync(
            "created-note",
            "Created Note",
            database.DatabasePath,
            CancellationToken.None);

        using var context = factory.Create(database.DatabasePath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(database.DatabasePath), Is.True);
            Assert.That(note.DataSource, Is.EqualTo(Path.GetFullPath(database.DatabasePath)));
            Assert.That(note.Metadata.Name, Is.EqualTo("created-note"));
            Assert.That(note.Metadata.Title, Is.EqualTo("Created Note"));
            Assert.That(note.Metadata.Version, Is.EqualTo(NoteDbContext.CurrentVersion));
            Assert.That(
                context.Database.GetAppliedMigrations(),
                Is.EqualTo(new[] { InitialMigration, FtsMigration }));
        }
    }

    /// <summary>
    /// Verifies that creation never overwrites an existing output file.
    /// </summary>
    [Test]
    public async Task CreateAsync_WhenOutputExists_PreservesExistingFile()
    {
        using var database = new TemporaryNoteDatabase();
        await File.WriteAllTextAsync(database.DatabasePath, "existing content");
        var (migrator, _, _) = CreatePersistence();

        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            await migrator.CreateAsync(
                "created-note",
                "Created Note",
                database.DatabasePath,
                CancellationToken.None)));

        Assert.That(
            await File.ReadAllTextAsync(database.DatabasePath),
            Is.EqualTo("existing content"));
    }

    /// <summary>
    /// Verifies that cancellation is observed before creating the output file.
    /// </summary>
    [Test]
    public void CreateAsync_WhenPreCancelled_DoesNotCreateDatabase()
    {
        using var database = new TemporaryNoteDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var (migrator, _, _) = CreatePersistence();

        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            await migrator.CreateAsync(
                "created-note",
                "Created Note",
                database.DatabasePath,
                cancellation.Token)));
        Assert.That(File.Exists(database.DatabasePath), Is.False);
    }

    /// <summary>
    /// Verifies that creation removes a partial database when metadata initialization fails.
    /// </summary>
    [Test]
    public void CreateAsync_WhenMetadataInitializationFails_RemovesPartialDatabase()
    {
        using var database = new TemporaryNoteDatabase();
        var factory = new SqliteNoteDatabaseFactory(NullLoggerFactory.Instance);
        var migrator = new SqliteNoteMigrator(
            factory,
            new FailingMetadataRepository());

        Assert.ThrowsAsync<IOException>(new Func<Task>(async () =>
            await migrator.CreateAsync(
                "created-note",
                "Created Note",
                database.DatabasePath,
                CancellationToken.None)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(database.DatabasePath), Is.False);
            Assert.That(File.Exists(database.DatabasePath + "-journal"), Is.False);
            Assert.That(File.Exists(database.DatabasePath + "-shm"), Is.False);
            Assert.That(File.Exists(database.DatabasePath + "-wal"), Is.False);
        }
    }

    /// <summary>
    /// Verifies that migrating a missing database fails without creating a file.
    /// </summary>
    [Test]
    public void MigrateAsync_WhenDatabaseIsMissing_DoesNotCreateDatabase()
    {
        using var database = new TemporaryNoteDatabase();
        var (migrator, _, _) = CreatePersistence();

        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            await migrator.MigrateAsync(
                database.DatabasePath,
                CancellationToken.None)));
        Assert.That(File.Exists(database.DatabasePath), Is.False);
    }

    /// <summary>
    /// Verifies that repeatedly migrating a current database preserves data and updates version metadata.
    /// </summary>
    [Test]
    public async Task MigrateAsync_WhenRepeated_PreservesDataAndUpdatesVersion()
    {
        using var database = new TemporaryNoteDatabase();
        var (migrator, _, metadataRepository) = CreatePersistence();
        var note = await migrator.CreateAsync(
            "existing-note",
            "Existing Note",
            database.DatabasePath,
            CancellationToken.None);
        var page = note.CreatePage("Entry", "Preserved text");
        await metadataRepository.UpdateAsync(
            database.DatabasePath,
            new NoteMetadataUpdate().SetVersion("old-version"),
            CancellationToken.None);

        await migrator.MigrateAsync(database.DatabasePath, CancellationToken.None);
        await migrator.MigrateAsync(database.DatabasePath, CancellationToken.None);

        var reopened = new Note(database.DatabasePath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reopened.Metadata.Version, Is.EqualTo(NoteDbContext.CurrentVersion));
            Assert.That(reopened.ReadPage(page.Guid)?.Text, Is.EqualTo("Preserved text"));
        }
    }

    /// <summary>
    /// Verifies that a failed SQLite migration leaves existing data and version metadata retryable.
    /// </summary>
    [Test]
    public async Task MigrateAsync_WhenMigrationFails_PreservesExistingState()
    {
        using var database = new TemporaryNoteDatabase();
        var (migrator, factory, _) = CreatePersistence();
        using (var context = factory.Create(database.DatabasePath))
        {
            var efMigrator = context.GetService<IMigrator>();
            await efMigrator.MigrateAsync(InitialMigration);
            context.Metadata.AddRange(
                new NoteKeyValue { Key = NoteKeyValue.Name, Value = "existing-note" },
                new NoteKeyValue { Key = NoteKeyValue.Title, Value = "Existing Note" },
                new NoteKeyValue { Key = NoteKeyValue.Version, Value = "old-version" });
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE FtsIndex (Marker TEXT NOT NULL);");
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO FtsIndex (Marker) VALUES ('preserved');");
            await context.SaveChangesAsync();
        }

        Assert.ThrowsAsync<SqliteException>(new Func<Task>(async () =>
            await migrator.MigrateAsync(
                database.DatabasePath,
                CancellationToken.None)));

        using var verification = factory.Create(database.DatabasePath);
        var version = await verification.Metadata
            .Where(value => value.Key == NoteKeyValue.Version)
            .Select(value => value.Value)
            .SingleAsync();
        await verification.Database.OpenConnectionAsync();
        using var command = verification.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT Marker FROM FtsIndex";
        var marker = await command.ExecuteScalarAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(version, Is.EqualTo("old-version"));
            Assert.That(marker, Is.EqualTo("preserved"));
            Assert.That(
                verification.Database.GetAppliedMigrations(),
                Is.EqualTo(new[] { InitialMigration }));
        }
    }

    private static (
        INoteMigrator Migrator,
        INoteDatabaseFactory Factory,
        INoteMetadataRepository MetadataRepository) CreatePersistence()
    {
        var factory = new SqliteNoteDatabaseFactory(NullLoggerFactory.Instance);
        var metadataRepository = new SqliteNoteMetadataRepository(factory);
        return (
            new SqliteNoteMigrator(factory, metadataRepository),
            factory,
            metadataRepository);
    }

    private sealed class FailingMetadataRepository : INoteMetadataRepository
    {
        public Task<MetadataLoadResult> LoadAsync(
            string dataSource,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<MetadataLoadResult> UpdateAsync(
            string dataSource,
            NoteMetadataUpdate update,
            CancellationToken token)
        {
            throw new IOException("Forced metadata initialization failure.");
        }
    }
}
