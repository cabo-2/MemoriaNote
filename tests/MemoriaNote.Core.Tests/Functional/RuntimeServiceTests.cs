using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>Verifies injectable clocks and process-owned temporary files.</summary>
[TestFixture]
[Category("Functional")]
public sealed class RuntimeServiceTests
{
    /// <summary>Verifies that generated names use UTC, 24-hour time, and collision suffixes.</summary>
    [Test]
    public void NotebookPaths_FixedClock_UseUtcAndAvoidCollisions()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FixedClock(
            new DateTimeOffset(2026, 9, 14, 13, 4, 5, TimeSpan.Zero));
        var factory = new NotebookFilePathFactory(clock);
        Directory.CreateDirectory(directory.Path);

        var firstBackup = factory.CreateBackupPath(directory.Path, "notes");
        File.WriteAllText(firstBackup, string.Empty);
        var secondBackup = factory.CreateBackupPath(directory.Path, "notes");
        File.WriteAllText(Path.Combine(directory.Path, "notes.db"), string.Empty);
        var databasePath = factory.CreateDatabasePath(directory.Path, "notes");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Path.GetFileName(firstBackup),
                Is.EqualTo("notes_20260914130405.json.zip"));
            Assert.That(
                Path.GetFileName(secondBackup),
                Is.EqualTo("notes_20260914130405_1.json.zip"));
            Assert.That(
                Path.GetFileName(databasePath),
                Is.EqualTo("notes_20260914130405.db"));
        }
    }

    /// <summary>Verifies that page persistence uses the injected clock exactly.</summary>
    [Test]
    public async Task PageRepository_InjectedClock_ControlsCreateAndUpdateTimes()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("clock", "Clock test");
        var clock = new FixedClock(
            new DateTimeOffset(2026, 9, 14, 1, 2, 3, TimeSpan.Zero));
        var databaseFactory = new SqliteNotebookDbContextFactory(
            NullLoggerFactory.Instance);
        var repository = new SqlitePageRepository(databaseFactory, clock);

        var page = await repository.CreatePageAsync(
            database.DatabasePath,
            "Page",
            "Initial",
            null,
            CancellationToken.None);
        var createdAt = clock.UtcNow.UtcDateTime;
        clock.UtcNow = new DateTimeOffset(2026, 9, 14, 4, 5, 6, TimeSpan.Zero);
        page.Text = "Updated";
        var updated = await repository.UpdatePageAsync(
            database.DatabasePath,
            page,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.CreateTime, Is.EqualTo(createdAt));
            Assert.That(page.UpdateTime, Is.EqualTo(createdAt));
            Assert.That(updated.CreateTime, Is.EqualTo(createdAt));
            Assert.That(updated.UpdateTime, Is.EqualTo(clock.UtcNow.UtcDateTime));
        }
    }

    /// <summary>Verifies that disposing a lease removes only its reserved file.</summary>
    [Test]
    public void TemporaryFileStore_LeaseDisposal_RemovesReservedFile()
    {
        using var directory = new TemporaryDirectory();
        using var store = new TemporaryFileStore(directory.Path);
        var externalPath = Path.Combine(directory.Path, "external.txt");
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(externalPath, "keep");

        var lease = store.CreateFile("entry.md");
        var temporaryPath = lease.Path;

        Assert.That(File.Exists(temporaryPath), Is.True);
        Assert.That(Path.GetExtension(temporaryPath), Is.EqualTo(".md"));

        lease.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(temporaryPath), Is.False);
            Assert.That(File.Exists(externalPath), Is.True);
        }
    }

    /// <summary>Verifies that store disposal cleans every outstanding reservation.</summary>
    [Test]
    public void TemporaryFileStore_StoreDisposal_CleansOutstandingFiles()
    {
        using var directory = new TemporaryDirectory();
        var store = new TemporaryFileStore(directory.Path);
        var first = store.CreateFile("first.txt");
        var second = store.CreateFile("second.txt");

        store.Dispose();
        Action createAfterDisposal = () => store.CreateFile();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(first.Path), Is.False);
            Assert.That(File.Exists(second.Path), Is.False);
            Assert.Throws<ObjectDisposedException>(createAfterDisposal);
        }
    }

    sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MemoriaNote.RuntimeServiceTests",
                Guid.NewGuid().ToString("N"));
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
