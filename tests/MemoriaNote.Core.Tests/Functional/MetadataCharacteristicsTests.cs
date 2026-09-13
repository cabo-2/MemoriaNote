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
    public void CreateNote_PersistsInitialMetadataAsSnapshot()
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
            Assert.That(reopened.Metadata.CreateTime, Is.EqualTo(default(DateTime)));
            Assert.That(reopened.MetadataIssues, Is.Empty);
            Assert.That(
                storedMetadata,
                Is.EqualTo(new Dictionary<string, string>
                {
                    [NoteKeyValue.Name] = "test-note",
                    [NoteKeyValue.Title] = "Test Note",
                    [NoteKeyValue.Version] = NoteDbContext.CurrentVersion
                }));
        }

    }

    /// <summary>
    /// Verifies that one explicit metadata update persists all requested values.
    /// </summary>
    [Test]
    public void UpdateMetadata_PersistsValuesAsOneSnapshot()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var createTime = new DateTime(2026, 1, 2, 9, 10, 11);

        note.UpdateMetadata(new NoteMetadataUpdate()
            .SetName("renamed-note")
            .SetTitle("Renamed Note")
            .SetVersion("characterized-version")
            .SetDescription("Metadata description")
            .SetAuthor("Test Author")
            .SetReadOnly(true)
            .SetTag("baseline")
            .SetCreateTime(createTime));

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
            Assert.That(reopened.Metadata.CreateTime, Is.EqualTo(createTime));
            Assert.That(storedCreateTime, Is.EqualTo("20260102091011"));
        }
    }

    /// <summary>
    /// Verifies that snapshot operations do not access the database after loading.
    /// </summary>
    [Test]
    public void LoadedSnapshot_AfterDatabaseDeletion_RemainsUsable()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("offline-note", "Offline Note");
        note.UpdateMetadata(new NoteMetadataUpdate()
            .SetTag("offline")
            .SetCreateTime(new DateTime(2026, 2, 3, 4, 5, 6)));
        var snapshot = note.Metadata;
        var tracker = DataSourceTracker.Create(snapshot);
        var workspace = new Workspace(null, new[] { note });
        var errors = new List<string>();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(database.DatabasePath);

        tracker.ValidateName(note, workspace, ref errors);
        tracker.ValidateTitle(note, workspace, ref errors);
        var clone = snapshot.Clone();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.Name, Is.EqualTo("offline-note"));
            Assert.That(snapshot.Title, Is.EqualTo("Offline Note"));
            Assert.That(snapshot.ToString(), Is.EqualTo("offline-note:offline"));
            Assert.That(clone, Is.EqualTo(snapshot));
            Assert.That(clone.GetHashCode(), Is.EqualTo(snapshot.GetHashCode()));
            Assert.That(errors, Is.Empty);
        }
    }
}
