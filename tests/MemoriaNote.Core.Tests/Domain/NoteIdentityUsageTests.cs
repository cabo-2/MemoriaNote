using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>
/// Verifies that note comparisons use normalized note identifiers where identity is required.
/// </summary>
[TestFixture]
public sealed class NoteIdentityUsageTests
{
    /// <summary>
    /// Verifies that a second object for the same note is excluded from duplicate-name checks.
    /// </summary>
    [Test]
    public void ValidateName_SameNoteIdInDifferentObject_IsExcluded()
    {
        var dataSource = Path.Combine(
            Path.GetTempPath(),
            $"same-note-id-{Guid.NewGuid():N}.db");
        var note = new Note(dataSource);
        var sameNote = new Note(dataSource);
        var workgroup = new Workgroup();
        workgroup.Notes.Add(note);
        workgroup.Notes.Add(sameNote);
        var tracker = DataSourceTracker.Create("valid-name", dataSource);
        var errors = new List<string>();
        Action validate = () => tracker.ValidateName(note, workgroup, ref errors);

        Assert.That(validate, Throws.Nothing);
        Assert.That(errors, Is.Empty);
    }
}
