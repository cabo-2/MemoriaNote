using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Captures the current metadata persistence behavior against SQLite.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class MetadataCharacteristicsTests
{
    /// <summary>
    /// Verifies the metadata values written while creating a note.
    /// </summary>
    [Test]
    public void CreateNote_PersistsInitialMetadataButCreateTimeReadFails()
    {
        using var database = new TemporaryNoteDatabase();

        database.CreateNote("test-note", "Test Note");
        var reopened = new Note(database.DatabasePath);

        using var context = new NoteDbContext(database.DatabasePath);
        var storedMetadata = context.Metadata.ToDictionary(entry => entry.Key, entry => entry.Value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reopened.Metadata.Name, Is.EqualTo("test-note"));
            Assert.That(reopened.Metadata.Title, Is.EqualTo("Test Note"));
            Assert.That(reopened.Metadata.Version, Is.EqualTo(NoteDbContext.CurrentVersion));
            Assert.That(reopened.Metadata.Description, Is.Null);
            Assert.That(reopened.Metadata.Author, Is.Null);
            Assert.That(reopened.Metadata.ReadOnly, Is.False);
            Assert.That(reopened.Metadata.Tag, Is.Null);
            Assert.That(
                storedMetadata,
                Is.EqualTo(new Dictionary<string, string>
                {
                    [NoteKeyValue.Name] = "test-note",
                    [NoteKeyValue.Title] = "Test Note",
                    [NoteKeyValue.Version] = NoteDbContext.CurrentVersion
                }));
        }

        Action readCreateTime = () =>
        {
            _ = reopened.Metadata.CreateTime;
        };
        Assert.Throws<FormatException>(readCreateTime);
    }

    /// <summary>
    /// Verifies that metadata setters persist values for a newly opened note instance.
    /// </summary>
    [Test]
    public void UpdateMetadata_PersistsValuesButCreateTimeReadFails()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var createTime = new DateTime(2026, 1, 2, 9, 10, 11);

        note.Metadata.Name = "renamed-note";
        note.Metadata.Title = "Renamed Note";
        note.Metadata.Version = "characterized-version";
        note.Metadata.Description = "Metadata description";
        note.Metadata.Author = "Test Author";
        note.Metadata.ReadOnly = true;
        note.Metadata.Tag = "baseline";
        note.Metadata.CreateTime = createTime;

        var reopened = new Note(database.DatabasePath);

        using var context = new NoteDbContext(database.DatabasePath);
        var storedCreateTime = context.Metadata.Find(NoteKeyValue.CreateTime)?.Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reopened.Metadata.Name, Is.EqualTo("renamed-note"));
            Assert.That(reopened.Metadata.Title, Is.EqualTo("Renamed Note"));
            Assert.That(reopened.Metadata.Version, Is.EqualTo("characterized-version"));
            Assert.That(reopened.Metadata.Description, Is.EqualTo("Metadata description"));
            Assert.That(reopened.Metadata.Author, Is.EqualTo("Test Author"));
            Assert.That(reopened.Metadata.ReadOnly, Is.True);
            Assert.That(reopened.Metadata.Tag, Is.EqualTo("baseline"));
            Assert.That(storedCreateTime, Is.EqualTo("20260102091011"));
        }

        Action readCreateTime = () =>
        {
            _ = reopened.Metadata.CreateTime;
        };
        Assert.Throws<FormatException>(readCreateTime);
    }
}
