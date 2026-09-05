using System.IO.Compression;
using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Captures the current backup, restore, text export, and text import behavior.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class NoteTransferCharacteristicsTests
{
    /// <summary>
    /// Verifies the zip format and the values retained by a backup and restore round trip.
    /// </summary>
    [Test]
    public async Task BackupRestore_RoundTripPreservesPagesAndSelectedMetadata()
    {
        using var database = new TemporaryNoteDatabase();
        var source = database.CreateNote("archive", "Archive Note");
        source.Metadata.Description = "Backup description";
        source.Metadata.Author = "Test Author";
        source.Metadata.ReadOnly = true;
        source.Metadata.Tag = "source-only";
        var first = source.CreatePage("Daily", "First entry", "journal/2026");
        var second = source.CreatePage("Daily", "Second entry", "journal/2026");
        var backupPath = Path.Combine(database.DirectoryPath, "archive.json.zip");
        var restoreDirectory = Path.Combine(database.DirectoryPath, "restored");
        Directory.CreateDirectory(restoreDirectory);

        await NoteUtil.Backup(source, backupPath);

        using (var zip = ZipFile.OpenRead(backupPath))
        {
            Assert.That(
                zip.Entries.Select(entry => entry.FullName),
                Is.EquivalentTo(new[] { "1.json", "2.json", "metadata.json" }));

            var metadata = NoteUtil.DeserializeNoteKeyValues(zip)
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            var pages = NoteUtil.DeserializePages(zip).ToList();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(metadata[NoteKeyValue.Name], Is.EqualTo("archive"));
                Assert.That(metadata[NoteKeyValue.Title], Is.EqualTo("Archive Note"));
                Assert.That(metadata[NoteKeyValue.Description], Is.EqualTo("Backup description"));
                Assert.That(metadata[NoteKeyValue.Author], Is.EqualTo("Test Author"));
                Assert.That(metadata[NoteKeyValue.ReadOnly], Is.EqualTo(bool.TrueString));
                Assert.That(metadata[NoteKeyValue.Tag], Is.EqualTo("source-only"));
                Assert.That(pages, Has.Count.EqualTo(2));
            }
        }

        var restored = await NoteUtil.Restore(backupPath, restoreDirectory);
        var restoredPages = restored.ReadPage("Daily").OrderBy(page => page.Index).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.DataSource, Is.EqualTo(Path.Combine(restoreDirectory, "archive.db")));
            Assert.That(restored.Metadata.Name, Is.EqualTo("archive"));
            Assert.That(restored.Metadata.Title, Is.EqualTo("Archive Note"));
            Assert.That(restored.Metadata.Description, Is.EqualTo("Backup description"));
            Assert.That(restored.Metadata.Author, Is.EqualTo("Test Author"));
            Assert.That(restored.Metadata.ReadOnly, Is.False);
            Assert.That(restored.Metadata.Tag, Is.Null);
            Assert.That(restoredPages, Has.Count.EqualTo(2));
        }

        AssertPage(restoredPages[0], first);
        AssertPage(restoredPages[1], second);
        Assert.That(
            restored.SearchFullText("entry", 0, 10).Contents.Select(content => content.Guid),
            Is.EqualTo(new[] { first.Guid, second.Guid }));
    }

    /// <summary>
    /// Verifies that restoring over a note with the same name chooses a timestamp-suffixed database.
    /// </summary>
    [Test]
    public async Task Restore_WhenDefaultDatabaseExists_UsesTimestampSuffix()
    {
        using var database = new TemporaryNoteDatabase();
        var source = database.CreateNote("duplicate", "Duplicate Note");
        var page = source.CreatePage("Entry", "Restored text");
        var backupPath = Path.Combine(database.DirectoryPath, "duplicate.json.zip");
        var restoreDirectory = Path.Combine(database.DirectoryPath, "restore-duplicate");
        Directory.CreateDirectory(restoreDirectory);
        var existingPath = Path.Combine(restoreDirectory, "duplicate.db");
        await File.WriteAllTextAsync(existingPath, "existing file");
        await NoteUtil.Backup(source, backupPath);

        var restored = await NoteUtil.Restore(backupPath, restoreDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.DataSource, Is.Not.EqualTo(existingPath));
            Assert.That(Path.GetDirectoryName(restored.DataSource), Is.EqualTo(restoreDirectory));
            Assert.That(Path.GetFileName(restored.DataSource), Does.Match(@"^duplicate_\d{14}\.db$"));
            Assert.That(File.Exists(restored.DataSource), Is.True);
            Assert.That(await File.ReadAllTextAsync(existingPath), Is.EqualTo("existing file"));
            Assert.That(restored.ReadPage(page.Guid)?.Text, Is.EqualTo("Restored text"));
        }
    }

    /// <summary>
    /// Verifies that unique root and nested text files survive an export and recursive import.
    /// </summary>
    [Test]
    public async Task TextExportImport_RoundTripPreservesUniqueNamesTextAndDirectories()
    {
        using var database = new TemporaryNoteDatabase();
        var source = database.CreateNote("source", "Source Note");
        var overview = source.CreatePage("Overview", "Root text");
        var meeting = source.CreatePage("Meeting", "Nested text", "work/2026");
        var exportDirectory = Path.Combine(database.DirectoryPath, "text-export");
        Directory.CreateDirectory(exportDirectory);

        await NoteUtil.TextExporter(source, exportDirectory);

        var overviewPath = Path.Combine(exportDirectory, "Overview.txt");
        var meetingPath = Path.Combine(exportDirectory, "work", "2026", "Meeting.txt");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllTextAsync(overviewPath), Is.EqualTo("Root text"));
            Assert.That(await File.ReadAllTextAsync(meetingPath), Is.EqualTo("Nested text"));
        }

        var imported = Note.Create(
            "imported",
            "Imported Note",
            Path.Combine(database.DirectoryPath, "imported.db"));
        await NoteUtil.TextImporter(imported, exportDirectory, recursive: true);
        var importedOverview = imported.ReadPage("Overview", 1);
        var importedMeeting = imported.ReadPage("Meeting", 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(imported.Count, Is.EqualTo(2));
            Assert.That(importedOverview.Text, Is.EqualTo("Root text"));
            Assert.That(importedOverview.TagDict, Does.Not.ContainKey(PageTag.Dir));
            Assert.That(importedOverview.Guid, Is.Not.EqualTo(overview.Guid));
            Assert.That(importedMeeting.Text, Is.EqualTo("Nested text"));
            Assert.That(importedMeeting.TagDict[PageTag.Dir], Is.EqualTo("work/2026"));
            Assert.That(importedMeeting.Guid, Is.Not.EqualTo(meeting.Guid));
        }
    }

    /// <summary>
    /// Verifies that importing an existing page name creates another indexed page.
    /// </summary>
    [Test]
    public async Task TextImport_WhenNameAlreadyExists_CreatesAnotherIndex()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("target", "Target Note");
        var existing = note.CreatePage("Daily", "Existing text");
        var importDirectory = Path.Combine(database.DirectoryPath, "text-import");
        Directory.CreateDirectory(importDirectory);
        await File.WriteAllTextAsync(Path.Combine(importDirectory, "Daily.txt"), "Imported text");

        await NoteUtil.TextImporter(note, importDirectory);
        var pages = note.ReadPage("Daily").OrderBy(page => page.Index).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pages, Has.Count.EqualTo(2));
            Assert.That(pages[0].Guid, Is.EqualTo(existing.Guid));
            Assert.That(pages[0].Text, Is.EqualTo("Existing text"));
            Assert.That(pages[1].Guid, Is.Not.EqualTo(existing.Guid));
            Assert.That(pages[1].Text, Is.EqualTo("Imported text"));
        }
    }

    /// <summary>
    /// Captures that exporting duplicate page names to one directory keeps only the later text.
    /// </summary>
    [Test]
    public async Task TextExport_WhenNamesShareAPath_CurrentlyOverwritesTheEarlierPage()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("source", "Source Note");
        note.CreatePage("Daily", "First text");
        note.CreatePage("Daily", "Second text");
        var exportDirectory = Path.Combine(database.DirectoryPath, "duplicate-export");
        Directory.CreateDirectory(exportDirectory);

        await NoteUtil.TextExporter(note, exportDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(exportDirectory, "*.txt"), Has.Length.EqualTo(1));
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(exportDirectory, "Daily.txt")),
                Is.EqualTo("Second text"));
        }
    }

    private static void AssertPage(Page actual, Page expected)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual.Rowid, Is.EqualTo(expected.Rowid));
            Assert.That(actual.Guid, Is.EqualTo(expected.Guid));
            Assert.That(actual.Name, Is.EqualTo(expected.Name));
            Assert.That(actual.Index, Is.EqualTo(expected.Index));
            Assert.That(actual.Text, Is.EqualTo(expected.Text));
            Assert.That(actual.TagDict, Is.EqualTo(expected.TagDict));
            Assert.That(actual.ContentType, Is.EqualTo(expected.ContentType));
            Assert.That(actual.CreateTime, Is.EqualTo(expected.CreateTime));
            Assert.That(actual.UpdateTime, Is.EqualTo(expected.UpdateTime));
            Assert.That(actual.IsErased, Is.EqualTo(expected.IsErased));
        }
    }
}
