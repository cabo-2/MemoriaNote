using System.IO.Compression;
using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies cancellation, validation, and cleanup guarantees for notebook transfers.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class NotebookTransferFailureTests
{
    /// <summary>
    /// Verifies that all transfer entry points honor an already-canceled caller token.
    /// </summary>
    [Test]
    public void TransferOperations_WhenCallerTokenIsCanceled_DoNotCreateOutputs()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("cancel", "Canceled transfer");
        var importDirectory = Path.Combine(database.DirectoryPath, "import");
        var exportDirectory = Path.Combine(database.DirectoryPath, "export");
        var restoreDirectory = Path.Combine(database.DirectoryPath, "restore");
        Directory.CreateDirectory(importDirectory);
        Directory.CreateDirectory(exportDirectory);
        Directory.CreateDirectory(restoreDirectory);
        File.WriteAllText(Path.Combine(importDirectory, "Entry.txt"), "not imported");
        var backupPath = Path.Combine(database.DirectoryPath, "cancel.json.zip");
        File.WriteAllText(backupPath, "not read");
        var newBackupPath = Path.Combine(database.DirectoryPath, "new.json.zip");
        var services = CreateServices();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            (Func<Task>)(async () => await services.Importer.ImportAsync(
                GetNotebookId(notebook),
                importDirectory,
                recursive: false,
                cancellation.Token)));
        Assert.ThrowsAsync<OperationCanceledException>(
            (Func<Task>)(async () => await services.Exporter.ExportAsync(
                GetNotebookId(notebook),
                exportDirectory,
                cancellation.Token)));
        Assert.ThrowsAsync<OperationCanceledException>(
            (Func<Task>)(async () => await services.Backup.CreateBackupAsync(
                GetNotebookId(notebook),
                newBackupPath,
                cancellation.Token)));
        Assert.ThrowsAsync<OperationCanceledException>(
            (Func<Task>)(async () => await services.Backup.RestoreBackupAsync(
                backupPath,
                restoreDirectory,
                cancellation.Token)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notebook.Count, Is.Zero);
            Assert.That(Directory.GetFiles(exportDirectory), Is.Empty);
            Assert.That(File.Exists(newBackupPath), Is.False);
            Assert.That(Directory.GetFiles(restoreDirectory), Is.Empty);
        }
    }

    /// <summary>
    /// Verifies that a missing metadata document is reported before a database is created.
    /// </summary>
    [Test]
    public void RestoreBackup_WhenMetadataIsMissing_ThrowsInvalidDataAndCreatesNoDatabase()
    {
        using var database = new TemporaryNotebookDatabase();
        var archivePath = Path.Combine(database.DirectoryPath, "missing-metadata.json.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "1.json", JsonConvert.SerializeObject(new Page()));
        }

        var restoreDirectory = Path.Combine(database.DirectoryPath, "restore");
        Directory.CreateDirectory(restoreDirectory);
        var services = CreateServices();

        Assert.ThrowsAsync<InvalidDataException>(
            (Func<Task>)(async () => await services.Backup.RestoreBackupAsync(
                archivePath,
                restoreDirectory,
                CancellationToken.None)));
        Assert.That(Directory.GetFiles(restoreDirectory), Is.Empty);
    }

    /// <summary>
    /// Verifies that malformed page JSON is reported before a database is created.
    /// </summary>
    [Test]
    public void RestoreBackup_WhenPageJsonIsMalformed_ThrowsInvalidDataAndCreatesNoDatabase()
    {
        using var database = new TemporaryNotebookDatabase();
        var archivePath = Path.Combine(database.DirectoryPath, "malformed-page.json.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(
                archive,
                "metadata.json",
                JsonConvert.SerializeObject(new[]
                {
                    new NoteKeyValue { Key = NoteKeyValue.Name, Value = "malformed" },
                    new NoteKeyValue { Key = NoteKeyValue.Title, Value = "Malformed" }
                }));
            WriteEntry(archive, "1.json", "{ this is not valid JSON");
        }

        var restoreDirectory = Path.Combine(database.DirectoryPath, "restore");
        Directory.CreateDirectory(restoreDirectory);
        var services = CreateServices();

        Assert.ThrowsAsync<InvalidDataException>(
            (Func<Task>)(async () => await services.Backup.RestoreBackupAsync(
                archivePath,
                restoreDirectory,
                CancellationToken.None)));
        Assert.That(Directory.GetFiles(restoreDirectory), Is.Empty);
    }

    /// <summary>
    /// Verifies that a restore failure removes only the newly created database and sidecars.
    /// </summary>
    [Test]
    public async Task RestoreBackup_WhenPagePersistenceFails_CleansCreatedDatabase()
    {
        using var database = new TemporaryNotebookDatabase();
        var source = database.CreateNotebook("cleanup", "Cleanup restore");
        source.CreatePage("Entry", "Text");
        var archivePath = Path.Combine(database.DirectoryPath, "cleanup.json.zip");
        var workingServices = CreateServices();
        await workingServices.Backup.CreateBackupAsync(
            GetNotebookId(source),
            archivePath,
            CancellationToken.None);

        var restoreDirectory = Path.Combine(database.DirectoryPath, "restore");
        Directory.CreateDirectory(restoreDirectory);
        var existingPath = Path.Combine(restoreDirectory, "cleanup.db");
        await File.WriteAllTextAsync(existingPath, "keep this file");
        var databaseFactory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var metadataRepository = new SqliteNotebookMetadataRepository(databaseFactory);
        var failingService = new NotebookBackupService(
            new FailingTransferRepository(),
            metadataRepository,
            new SqliteNotebookMigrator(databaseFactory, metadataRepository),
            new NotebookFilePathFactory());

        Assert.ThrowsAsync<InvalidOperationException>(
            (Func<Task>)(async () => await failingService.RestoreBackupAsync(
                archivePath,
                restoreDirectory,
                CancellationToken.None)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllTextAsync(existingPath), Is.EqualTo("keep this file"));
            Assert.That(
                Directory.GetFiles(restoreDirectory).Select(Path.GetFileName),
                Is.EqualTo(new[] { "cleanup.db" }));
        }
    }

    /// <summary>
    /// Verifies that backup creation never replaces an existing archive.
    /// </summary>
    [Test]
    public void CreateBackup_WhenOutputExists_PreservesExistingFile()
    {
        using var database = new TemporaryNotebookDatabase();
        var source = database.CreateNotebook("existing", "Existing backup");
        var archivePath = Path.Combine(database.DirectoryPath, "existing.json.zip");
        File.WriteAllText(archivePath, "keep this archive");
        var services = CreateServices();

        Assert.ThrowsAsync<IOException>(
            (Func<Task>)(async () => await services.Backup.CreateBackupAsync(
                GetNotebookId(source),
                archivePath,
                CancellationToken.None)));
        Assert.That(File.ReadAllText(archivePath), Is.EqualTo("keep this archive"));
    }

    private static TransferServices CreateServices()
    {
        var databaseFactory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var pageRepository = new SqlitePageRepository(databaseFactory);
        var metadataRepository = new SqliteNotebookMetadataRepository(databaseFactory);
        var transferRepository = new SqliteNotebookTransferRepository(databaseFactory);
        var migrator = new SqliteNotebookMigrator(databaseFactory, metadataRepository);
        return new TransferServices(
            new TextPageImporter(pageRepository),
            new TextPageExporter(transferRepository),
            new NotebookBackupService(
                transferRepository,
                metadataRepository,
                migrator,
                new NotebookFilePathFactory()));
    }

    private static NotebookId GetNotebookId(Notebook notebook)
    {
        return NotebookId.FromDatabasePath(notebook.DatabasePath);
    }

    private static void WriteEntry(ZipArchive archive, string name, string contents)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(contents);
    }

    private sealed class FailingTransferRepository : INotebookTransferRepository
    {
        public Task<IReadOnlyList<Page>> ListPagesAsync(
            NotebookId notebookId,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task AddPagesAsync(
            NotebookId notebookId,
            IReadOnlyCollection<Page> pages,
            CancellationToken token)
        {
            throw new InvalidOperationException("Simulated page persistence failure.");
        }
    }

    private sealed record TransferServices(
        TextPageImporter Importer,
        TextPageExporter Exporter,
        NotebookBackupService Backup);
}
