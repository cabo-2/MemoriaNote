using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Captures the current text export and text import behavior.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class NotebookTransferCharacteristicsTests
{
    /// <summary>
    /// Verifies that text export and import use one flat directory.
    /// </summary>
    [Test]
    public async Task TextExportImport_RoundTripsEveryPageThroughOneFlatDirectory()
    {
        using var database = new TemporaryNotebookDatabase();
        var source = database.CreateNotebook("source", "Source Note");
        var overview = source.CreatePage("Overview", "Root text");
        var meeting = source.CreatePage("Meeting", "Nested text", "work/2026");
        var exportDirectory = Path.Combine(database.DirectoryPath, "text-export");

        var services = CreateServices();
        var exportResult = await services.Exporter.ExportAsync(
            new TextPageExportRequest(
                GetNotebookId(source),
                exportDirectory,
                TextPageExportNameConflictPolicy.Fail),
            CancellationToken.None);

        var overviewPath = Path.Combine(exportDirectory, "Overview.txt");
        var meetingPath = Path.Combine(exportDirectory, "Meeting.txt");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exportResult.IsSuccess, Is.True);
            Assert.That(exportResult.ExportedCount, Is.EqualTo(2));
            Assert.That(await File.ReadAllTextAsync(overviewPath), Is.EqualTo("Root text"));
            Assert.That(await File.ReadAllTextAsync(meetingPath), Is.EqualTo("Nested text"));
        }

        var imported = database.CreateNotebook(
            "imported",
            "Imported Note",
            Path.Combine(database.DirectoryPath, "imported.db"));
        var result = await services.Importer.ImportAsync(
            new TextPageImportRequest(
                GetNotebookId(imported),
                exportDirectory,
                TextPageImportConflictPolicy.Fail),
            CancellationToken.None);
        var importedOverview = imported.ReadPage("Overview", 1);
        var importedMeeting = imported.ReadPage("Meeting", 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.CreatedCount, Is.EqualTo(2));
            Assert.That(imported.CountPages(), Is.EqualTo(2));
            Assert.That(importedOverview.Text, Is.EqualTo("Root text"));
            Assert.That(importedOverview.TagDict, Does.Not.ContainKey(PageTag.Dir));
            Assert.That(importedOverview.Guid, Is.Not.EqualTo(overview.Guid));
            Assert.That(importedMeeting.Text, Is.EqualTo("Nested text"));
            Assert.That(importedMeeting.TagDict, Does.Not.ContainKey(PageTag.Dir));
            Assert.That(importedMeeting.Guid, Is.Not.EqualTo(meeting.Guid));
        }
    }

    /// <summary>
    /// Verifies portable encoding preserves reserved names, unsafe characters, and literal lookalikes.
    /// </summary>
    [Test]
    public async Task TextExportImport_PortableNames_RoundTripWithoutLookalikeReplacement()
    {
        using var database = new TemporaryNotebookDatabase();
        var source = database.CreateNotebook("source", "Source Note");
        source.CreatePage("CON", "Reserved name");
        source.CreatePage("Plan/2026", "Slash name");
        source.CreatePage("Literal／Slash", "Full-width slash name");
        var exportDirectory = Path.Combine(database.DirectoryPath, "portable-export");

        var services = CreateServices();
        var exportResult = await services.Exporter.ExportAsync(
            new TextPageExportRequest(
                GetNotebookId(source),
                exportDirectory,
                TextPageExportNameConflictPolicy.Fail),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exportResult.IsSuccess, Is.True);
            Assert.That(
                File.Exists(Path.Combine(
                    exportDirectory,
                    "CON~mn~2~.txt")),
                Is.True);
            Assert.That(
                File.Exists(Path.Combine(exportDirectory, "Plan%2F2026~mn~2~.txt")),
                Is.True);
            Assert.That(
                File.Exists(Path.Combine(exportDirectory, "Literal／Slash.txt")),
                Is.True);
        }

        var imported = database.CreateNotebook(
            "imported-portable",
            "Imported Portable Note",
            Path.Combine(database.DirectoryPath, "imported-portable.db"));
        var result = await services.Importer.ImportAsync(
            new TextPageImportRequest(
                GetNotebookId(imported),
                exportDirectory,
                TextPageImportConflictPolicy.Fail),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(imported.ReadPage("CON", 1).Text, Is.EqualTo("Reserved name"));
            Assert.That(imported.ReadPage("Plan/2026", 1).Text, Is.EqualTo("Slash name"));
            Assert.That(
                imported.ReadPage("Literal／Slash", 1).Text,
                Is.EqualTo("Full-width slash name"));
        }
    }

    /// <summary>
    /// Verifies that the default policy rejects an existing exact page name atomically.
    /// </summary>
    [Test]
    public async Task TextImport_WhenNameAlreadyExists_RejectsCompleteImport()
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("target", "Target Note");
        var existing = note.CreatePage("Daily", "Existing text");
        var importDirectory = Path.Combine(database.DirectoryPath, "text-import");
        Directory.CreateDirectory(importDirectory);
        await File.WriteAllTextAsync(Path.Combine(importDirectory, "Daily.txt"), "Imported text");

        var services = CreateServices();
        var result = await services.Importer.ImportAsync(
            new TextPageImportRequest(
                GetNotebookId(note),
                importDirectory,
                TextPageImportConflictPolicy.Fail),
            CancellationToken.None);
        var pages = note.ReadPage("Daily").OrderBy(page => page.Index).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo(TextPageImportErrorCode.ExistingPageConflict));
            Assert.That(pages, Has.Count.EqualTo(1));
            Assert.That(pages[0].Guid, Is.EqualTo(existing.Guid));
            Assert.That(pages[0].Text, Is.EqualTo("Existing text"));
        }
    }

    /// <summary>
    /// Verifies duplicate page names reject the export without publishing a directory.
    /// </summary>
    [Test]
    public async Task TextExport_WhenNamesShareAPath_RejectsWithoutPublishing()
    {
        using var database = new TemporaryNotebookDatabase();
        var note = database.CreateNotebook("source", "Source Note");
        note.CreatePage("Daily", "First text");
        note.CreatePage("Daily", "Second text");
        var exportDirectory = Path.Combine(database.DirectoryPath, "duplicate-export");

        var services = CreateServices();
        var result = await services.Exporter.ExportAsync(
            new TextPageExportRequest(
                GetNotebookId(note),
                exportDirectory,
                TextPageExportNameConflictPolicy.Fail),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(TextPageExportErrorCode.NameConflict));
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
