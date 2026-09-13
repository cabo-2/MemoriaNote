using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies SQLite note database context creation through the persistence factory.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class NotebookDbContextFactoryTests
{
    readonly INotebookDbContextFactory _factory =
        new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);

    /// <summary>
    /// Verifies that invalid note data source paths are rejected before a context is created.
    /// </summary>
    [Test]
    public void CreateDbContext_InvalidDatabasePath_ThrowsArgumentException()
    {
        Action createWithNull = () => _factory.CreateDbContext(null!);
        Action createWithEmpty = () => _factory.CreateDbContext(string.Empty);
        Action createWithWhitespace = () => _factory.CreateDbContext("   ");
        Action createWithMemoryDataSource = () => _factory.CreateDbContext(":memory:");

        Assert.Throws<ArgumentNullException>(createWithNull);
        Assert.Throws<ArgumentException>(createWithEmpty);
        Assert.Throws<ArgumentException>(createWithWhitespace);
        Assert.Throws<ArgumentException>(createWithMemoryDataSource);
    }

    /// <summary>
    /// Verifies that the factory normalizes paths and safely builds SQLite connection strings.
    /// </summary>
    [Test]
    public void CreateDbContext_RelativeSegmentsAndConnectionStringCharacters_NormalizesPath()
    {
        using var database = new TemporaryNotebookDatabase();
        var dataSource = Path.Combine(database.DirectoryPath, ".", "factory;note.db");
        var expectedDataSource = Path.GetFullPath(dataSource);

        using var context = _factory.CreateDbContext(dataSource);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.DatabasePath, Is.EqualTo(expectedDataSource));
            Assert.That(
                context.Database.GetDbConnection().DataSource,
                Is.EqualTo(expectedDataSource));
            Assert.That(File.Exists(expectedDataSource), Is.False);
        }
    }

    /// <summary>
    /// Verifies that externally supplied options are not replaced by legacy configuration.
    /// </summary>
    [Test]
    public void OptionsConstructor_WithConfiguredDatabase_UsesExternalOptions()
    {
        using var database = new TemporaryNotebookDatabase();
        var options = new DbContextOptionsBuilder<NotebookDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath}")
            .Options;

        using var context = new NotebookDbContext(options);
        context.Database.Migrate();

        Assert.That(File.Exists(database.DatabasePath), Is.True);
    }

    /// <summary>
    /// Verifies that contexts for different notes retain independent options and data.
    /// </summary>
    [Test]
    public void CreateDbContext_ForDifferentNotebooks_DoesNotMixDatabases()
    {
        using var database = new TemporaryNotebookDatabase();
        var firstPath = Path.Combine(database.DirectoryPath, "first.db");
        var secondPath = Path.Combine(database.DirectoryPath, "second.db");

        using (var firstContext = _factory.CreateDbContext(firstPath))
        {
            firstContext.Database.Migrate();
            firstContext.Metadata.Add(new NoteKeyValue
            {
                Key = NoteKeyValue.Name,
                Value = "first"
            });
            firstContext.SaveChanges();
        }

        using (var secondContext = _factory.CreateDbContext(secondPath))
        {
            secondContext.Database.Migrate();
            secondContext.Metadata.Add(new NoteKeyValue
            {
                Key = NoteKeyValue.Name,
                Value = "second"
            });
            secondContext.SaveChanges();
        }

        using var reopenedFirstContext = _factory.CreateDbContext(firstPath);
        using var reopenedSecondContext = _factory.CreateDbContext(secondPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                reopenedFirstContext.Metadata.Single().Value,
                Is.EqualTo("first"));
            Assert.That(
                reopenedSecondContext.Metadata.Single().Value,
                Is.EqualTo("second"));
        }
    }

    /// <summary>
    /// Verifies that a database created by the existing API can be opened through the factory.
    /// </summary>
    [Test]
    public void CreateDbContext_ForExistingMigratedNotebook_PreservesSchemaAndMetadata()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("existing", "Existing note");

        using var context = _factory.CreateDbContext(database.DatabasePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                context.Database.GetAppliedMigrations(),
                Is.EqualTo(new[]
                {
                    "20230404004629_InitialCreate",
                    "20230404004813_FtsIndexTable"
                }));
            Assert.That(
                context.Metadata.Single(value => value.Key == NoteKeyValue.Name).Value,
                Is.EqualTo("existing"));
        }
    }

    /// <summary>
    /// Verifies that the renamed context model introduces no operations beyond the existing
    /// migration snapshot.
    /// </summary>
    [Test]
    public void Model_MatchesExistingMigrationSnapshot()
    {
        using var database = new TemporaryNotebookDatabase();
        using var context = _factory.CreateDbContext(database.DatabasePath);
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var modelDiffer = context.GetService<IMigrationsModelDiffer>();
        var modelInitializer = context.GetService<IModelRuntimeInitializer>();
        var snapshotModel = modelInitializer.Initialize(
            migrationsAssembly.ModelSnapshot!.Model,
            designTime: true);
        var currentModel = context.GetService<IDesignTimeModel>().Model;

        var operations = modelDiffer.GetDifferences(
            snapshotModel.GetRelationalModel(),
            currentModel.GetRelationalModel());

        Assert.That(operations, Is.Empty);
    }

    /// <summary>
    /// Verifies that disposing a factory-created context prevents further database access.
    /// </summary>
    [Test]
    public void Dispose_FactoryCreatedContext_PreventsFurtherUse()
    {
        using var database = new TemporaryNotebookDatabase();
        var context = _factory.CreateDbContext(database.DatabasePath);
        context.Dispose();
        Action accessDisposedContext = () => context.Database.OpenConnection();

        Assert.Throws<ObjectDisposedException>(accessDisposedContext);
    }
}
