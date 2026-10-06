using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies cancellation guarantees for text page transfers.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class NotebookTransferFailureTests
{
    /// <summary>
    /// Verifies that text transfer entry points honor an already-canceled caller token.
    /// </summary>
    [Test]
    public void TransferOperations_WhenCallerTokenIsCanceled_DoNotCreateOutputs()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("cancel", "Canceled transfer");
        var importDirectory = Path.Combine(database.DirectoryPath, "import");
        var exportDirectory = Path.Combine(database.DirectoryPath, "export");
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(Path.Combine(importDirectory, "Entry.txt"), "not imported");
        var services = CreateServices();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            (Func<Task>)(async () => await services.Importer.ImportAsync(
                new TextPageImportRequest(
                    GetNotebookId(notebook),
                    importDirectory,
                    TextPageImportConflictPolicy.Fail),
                cancellation.Token)));
        Assert.ThrowsAsync<OperationCanceledException>(
            (Func<Task>)(async () => await services.Exporter.ExportAsync(
                new TextPageExportRequest(
                    GetNotebookId(notebook),
                    exportDirectory,
                    TextPageExportNameConflictPolicy.Fail),
                cancellation.Token)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notebook.CountPages(), Is.Zero);
            Assert.That(Directory.Exists(exportDirectory), Is.False);
        }
    }

    private static TransferServices CreateServices()
    {
        var databaseFactory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var transferRepository = new SqliteNotebookTransferRepository(databaseFactory);
        return new TransferServices(
            new TextPageImporter(transferRepository),
            new TextPageExporter(transferRepository));
    }

    private static NotebookId GetNotebookId(Notebook notebook)
    {
        return NotebookId.FromDatabasePath(notebook.DatabasePath);
    }

    private sealed record TransferServices(
        TextPageImporter Importer,
        TextPageExporter Exporter);
}
