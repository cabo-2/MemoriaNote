using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>
/// Verifies that note comparisons use normalized note identifiers where identity is required.
/// </summary>
[TestFixture]
public sealed class NotebookIdentityUsageTests
{
    /// <summary>
    /// Verifies that a second object for the same note is excluded from duplicate-name checks.
    /// </summary>
    [Test]
    public void ValidateName_SameNotebookIdInDifferentObject_IsExcluded()
    {
        var dataSource = Path.Combine(
            Path.GetTempPath(),
            $"same-note-id-{Guid.NewGuid():N}.db");
        var note = new Notebook(dataSource);
        var sameNote = new Notebook(dataSource);
        var workspace = new Workspace(null, new[] { note, sameNote });
        var tracker = DataSourceTracker.Create("valid-name", dataSource);
        var errors = new List<string>();
        Action validate = () => tracker.ValidateName(note, workspace, ref errors);

        Assert.That(validate, Throws.Nothing);
        Assert.That(errors, Is.Empty);
    }
}
